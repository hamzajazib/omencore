using System;
using System.Runtime.InteropServices;

namespace OmenCore.Hardware
{
    /// <summary>
    /// Read-only snapshot of the NVIDIA driver's power policy in watts: the limit it is enforcing right
    /// now, its default, the allowed range and the live draw. Lets a report show whether the firmware or
    /// driver ceiling is what is holding a laptop GPU down, before any write path is considered.
    /// </summary>
    public sealed record NvmlPowerPolicy(
        double? EnforcedLimitW, double? DefaultLimitW, double? MinLimitW, double? MaxLimitW, double? DrawW)
    {
        public bool HasData => EnforcedLimitW.HasValue || DrawW.HasValue;

        /// <summary>Human-readable single line for logs and diagnostics exports.</summary>
        public string Describe()
        {
            if (!HasData) return "unavailable";
            string W(double? v) => v.HasValue ? $"{v.Value:0.#} W" : "n/a";
            var text = $"enforced {W(EnforcedLimitW)}, default {W(DefaultLimitW)}, range {W(MinLimitW)}-{W(MaxLimitW)}, draw {W(DrawW)}";
            if (EnforcedLimitW.HasValue && MaxLimitW.HasValue && EnforcedLimitW.Value + 0.5 < MaxLimitW.Value)
                text += " (enforced limit is below the driver maximum: an OEM/firmware policy is holding it down)";
            return text;
        }

        /// <summary>NVML reports milliwatts; zero or implausible values mean "not reported".</summary>
        internal static double? FromMilliwatts(uint mw) => mw is 0 or > 1_000_000 ? null : mw / 1000.0;

        /// <summary>
        /// Reads the policy of the first NVIDIA GPU. Never wakes a sleeping dGPU (see
        /// <see cref="GpuPowerStateProbe"/>) and never throws: any failure returns an empty snapshot.
        /// </summary>
        public static NvmlPowerPolicy Read(GpuPowerStateProbe? probe = null)
        {
            var empty = new NvmlPowerPolicy(null, null, null, null, null);
            try
            {
                if (probe?.IsAsleep() == true) return empty;
                if (Native.nvmlInit_v2() != 0) return empty;
                try
                {
                    if (Native.nvmlDeviceGetHandleByIndex_v2(0, out var dev) != 0) return empty;
                    uint enforced = 0, def = 0, min = 0, max = 0, draw = 0;
                    Native.nvmlDeviceGetEnforcedPowerLimit(dev, out enforced);
                    Native.nvmlDeviceGetPowerManagementDefaultLimit(dev, out def);
                    Native.nvmlDeviceGetPowerManagementLimitConstraints(dev, out min, out max);
                    Native.nvmlDeviceGetPowerUsage(dev, out draw);
                    return new NvmlPowerPolicy(FromMilliwatts(enforced), FromMilliwatts(def),
                        FromMilliwatts(min), FromMilliwatts(max), FromMilliwatts(draw));
                }
                finally { Native.nvmlShutdown(); }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                return empty;
            }
        }

        private static class Native
        {
            private const string Lib = "nvml.dll";
            [DllImport(Lib)] internal static extern int nvmlInit_v2();
            [DllImport(Lib)] internal static extern int nvmlShutdown();
            [DllImport(Lib)] internal static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
            [DllImport(Lib)] internal static extern int nvmlDeviceGetEnforcedPowerLimit(IntPtr device, out uint milliwatts);
            [DllImport(Lib)] internal static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint milliwatts);
            [DllImport(Lib)] internal static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr device, out uint min, out uint max);
            [DllImport(Lib)] internal static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
        }
    }
}
