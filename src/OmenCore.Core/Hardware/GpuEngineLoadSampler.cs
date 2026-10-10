using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Windows "GPU Engine" utilisation, read with one <see cref="PerformanceCounterCategory.ReadCategory"/> call per poll.
    /// This replaces one PerformanceCounter object per engine instance (about 290 on an idle desktop, one per process per
    /// engine): reading those took ~144 ms per poll and held a counter object each, against ~2 ms for the single read.
    /// The numbers are identical: utilisation is the delta of the raw sample between two reads.
    /// </summary>
    public sealed class GpuEngineLoadSampler
    {
        private readonly object _sync = new();
        private Dictionary<string, CounterSample> _previous = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Current GPU load in percent. Returns 0 on the first call (it only records a baseline) and on any
        /// failure to read the counters, which callers already treat as "no reading".
        /// </summary>
        public double ReadLoadPercent()
        {
            try
            {
                var data = new PerformanceCounterCategory("GPU Engine").ReadCategory();
                if (!data.Contains("Utilization Percentage")) return 0;
                var instances = data["Utilization Percentage"];

                var current = new Dictionary<string, CounterSample>(StringComparer.OrdinalIgnoreCase);
                foreach (System.Collections.DictionaryEntry e in instances)
                {
                    var name = (string)e.Key;
                    if (IsTrackedEngine(name)) current[name] = ((InstanceData)e.Value!).Sample;
                }

                lock (_sync)
                {
                    var previous = _previous;
                    _previous = current;
                    if (previous.Count == 0) return 0;

                    var values = new List<KeyValuePair<string, double>>(current.Count);
                    foreach (var kv in current)
                    {
                        if (!previous.TryGetValue(kv.Key, out var before)) continue;
                        values.Add(new KeyValuePair<string, double>(kv.Key, Calculate(before, kv.Value)));
                    }
                    return Aggregate(values);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException or ArgumentException)
            {
                return 0;
            }
        }

        private static double Calculate(CounterSample before, CounterSample after)
        {
            try { return CounterSample.Calculate(before, after); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                // An engine instance that was recreated between the two reads: no usable delta.
                return 0;
            }
        }

        internal static bool IsTrackedEngine(string instanceName) =>
            instanceName.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) ||
            instanceName.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase) ||
            instanceName.Contains("engtype_Cuda", StringComparison.OrdinalIgnoreCase) ||
            instanceName.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase) ||
            instanceName.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) ||
            instanceName.Contains("engtype_VideoProcessing", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The busiest 3D/compute engine if any is active, otherwise the sum of the others (copy and video engines),
        /// clamped to 0-100 and rounded to one decimal. Same rule both monitors used with the per-instance counters.
        /// </summary>
        internal static double Aggregate(IEnumerable<KeyValuePair<string, double>> perInstance)
        {
            double total = 0, preferredPeak = 0;
            foreach (var (name, value) in perInstance)
            {
                if (!double.IsFinite(value) || value <= 0) continue;
                total += value;
                if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("engtype_Cuda", StringComparison.OrdinalIgnoreCase))
                    preferredPeak = Math.Max(preferredPeak, value);
            }
            return Math.Round(Math.Clamp(preferredPeak > 0 ? preferredPeak : total, 0, 100), 1);
        }
    }
}
