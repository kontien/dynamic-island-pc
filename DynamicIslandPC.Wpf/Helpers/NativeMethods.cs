using System;
using System.Runtime.InteropServices;

namespace DynamicIslandPC.Wpf.Helpers
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, ChangeFilterAction action, IntPtr changeInfo);

        [DllImport("user32.dll")]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public const uint WM_COPYGLOBALDATA = 0x0049;
        public const uint WM_COPYDATA = 0x004A;
        public const uint WM_DROPFILES = 0x0233;
        public const int WM_CLIPBOARDUPDATE = 0x031D;

        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

        public enum ChangeFilterAction : uint
        {
            MSGFLT_RESET = 0,
            MSGFLT_ALLOW = 1,
            MSGFLT_DISALLOW = 2
        }
    }
}