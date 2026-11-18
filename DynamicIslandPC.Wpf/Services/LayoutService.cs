using System;
using System.Globalization;
using DynamicIslandPC.Wpf.Helpers;

namespace DynamicIslandPC.Wpf.Services
{
    public class LayoutService
    {
        public string CurrentLayout { get; private set; } = "EN";
        public bool IsCapsLock { get; private set; }

        public event EventHandler<(string layout, bool capsLock)>? Changed;

        private bool _errorLogged;

        public void Poll()
        {
            try
            {
                IntPtr hwnd = NativeMethods.GetForegroundWindow();
                uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, IntPtr.Zero);
                IntPtr layout = NativeMethods.GetKeyboardLayout(threadId);
                int layoutId = (int)(layout.ToInt64() & 0xFFFF);

                // Любой язык через LCID → culture, а не только EN/RU
                string newLayout;
                try
                {
                    newLayout = new CultureInfo(layoutId).TwoLetterISOLanguageName.ToUpperInvariant();
                    if (string.IsNullOrEmpty(newLayout)) newLayout = "?";
                }
                catch { newLayout = "?"; }

                bool newCaps = (NativeMethods.GetKeyState(NativeMethods.VK_CAPITAL) & 0x01) != 0;

                if (newLayout != CurrentLayout || newCaps != IsCapsLock)
                {
                    CurrentLayout = newLayout;
                    IsCapsLock = newCaps;
                    Changed?.Invoke(this, (CurrentLayout, IsCapsLock));
                }

                _errorLogged = false;
            }
            catch (Exception ex)
            {
                // Poll вызывается 5 раз в секунду — не спамим вывод
                if (_errorLogged) return;
                _errorLogged = true;
                System.Diagnostics.Debug.WriteLine($"[Layout] Poll failed: {ex.Message}");
            }
        }
    }
}