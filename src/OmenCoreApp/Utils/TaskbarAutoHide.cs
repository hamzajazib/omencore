using System;
using System.Runtime.InteropServices;

namespace OmenCore.Utils
{
    /// <summary>
    /// A borderless window maximised on a monitor whose taskbar is set to auto-hide covers the taskbar, because
    /// Windows reports the whole screen as the work area. Leaving a thin strip free on the taskbar's edge lets the
    /// mouse reach it and the taskbar pop up, which is what every other maximised app does.
    /// </summary>
    public static class TaskbarAutoHide
    {
        public const int StripPx = 2;

        public enum Edge { Left = 0, Top = 1, Right = 2, Bottom = 3 }

        /// <summary>The rectangle (left, top, width, height) a maximised window should take.</summary>
        public static (int Left, int Top, int Width, int Height) Fit(
            int left, int top, int width, int height, bool autoHide, Edge edge)
        {
            if (!autoHide) return (left, top, width, height);
            return edge switch
            {
                Edge.Left => (left + StripPx, top, width - StripPx, height),
                Edge.Top => (left, top + StripPx, width, height - StripPx),
                Edge.Right => (left, top, width - StripPx, height),
                _ => (left, top, width, height - StripPx),
            };
        }

        public static bool TryGetState(out bool autoHide, out Edge edge)
        {
            autoHide = false;
            edge = Edge.Bottom;
            try
            {
                var data = new AppBarData { cbSize = Marshal.SizeOf<AppBarData>() };
                autoHide = ((uint)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) != 0;
                if (autoHide && SHAppBarMessage(AbmGetTaskbarPos, ref data) != IntPtr.Zero)
                    edge = (Edge)Math.Clamp(data.uEdge, 0, 3);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
            {
                return false;
            }
        }

        private const uint AbmGetState = 0x4;
        private const uint AbmGetTaskbarPos = 0x5;
        private const uint AbsAutoHide = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uCallbackMessage;
            public int uEdge;
            public int left, top, right, bottom;
            public IntPtr lParam;
        }

        [DllImport("shell32.dll")]
        private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);
    }
}
