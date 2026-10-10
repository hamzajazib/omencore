using System;
using System.Management;
using System.Threading;
using NvpwrControlBlackwell;
using OmenCore.Hardware;
using OmenCore.Models;

namespace OmenCore.Services
{
    /// <summary>
    /// Opt-in NVIDIA laptop GPU power unlock (MAX via the driver's romOverride, CURRENT via the driver's internal
    /// RmControl call). The backend (OmenCore.NvidiaPower) comes from timmyy123's nvidia-power-control and
    /// bobshmo's OmenCore-Prophecy fork, used with their permission. This class only adds OmenCore's safety
    /// wrapper: on a Victus laptop the HP performance mode is set and the fan engine paused around a CURRENT
    /// write, and the write never runs if that preparation fails.
    /// </summary>
    public sealed class NvidiaPowerService
    {
        private static readonly object Sync = new();
        private readonly PowerBackend _backend = new();
        private readonly FanService? _fans;
        private readonly LoggingService _logging;
        private readonly Lazy<bool> _isVictus = new(DetectVictus);

        public NvidiaPowerService(FanService? fans, LoggingService logging)
        {
            _fans = fans;
            _logging = logging;
            PowerBackend.CurrentWriteGuard = write => GuardCurrent(BeforeCurrentPower, write);
        }

        public sealed record Status(
            bool Supported, bool MaxReady, bool CurrentReady, string Gpu, string Reason,
            double? LiveCurrentW, double? LiveMaxW, int[] Targets);

        public sealed record Outcome(bool Success, string Message, bool RebootRequired);

        public Status Assess()
        {
            var c = _backend.CheckCompatibility();
            var p = _backend.GetPowerState();
            return new Status(c.Supported, c.MaxWritesReady, c.CurrentWritesReady, c.GpuName, c.Reason,
                p.CurrentW, p.MaxW, c.Profile?.Targets() ?? Array.Empty<int>());
        }

        public Outcome ApplyMax(int watts) => Run("MAX " + watts + " W", () => _backend.SetMaxOverride(watts));
        /// <summary>Reads the VBIOS power table so MAX can be written. Uses nvflash64.exe if the user placed it next to OmenCore.</summary>
        public Outcome ResolveVbiosAuto() => Run("resolve VBIOS", () => _backend.TryAutoResolveVbios(out _));
        public Outcome ResolveVbiosFromRom(string romPath) => Run("resolve VBIOS from file", () => _backend.ResolveVbiosFromRom(romPath));
        public Outcome RemoveMax() => Run("remove MAX override", () => _backend.RemoveMaxOverride());
        public Outcome ApplyCurrent(int watts) => Run("CURRENT " + watts + " W", () => _backend.SetCurrent(watts));

        private Outcome Run(string what, Func<OperationResult> op)
        {
            try
            {
                var r = op();
                _logging.Info($"NVIDIA power: {what} -> {(r.Success ? "ok" : "refused")}: {r.Message}");
                return new Outcome(r.Success, r.Message, r.RebootRequired);
            }
            catch (InvalidOperationException ex)
            {
                // Raised by the Victus guard: the write was never sent.
                _logging.Warn($"NVIDIA power: {what} not sent: {ex.Message}");
                return new Outcome(false, ex.Message, false);
            }
        }

        /// <summary>Runs <paramref name="write"/> only after <paramref name="prepare"/> succeeded; releases afterwards.</summary>
        internal static int GuardCurrent(Func<IDisposable> prepare, Func<int> write)
        {
            using var lease = prepare();
            return write();
        }

        private IDisposable BeforeCurrentPower()
        {
            if (!_isVictus.Value) return NoLease.Instance;

            Monitor.Enter(Sync);
            var diagnosticEntered = false;
            try
            {
                if (FanService.IsAnyDiagnosticModeActive)
                    throw new InvalidOperationException("Finish OmenCore fan diagnostics before changing GPU CURRENT.");

                var maxPreset = _fans?.ActivePreset?.Mode == FanMode.Max ? _fans.ActivePreset : null;
                if (_fans != null && maxPreset == null &&
                    !_fans.ApplyPreset(new FanPreset { Name = "Performance", Mode = FanMode.Performance, IsBuiltIn = true }, immediate: true))
                    throw new InvalidOperationException("OmenCore could not prepare performance cooling. CURRENT was not sent.");

                using var hp = new HpWmiBios(_logging);
                if (!hp.IsAvailable)
                    throw new InvalidOperationException("HP WMI is unavailable (" + hp.Status + "). CURRENT was not sent.");
                if (!hp.SetFanMode(HpWmiBios.FanMode.LegacyPerformance))
                    throw new InvalidOperationException("HP performance mode was refused. CURRENT was not sent.");

                // Changing the HP mode can drop Max; give it back through the fan service (which owns its release).
                if (maxPreset != null && (!_fans!.ApplyPreset(maxPreset, immediate: true) || hp.GetFanMax() != true))
                    throw new InvalidOperationException("Fan Max could not be preserved. CURRENT was not sent.");

                if (_fans != null)
                {
                    _fans.EnterDiagnosticMode();
                    diagnosticEntered = true;
                }
                return new HeldLease(_fans, diagnosticEntered);
            }
            catch
            {
                if (diagnosticEntered) _fans?.ExitDiagnosticMode();
                Monitor.Exit(Sync);
                throw;
            }
        }

        private static bool DetectVictus()
        {
            try
            {
                using var q = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem");
                foreach (ManagementObject o in q.Get())
                    if ((Convert.ToString(o["Manufacturer"]) ?? "").Contains("HP", StringComparison.OrdinalIgnoreCase) &&
                        (Convert.ToString(o["Model"]) ?? "").Contains("Victus", StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch (ManagementException)
            {
                // Unknown host: treated as not Victus, so the plain (no HP preparation) path is used.
            }
            return false;
        }

        private sealed class NoLease : IDisposable
        {
            public static readonly NoLease Instance = new();
            public void Dispose() { }
        }

        private sealed class HeldLease : IDisposable
        {
            private readonly FanService? _fans;
            private readonly bool _diagnostic;
            public HeldLease(FanService? fans, bool diagnostic) { _fans = fans; _diagnostic = diagnostic; }

            public void Dispose()
            {
                try { if (_diagnostic) _fans?.ExitDiagnosticMode(); }
                finally { Monitor.Exit(Sync); }
            }
        }
    }
}
