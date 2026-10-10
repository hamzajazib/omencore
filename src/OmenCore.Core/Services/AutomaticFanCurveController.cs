using System;
using OmenCore.Hardware;

namespace OmenCore.Services
{
    /// <summary>
    /// Runs HP's factory Performance-mode fan curve (<see cref="FactoryFanCurve"/>) on a board whose firmware Auto
    /// under-cools (GitHub #189). It owns no timer and no hardware access: <see cref="FanService"/> calls
    /// <see cref="Tick"/> from its monitor loop, only while its own safety gates allow it, and supplies the write and
    /// release delegates. A write failure three times in a row hands the fans back to firmware Auto and stops trying.
    /// </summary>
    public sealed class AutomaticFanCurveController
    {
        private const int MaxConsecutiveWriteFailures = 3;
        private static readonly TimeSpan KeepAlive = TimeSpan.FromSeconds(15);

        private readonly FactoryFanCurve _curve;
        private readonly FanMappingTable _mapping;
        private readonly int _maxFanLevel;
        private readonly Func<int, int, bool> _apply;
        private readonly Action _release;
        private readonly Func<double?> _readIr;
        private readonly Func<DateTime> _now;
        private readonly Action<string>? _log;

        private (int Cpu, int Gpu)? _lastWritten;
        private DateTime _lastWriteAt = DateTime.MinValue;
        private int _failures;

        public AutomaticFanCurveController(
            FactoryFanCurve curve, FanMappingTable mapping, int maxFanLevel,
            Func<int, int, bool> apply, Action release, Func<double?> readIr,
            Action<string>? log = null, Func<DateTime>? now = null)
        {
            _curve = curve;
            _mapping = mapping;
            _maxFanLevel = Math.Clamp(maxFanLevel, 1, 100);
            _apply = apply;
            _release = release;
            _readIr = readIr;
            _log = log;
            _now = now ?? (() => DateTime.UtcNow);
        }

        /// <summary>True while this controller is holding the fans at a level it chose.</summary>
        public bool Engaged { get; private set; }

        /// <summary>True after repeated write failures; stays off until <see cref="Reset"/>.</summary>
        public bool Faulted { get; private set; }

        /// <summary>The percent that makes WmiFanController write exactly <paramref name="level"/> (it truncates).</summary>
        public static int LevelToPercent(int level, int maxFanLevel) =>
            Math.Clamp((int)Math.Ceiling(level * 100.0 / Math.Max(1, maxFanLevel)), 0, 100);

        /// <summary>One sample. Temperatures of 0 or less mean "no reading".</summary>
        public void Tick(double cpuC, double gpuC)
        {
            if (Faulted) return;

            var result = _curve.Update(cpuC > 0 ? cpuC : null, gpuC > 0 ? gpuC : null, _readIr());

            if (result.Emergency)
            {
                Write(100, 100, force: true);
                return;
            }

            if (result.Level <= 0)
            {
                Disengage();
                return;
            }

            var gpuLevel = _mapping.GpuLevelFor(result.Level);
            Write(LevelToPercent(result.Level, _maxFanLevel), LevelToPercent(gpuLevel, _maxFanLevel), force: false);
        }

        /// <summary>Hand the fans back to firmware Auto. Safe to call when not engaged.</summary>
        public void Disengage()
        {
            if (!Engaged) return;
            Engaged = false;
            _lastWritten = null;
            _curve.Reset();
            try { _release(); }
            catch (InvalidOperationException ex) { _log?.Invoke($"Automatic fan curve: release failed: {ex.Message}"); }
        }

        /// <summary>
        /// Another mode (a user preset, Max, thermal protection) now controls the fans: forget our state WITHOUT
        /// releasing, since a release would undo whatever that mode just set.
        /// </summary>
        public void Abandon()
        {
            Engaged = false;
            _lastWritten = null;
            _curve.Reset();
        }

        public void Reset()
        {
            Faulted = false;
            _failures = 0;
        }

        private void Write(int cpuPercent, int gpuPercent, bool force)
        {
            var target = (cpuPercent, gpuPercent);
            var due = force || _lastWritten != target || _now() - _lastWriteAt >= KeepAlive;
            if (!due) return;

            bool ok;
            try { ok = _apply(cpuPercent, gpuPercent); }
            catch (InvalidOperationException ex)
            {
                _log?.Invoke($"Automatic fan curve: write threw: {ex.Message}");
                ok = false;
            }

            if (ok)
            {
                _failures = 0;
                _lastWritten = target;
                _lastWriteAt = _now();
                Engaged = true;
                return;
            }

            if (++_failures >= MaxConsecutiveWriteFailures)
            {
                Faulted = true;
                _log?.Invoke("Automatic fan curve: fan writes failed three times, returning to firmware Auto.");
                Disengage();
            }
        }
    }
}
