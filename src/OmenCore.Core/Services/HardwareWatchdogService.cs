using OmenCore.Hardware;
using System;
using System.Threading;
using System.Threading.Tasks;
using OmenCore.Services.Diagnostics;

namespace OmenCore.Services
{
    /// <summary>
    /// Hardware watchdog that monitors for frozen temperature sensors.
    /// Automatically reverts to safe fan speeds if temperature monitoring fails.
    /// </summary>
    public class HardwareWatchdogService : IDisposable
    {
        private readonly LoggingService _logging;
        private readonly FanService _fanService;
        private readonly ResumeRecoveryDiagnosticsService _resumeDiagnostics; // Always non-null; STEP-12 Option A
        private readonly object _stateLock = new();

        private Timer? _watchdogTimer;
        private DateTime _lastTempUpdate = DateTime.Now;
        private double _lastCpuTemp = 0;
        private double _lastGpuTemp = 0;
        private bool _isWatchdogArmed = true;
        private bool _failsafeActive;
        private bool _suspendActive;
        private int _consecutiveFreezeBreaches;
        private bool _disposed;
        private DateTime _resumeGraceUntilUtc = DateTime.MinValue;

        // Failsafe release/reassert bookkeeping. Both fixed from a community fork
        // (ujjawalkaushik1110/omencore, "Harden Victus 8DD0 fan reliability and watchdog
        // failsafe") after review found two real gaps in this file, reimplemented here rather
        // than merged as-is:
        //
        //   1. UpdateTemperature released the failsafe on the very NEXT sample regardless of
        //      what that sample actually read - "the pipeline is alive again" was being treated
        //      as "it is safe to hand fans back to BIOS Auto", when the freeze that triggered the
        //      failsafe could just as easily have been the machine sitting at a genuinely high
        //      temperature the whole time. The one thing the failsafe exists to prevent - BIOS
        //      Auto reclaiming a hot machine - was exactly what an inopportunely-timed release
        //      could do. Release now needs FailsafeSafeReleaseSeconds of readings at or below
        //      FailsafeSafeReleaseTempC first - deliberately well under both FanService's real
        //      ~90°C thermal-protection ramp and the 85°C informational-toast threshold, so this
        //      is "genuinely back to normal", not merely "below the emergency line".
        //   2. Once _failsafeActive was set, CheckWatchdog's own freeze-detection branch returned
        //      immediately on every subsequent tick without ever touching the fans again - the
        //      90% speed was applied exactly once. Anything that reset fan state in between (OGH,
        //      a firmware reassert, another controller) could silently undo it with nothing
        //      watching. The failsafe now reapplies itself every FailsafeFanReapplyIntervalSeconds
        //      for as long as it stays active.
        private DateTime _failsafeLastFanApplyUtc = DateTime.MinValue;
        private DateTime _failsafeSafeSinceUtc = DateTime.MinValue;

        private const int WatchdogIntervalMs = 10000; // Check every 10 seconds
        internal const int FreezeThresholdSeconds = 90; // Require longer stall to reduce false positives
        private const int FreezeBreachConfirmations = 2; // Require two consecutive breaches before failsafe
        private const int FailsafeFanPercent = 90;
        private const int ResumeGraceSeconds = 120; // Ignore freeze detection briefly after wake while sensors reattach
        private const int FailsafeFanReapplyIntervalSeconds = 15;
        private const double FailsafeSafeReleaseTempC = 65.0;
        private const int FailsafeSafeReleaseSeconds = 15;

        public HardwareWatchdogService(LoggingService logging, FanService fanService, ResumeRecoveryDiagnosticsService resumeDiagnostics)
        {
            _logging = logging;
            _fanService = fanService;
            _resumeDiagnostics = resumeDiagnostics;
        }

        /// <summary>
        /// Start watchdog monitoring
        /// </summary>
        public void Start()
        {
            if (_watchdogTimer != null) return;

            _logging.Info("🐕 Hardware watchdog started");
            _watchdogTimer = new Timer(CheckWatchdog, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(WatchdogIntervalMs));
            BackgroundTimerRegistry.Register(
                "HardwareWatchdog",
                "HardwareWatchdogService",
                "Monitors for frozen temperature sensors; triggers failsafe fan speeds",
                WatchdogIntervalMs,
                BackgroundTimerTier.Critical);
        }

        /// <summary>
        /// Stop watchdog monitoring
        /// </summary>
        public void Stop()
        {
            BackgroundTimerRegistry.Unregister("HardwareWatchdog");
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
            _logging.Info("🐕 Hardware watchdog stopped");
        }

        /// <summary>
        /// Update temperature reading (called by hardware monitoring)
        /// </summary>
        public void UpdateTemperature(double cpuTemp, double gpuTemp)
        {
            bool shouldRestoreAuto = false;

            lock (_stateLock)
            {
                // Receiving ANY call means the monitoring pipeline is alive — update the heartbeat
                // unconditionally. Stable idle temps are normal and must not trigger a false alarm.
                _lastTempUpdate = DateTime.Now;
                _lastCpuTemp = cpuTemp;
                _lastGpuTemp = gpuTemp;
                _consecutiveFreezeBreaches = 0;

                if (_failsafeActive)
                {
                    if (TryGetHottestValidTemperature(cpuTemp, gpuTemp, out var hottestTemp) &&
                        hottestTemp <= FailsafeSafeReleaseTempC)
                    {
                        if (_failsafeSafeSinceUtc == DateTime.MinValue)
                        {
                            _failsafeSafeSinceUtc = DateTime.UtcNow;
                        }
                        else if ((DateTime.UtcNow - _failsafeSafeSinceUtc).TotalSeconds >= FailsafeSafeReleaseSeconds)
                        {
                            _failsafeActive = false;
                            _isWatchdogArmed = true;
                            _failsafeLastFanApplyUtc = DateTime.MinValue;
                            _failsafeSafeSinceUtc = DateTime.MinValue;
                            shouldRestoreAuto = true;
                        }
                    }
                    else
                    {
                        // Not safe yet (or an unreadable sample) - reset the hold so release needs
                        // a fresh unbroken run of safe readings, not an intermittent one.
                        _failsafeSafeSinceUtc = DateTime.MinValue;
                    }
                }
            }

            if (shouldRestoreAuto)
            {
                _logging.Warn($"WATCHDOG: Monitoring recovered and temperatures are safe (<= {FailsafeSafeReleaseTempC:F0}°C for {FailsafeSafeReleaseSeconds}s) — restoring BIOS auto fan control");
                try
                {
                    _fanService.RestoreAutoControl();
                }
                catch (Exception ex)
                {
                    _logging.Warn($"WATCHDOG: Recovery restore auto control failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// The hotter of the two readings, ignoring any that is missing/garbage (NaN, infinite, at
        /// or below zero, or above a sane physical ceiling for a laptop CPU/GPU die).
        /// </summary>
        private static bool TryGetHottestValidTemperature(double cpuTemp, double gpuTemp, out double hottestTemp)
        {
            bool IsSane(double t) => !double.IsNaN(t) && !double.IsInfinity(t) && t > 0 && t <= 150;

            var hasCpu = IsSane(cpuTemp);
            var hasGpu = IsSane(gpuTemp);

            if (!hasCpu && !hasGpu)
            {
                hottestTemp = double.NaN;
                return false;
            }

            hottestTemp = hasCpu && hasGpu ? Math.Max(cpuTemp, gpuTemp) : hasCpu ? cpuTemp : gpuTemp;
            return true;
        }

        /// <summary>
        /// Pause watchdog freeze detection while the system is suspended.
        /// Long sleep intervals must not be treated as frozen monitoring.
        /// </summary>
        public void HandleSystemSuspend()
        {
            lock (_stateLock)
            {
                _suspendActive = true;
                _isWatchdogArmed = false;
                _failsafeActive = false;
                _consecutiveFreezeBreaches = 0;
                _lastTempUpdate = DateTime.Now;
                _resumeGraceUntilUtc = DateTime.MinValue;
                _failsafeLastFanApplyUtc = DateTime.MinValue;
                _failsafeSafeSinceUtc = DateTime.MinValue;
            }

            _logging.Info("WATCHDOG: Suspended freeze detection for system sleep");
            _resumeDiagnostics.RecordStep("watchdog", "Freeze detection suspended for sleep");
        }

        /// <summary>
        /// Resume watchdog monitoring after wake with a short grace period for sensor stack recovery.
        /// </summary>
        public void HandleSystemResume()
        {
            var nowUtc = DateTime.UtcNow;

            lock (_stateLock)
            {
                _suspendActive = false;
                _failsafeActive = false;
                _isWatchdogArmed = true;
                _consecutiveFreezeBreaches = 0;
                _lastTempUpdate = DateTime.Now;
                _resumeGraceUntilUtc = nowUtc.AddSeconds(ResumeGraceSeconds);
                _failsafeLastFanApplyUtc = DateTime.MinValue;
                _failsafeSafeSinceUtc = DateTime.MinValue;
            }

            _logging.Info($"WATCHDOG: Resumed after sleep — freeze detection delayed for {ResumeGraceSeconds}s while monitoring recovers");
            _resumeDiagnostics.RecordStep("watchdog", $"Resume grace window started ({ResumeGraceSeconds}s)");

            var cycleId = _resumeDiagnostics.CurrentCycleId;
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(ResumeGraceSeconds));
                if (_resumeDiagnostics.CurrentCycleId == cycleId)
                {
                    _resumeDiagnostics.RecordStep("watchdog", "Resume grace window ended");
                }
            });
        }

        /// <summary>
        /// Disarm watchdog temporarily (e.g., during diagnostic mode)
        /// </summary>
        public void Disarm()
        {
            _isWatchdogArmed = false;
            _logging.Debug("Watchdog disarmed");
        }

        /// <summary>
        /// Re-arm watchdog after disarm
        /// </summary>
        public void Arm()
        {
            _isWatchdogArmed = true;
            _lastTempUpdate = DateTime.Now; // Reset timer to avoid false trigger
            _logging.Debug("Watchdog armed");
        }

        private void CheckWatchdog(object? state)
        {
            bool shouldApplyFailsafe = false;
            bool shouldReapplyFailsafe = false;
            TimeSpan timeSinceLastUpdate = TimeSpan.Zero;
            int currentBreaches = 0;

            lock (_stateLock)
            {
                if (_disposed || _suspendActive)
                {
                    return;
                }

                // The failsafe keeps re-asserting itself on its own cadence even while normal
                // freeze detection is disarmed (which it is, for as long as _failsafeActive is
                // true) - see this file's own remarks on why the original one-shot apply wasn't
                // enough.
                if (_failsafeActive)
                {
                    var nowUtc = DateTime.UtcNow;
                    if ((nowUtc - _failsafeLastFanApplyUtc).TotalSeconds >= FailsafeFanReapplyIntervalSeconds)
                    {
                        _failsafeLastFanApplyUtc = nowUtc;
                        shouldReapplyFailsafe = true;
                    }
                }
                else
                {
                    if (!_isWatchdogArmed)
                    {
                        return;
                    }

                    if (DateTime.UtcNow < _resumeGraceUntilUtc)
                    {
                        return;
                    }

                    timeSinceLastUpdate = DateTime.Now - _lastTempUpdate;

                    if (timeSinceLastUpdate.TotalSeconds > FreezeThresholdSeconds)
                    {
                        _consecutiveFreezeBreaches++;
                        currentBreaches = _consecutiveFreezeBreaches;

                        if (_consecutiveFreezeBreaches >= FreezeBreachConfirmations)
                        {
                            _failsafeActive = true;
                            _isWatchdogArmed = false;
                            _failsafeLastFanApplyUtc = DateTime.UtcNow;
                            _failsafeSafeSinceUtc = DateTime.MinValue;
                            shouldApplyFailsafe = true;
                        }
                    }
                }
            }

            try
            {
                if (shouldApplyFailsafe)
                {
                    _logging.Error($"🚨 WATCHDOG: Temperature monitoring frozen for {timeSinceLastUpdate.TotalSeconds:F0}s - applying failsafe fan speed");

                    // Emergency: set a high but non-max speed to avoid sticky max countdown mode.
                    Task.Run(() =>
                    {
                        try
                        {
                            _fanService.ForceSetFanSpeed(FailsafeFanPercent);
                            _logging.Warn($"Fans set to {FailsafeFanPercent}% due to frozen temperature monitoring");
                            _logging.Warn($"🚨 WATCHDOG: Hardware monitoring frozen — fans set to {FailsafeFanPercent}%. Waiting for monitoring recovery.");
                            _logging.Warn("If this issue persists, check: WMI BIOS availability, system stability, or Windows updates.");
                            _logging.Warn($"WATCHDOG: Failsafe fan control remains active until temperatures are <= {FailsafeSafeReleaseTempC:F0}°C for {FailsafeSafeReleaseSeconds}s.");
                        }
                        catch (Exception ex)
                        {
                            _logging.Error($"Watchdog emergency fan set failed: {ex.Message}", ex);
                        }
                    });
                }
                else if (shouldReapplyFailsafe)
                {
                    Task.Run(() =>
                    {
                        try
                        {
                            _fanService.ForceSetFanSpeed(FailsafeFanPercent);
                            _logging.Warn($"WATCHDOG: Failsafe still active — reapplied fan speed {FailsafeFanPercent}%.");
                        }
                        catch (Exception ex)
                        {
                            _logging.Error($"Watchdog failsafe reassert failed: {ex.Message}", ex);
                        }
                    });
                }
                else if (currentBreaches > 0)
                {
                    _logging.Warn($"WATCHDOG: Potential monitoring stall ({timeSinceLastUpdate.TotalSeconds:F0}s, confirmation {currentBreaches}/{FreezeBreachConfirmations})");
                }
            }
            catch (Exception ex)
            {
                _logging.Error($"Watchdog check error: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
            }
        }
    }
}
