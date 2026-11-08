using System;
using System.Runtime.InteropServices;

namespace UsageOrbit.Platform
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct Rect
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
        }
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor, Work;
            public uint Flags;
        }
        public static Rect WorkArea(Rect bounds)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (!GetMonitorInfo(MonitorFromRect(ref bounds, 2), ref info)) throw new System.ComponentModel.Win32Exception();
            return info.Work;
        }
        public static Rect PrimaryWorkArea() { return WorkArea(new Rect { Left = 0, Top = 0, Right = 1, Bottom = 1 }); }
        public static Rect Bounds(IntPtr handle)
        {
            Rect rect;
            if (!GetWindowRect(handle, out rect)) throw new System.ComponentModel.Win32Exception();
            return rect;
        }
        public static double Scale(IntPtr handle)
        {
            try { uint dpi = GetDpiForWindow(handle); return dpi == 0 ? 1 : dpi / 96.0; }
            catch (EntryPointNotFoundException) { return 1; }
        }
        public static void Place(IntPtr handle, int x, int y, int width, int height)
        {
            SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x0010 | 0x0004);
        }
        public static void ToolWindow(IntPtr handle)
        {
            long style = IntPtr.Size == 8 ? GetWindowLongPtr(handle, -20).ToInt64() : GetWindowLong(handle, -20);
            if (IntPtr.Size == 8) SetWindowLongPtr(handle, -20, new IntPtr(style | 0x80));
            else SetWindowLong(handle, -20, (int)style | 0x80);
        }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int MessageBox(IntPtr owner, string text, string caption, uint flags);
    }
}
