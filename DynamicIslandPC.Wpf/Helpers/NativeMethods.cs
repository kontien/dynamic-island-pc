using System;
using System.Runtime.InteropServices;

namespace DynamicIslandPC.Wpf.Helpers
{
    internal static class NativeMethods
    {
        // ---------- user32: курсор и окна ----------

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        // ---------- user32: клавиатура и раскладка ----------
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : new IntPtr(GetWindowLong32(hWnd, nIndex));

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        // ---------- user32: буфер обмена ----------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, ChangeFilterAction action, IntPtr changeInfo);

        // ---------- dwmapi ----------
        [DllImport("dwmapi.dll", SetLastError = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // ---------- структуры ----------
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

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public uint length;
            public uint flags;
            public uint showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        public static WINDOWPLACEMENT CreateWindowPlacement() => new()
        {
            length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>()
        };

        // ---------- сообщения ----------
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int WM_NCHITTEST = 0x0084;
        public const int HTTRANSPARENT = -1;
        public const int HTCLIENT = 1;

        public const uint WM_COPYGLOBALDATA = 0x0049;
        public const uint WM_COPYDATA = 0x004A;
        public const uint WM_DROPFILES = 0x0233;

        // ---------- клавиши ----------
        public const int VK_CAPITAL = 0x14;

        // ---------- окна / fullscreen ----------
        public const int SW_SHOWMAXIMIZED = 3;
        public const int GWL_STYLE = -16;
        public const int WS_CAPTION = 0x00C00000;

        // ---------- DWM ----------
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