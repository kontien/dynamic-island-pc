using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf.Services
{
    public enum HotkeyAction { ToggleIsland = 1, PasteLast = 2, Screenshot = 3 }

    public class HotkeyService : IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;
        private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        private const uint VK_I = 0x49, VK_V = 0x56, VK_S = 0x53;

        private readonly IntPtr _hwnd;
        private readonly Dictionary<int, Action> _actions = new();
        private bool _disposed;

        public HotkeyService(IntPtr hwnd) => _hwnd = hwnd;

        public void RegisterDefaults(Action toggleIsland, Action pasteLast, Action screenshot)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));
            Register(HotkeyAction.ToggleIsland, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_I, toggleIsland);
            Register(HotkeyAction.PasteLast, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_V, pasteLast);
            RegisterScreenshot(screenshot);
        }

        public void RegisterScreenshot(Action screenshot)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HotkeyService));
            Register(HotkeyAction.Screenshot, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_S, screenshot);
        }

        private void Register(HotkeyAction action, uint mods, uint vk, Action callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            int id = (int)action;
            if (RegisterHotKey(_hwnd, id, mods, vk))
                _actions[id] = callback;
            else
                Log.Warn($"[Hotkey] не удалось зарегистрировать {action}; Win32 error={Marshal.GetLastWin32Error()}");
        }

        public bool HandleMessage(int msg, IntPtr wParam)
        {
            if (msg != WM_HOTKEY) return false;
            if (_actions.TryGetValue(wParam.ToInt32(), out var cb))
            {
                try { cb(); }
                catch (Exception ex) { Log.Error("[Hotkey] callback", ex); }
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var id in _actions.Keys)
            {
                if (!UnregisterHotKey(_hwnd, id))
                    Log.Warn($"[Hotkey] failed to unregister id={id}; Win32 error={Marshal.GetLastWin32Error()}");
            }
            _actions.Clear();
        }
    }
}