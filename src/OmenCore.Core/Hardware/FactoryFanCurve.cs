using System;
using System.Collections.Generic;
using System.Linq;

namespace OmenCore.Hardware
{
    /// <summary>
    /// HP Gaming Hub's "Auto" fan policy on boards where the firmware's own Auto under-cools (GitHub #189, board 8D87).
    /// Pure maths: temperature in, fan level out, no hardware access. The tables below are the Performance-mode tables
    /// the reporter extracted from Gaming Hub's resource <c>Vibrance_STX_N22X9</c> (version 20250930) and validated with a
    /// standalone controller on a real 8D87.
    ///
    /// How it behaves, as reported: each sensor (CPU, GPU, IR/chassis) maps a temperature to a fan level through a table
    /// with separate rising and falling thresholds; the highest of the three requests wins; temperatures are smoothed
    /// with an EWMA (rise 0.1, fall 0.05) but an increase reacts to the raw reading so a spike is not delayed.
    /// </summary>
    public sealed class FactoryFanCurve
    {
        /// <summary>One sensor's thresholds. <see cref="Falling"/> is null when the table has none (IR).</summary>
        public sealed record SensorTable(double[] Rising, double[]? Falling, int[] Levels);

        public const double EmergencyCpuC = 92.0;

        private readonly SensorTable _cpu, _gpu, _ir;
        private readonly double _lambdaIncrease, _lambdaDecrease;
        private readonly SensorState _cpuState = new(), _gpuState = new(), _irState = new();

        public FactoryFanCurve(SensorTable cpu, SensorTable gpu, SensorTable ir, double lambdaIncrease, double lambdaDecrease)
        {
            _cpu = cpu; _gpu = gpu; _ir = ir;
            _lambdaIncrease = lambdaIncrease;
            _lambdaDecrease = lambdaDecrease;
        }

        private static readonly int[] PerformanceLevels = { 19, 20, 21, 22, 23, 25, 28, 31, 34, 37, 43, 47 };

        /// <summary>Gaming Hub's Performance-mode curve for board 8D87.</summary>
        public static FactoryFanCurve Performance8D87() => new(
            new SensorTable(new double[] { 60, 64, 68, 71, 74, 76, 78, 80, 82, 83, 84, 85 },
                            new double[] { 56, 60, 64, 67, 70, 72, 74, 76, 78, 79, 80, 81 }, PerformanceLevels),
            new SensorTable(new double[] { 57, 60, 63, 66, 69, 71, 73, 75, 77, 78, 79, 80 },
                            new double[] { 53, 55, 58, 61, 64, 67, 69, 71, 73, 75, 76, 77 }, PerformanceLevels),
            new SensorTable(new double[] { 42, 44, 46, 48, 50, 52, 54, 56, 58, 60, 62, 64 }, null, PerformanceLevels),
            lambdaIncrease: 0.1, lambdaDecrease: 0.05);

        public sealed record Result(int Level, bool Emergency, string Driver);

        /// <summary>
        /// Feed one sample (about once a second); returns the requested CPU-fan level, or 0 when no sensor has crossed its
        /// first threshold (hand the fans back to firmware Auto). A null temperature means that sensor is unavailable
        /// and is ignored for this sample.
        /// </summary>
        public Result Update(double? cpuC, double? gpuC, double? irC)
        {
            var cpu = Level(_cpu, _cpuState, cpuC);
            var gpu = Level(_gpu, _gpuState, gpuC);
            var ir = Level(_ir, _irState, irC);

            var level = Math.Max(cpu, Math.Max(gpu, ir));
            var driver = level == 0 ? "none" : level == cpu ? "CPU" : level == gpu ? "GPU" : "IR";
            return new Result(level, cpuC is >= EmergencyCpuC, driver);
        }

        public void Reset()
        {
            _cpuState.Reset(); _gpuState.Reset(); _irState.Reset();
        }

        private int Level(SensorTable table, SensorState s, double? raw)
        {
            if (raw is not double temp || double.IsNaN(temp)) return s.Index < 0 ? 0 : table.Levels[s.Index];

            s.Smoothed = s.Smoothed is double prev
                ? prev + (temp > prev ? _lambdaIncrease : _lambdaDecrease) * (temp - prev)
                : temp;

            // Rising reacts to the raw reading (a smoothed one lags a spike by seconds); falling uses the smoothed one.
            var rising = Math.Max(temp, s.Smoothed.Value);
            var target = -1;
            for (var i = 0; i < table.Rising.Length; i++)
                if (rising >= table.Rising[i]) target = i;
            if (target > s.Index) s.Index = target;

            var falling = table.Falling ?? Midpoints(table.Rising);
            while (s.Index >= 0 && s.Smoothed.Value < falling[s.Index] && temp < falling[s.Index])
                s.Index--;

            return s.Index < 0 ? 0 : table.Levels[s.Index];
        }

        // ponytail: a table with no falling thresholds (IR) steps down halfway between its rising thresholds, a guess:
        // the decompile only says "midpoints". Confirm against a real IR capture before trusting the IR drop-off.
        private static double[] Midpoints(double[] rising) =>
            rising.Select((t, i) => i == 0 ? t - 2 : (rising[i - 1] + t) / 2).ToArray();

        private sealed class SensorState
        {
            public int Index = -1;
            public double? Smoothed;
            public void Reset() { Index = -1; Smoothed = null; }
        }
    }

    /// <summary>
    /// HP WMI command 0x2F: which GPU-fan level pairs with each CPU-fan level (not 1:1 - CPU 47 pairs with GPU 49 and
    /// CPU 60 with GPU 58 on 8D87). Layout as documented in GitHub #189 and used by Gaming Hub and Linux hp-wmi:
    /// byte 0 = fan count, byte 1 unspecified, then records of one level per fan plus a noise index, until an all-zero record.
    /// </summary>
    public sealed class FanMappingTable
    {
        public sealed record Record(int CpuLevel, int GpuLevel, int NoiseIndex);

        public IReadOnlyList<Record> Records { get; }

        private FanMappingTable(IReadOnlyList<Record> records) => Records = records;

        /// <summary>
        /// The 0x2F response captured from an 8D87 on BIOS F.07 (GitHub #189, SHA-256 c9770ef1...a0e2f). It is a firmware
        /// constant, so the 8D87 curve uses it directly instead of querying the board.
        /// </summary>
        public static FanMappingTable Captured8D87()
        {
            const string hex = "02 3c 13 15 17 14 16 19 16 17 1c 18 1a 1f 1c 1e 23 1e 20 25 22 24 28 24 26 2a 25 27 2b 2b " +
                               "2d 2e 2f 31 30 32 34 32 35 37 33 38 3a 34 3c 3a 35 00";
            var bytes = new byte[128];
            var parts = hex.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length; i++) bytes[i] = Convert.ToByte(parts[i], 16);
            return Parse(bytes)!;
        }

        /// <summary>Parses the 128-byte payload of a two-fan response. Returns null if it is not that shape.</summary>
        public static FanMappingTable? Parse(byte[] payload)
        {
            if (payload.Length < 5 || payload[0] != 2) return null;
            var records = new List<Record>();
            for (var at = 2; at + 2 < payload.Length; at += 3)
            {
                if (payload[at] == 0 && payload[at + 1] == 0 && payload[at + 2] == 0) break;
                records.Add(new Record(payload[at], payload[at + 1], payload[at + 2]));
            }
            return records.Count == 0 ? null : new FanMappingTable(records);
        }

        /// <summary>
        /// GPU level for a CPU level: the exact record if there is one, otherwise the next record up (never cools less
        /// than asked), otherwise the last record.
        /// </summary>
        public int GpuLevelFor(int cpuLevel)
        {
            var match = Records.FirstOrDefault(r => r.CpuLevel >= cpuLevel);
            return (match ?? Records[^1]).GpuLevel;
        }
    }
}
