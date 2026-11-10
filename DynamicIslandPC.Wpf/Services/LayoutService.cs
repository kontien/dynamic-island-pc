using System;
using System.Runtime.InteropServices;

namespace DynamicIslandPC.Wpf.Services
{
    public class LayoutService
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private const int VK_CAPITAL = 0x14;

        public string CurrentLayout { get; private set; } = "EN";
        public bool IsCapsLock { get; private set; }

        public event EventHandler<(string layout, bool capsLock)>? Changed;

        public void Poll()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                uint threadId = GetWindowThreadProcessId(hwnd, IntPtr.Zero);
                IntPtr layout = GetKeyboardLayout(threadId);

                // Исправлено: ToInt64 вместо ToInt32 (ToInt32 падает на x64)
                int layoutId = (int)(layout.ToInt64() & 0xFFFF);

                string newLayout = layoutId switch
                {
                    0x0409 => "EN",
                    0x0419 => "RU",
                    _ => "??"
                };

                bool newCaps = (GetKeyState(VK_CAPITAL) & 0x01) != 0;

                if (newLayout != CurrentLayout || newCaps != IsCapsLock)
                {
                    CurrentLayout = newLayout;
                    IsCapsLock = newCaps;
                    Changed?.Invoke(this, (CurrentLayout, IsCapsLock));
                }
            }
            catch { }
        }
    }
}