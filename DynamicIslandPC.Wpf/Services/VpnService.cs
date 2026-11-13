using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DynamicIslandPC.Wpf.Services
{
    public class VpnService : IDisposable
    {
        public event Action<bool>? StatusChanged;
        public bool IsConnected { get; private set; }

        private const string AppPath = @"C:\Program Files\Quattro VPN\quattro_vpn.exe";
        private const string ProcessName = "quattro_vpn";
        private const string AdapterName = "quattro-tun";

        private readonly DispatcherTimer _statusTimer;

        // Импорт WinAPI
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x02;
        private const uint MOUSEEVENTF_LEFTUP = 0x04;
        private const int SW_RESTORE = 9;
        private const uint WM_CLOSE = 0x0010;

        public VpnService()
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusTimer.Tick += (s, e) => CheckStatus();
            _statusTimer.Start();
            CheckStatus();
        }

        private void CheckStatus()
        {
            try
            {
                var adapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.Name.Contains(AdapterName, StringComparison.OrdinalIgnoreCase) ||
                                          ni.Description.Contains("Quattro", StringComparison.OrdinalIgnoreCase));

                bool newStatus = adapter != null && adapter.OperationalStatus == OperationalStatus.Up;

                if (newStatus != IsConnected)
                {
                    IsConnected = newStatus;
                    StatusChanged?.Invoke(IsConnected);
                    Debug.WriteLine($"[VPN] Status changed to: {(IsConnected ? "Connected" : "Disconnected")}");
                }
            }
            catch { }
        }

        public async Task ToggleAsync()
        {
            try
            {
                var process = Process.GetProcessesByName(ProcessName).FirstOrDefault();
                if (process == null)
                {
                    Debug.WriteLine("[VPN] App not running, starting...");
                    try
                    {
                        Process.Start(new ProcessStartInfo(AppPath) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VPN] ОШИБКА ЗАПУСКА: {ex.Message}");
                        return;
                    }

                    await Task.Delay(4000);
                    process = Process.GetProcessesByName(ProcessName).FirstOrDefault();
                    if (process == null) return;
                }

                IntPtr mainWindow = process.MainWindowHandle;
                if (mainWindow == IntPtr.Zero)
                {
                    mainWindow = FindWindowByProcessId(process.Id);
                }

                if (mainWindow != IntPtr.Zero)
                {
                    // Показываем окно
                    ShowWindow(mainWindow, SW_RESTORE);
                    SetForegroundWindow(mainWindow);
                    await Task.Delay(300);

                    if (GetWindowRect(mainWindow, out RECT rect))
                    {
                        // Координаты кнопки
                        int clickX = rect.Left + (int)((rect.Right - rect.Left) * 0.75);
                        int clickY = rect.Top + (int)((rect.Bottom - rect.Top) * 0.42);

                        // Сохраняем старую позицию мыши
                        GetCursorPos(out POINT oldPos);

                        // Кликаем
                        SetCursorPos(clickX, clickY);
                        await Task.Delay(50);
                        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                        await Task.Delay(50);
                        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);

                        Debug.WriteLine($"[VPN] Clicked at {clickX}, {clickY}");

                        // Возвращаем мышку
                        await Task.Delay(100);
                        SetCursorPos(oldPos.X, oldPos.Y);

                        // Ждем полсекунды и закрываем (сворачиваем в трей)
                        await Task.Delay(500);
                        Debug.WriteLine("[VPN] Sending WM_CLOSE to hide window");
                        PostMessage(mainWindow, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                else
                {
                    Debug.WriteLine("[VPN] Could not find any window for Quattro VPN.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VPN] Error: {ex.Message}");
            }
        }

        private IntPtr FindWindowByProcessId(int processId)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == processId)
                {
                    result = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public void Dispose()
        {
            _statusTimer.Stop();
        }
    }
}