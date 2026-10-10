using System;
using System.Runtime.InteropServices;

namespace OmenCore.Utils
{
    /// <summary>Monitor and work-area lookup for the maximise hook.</summary>
    internal static class NativeMonitor
    {
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MinMaxInfo
        {
            public Point ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public Rect rcMonitor, rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        /// <summary>(monitor rectangle, work-area rectangle) of the monitor the window is on, or null.</summary>
        public static (Rect Monitor, Rect Work)? FromWindow(IntPtr hwnd)
        {
            const uint MonitorDefaultToNearest = 2;
            var handle = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (handle == IntPtr.Zero) return null;
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            return GetMonitorInfo(handle, ref info) ? (info.rcMonitor, info.rcWork) : null;
        }
    }
}
