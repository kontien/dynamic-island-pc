// Перехват уведомлений Windows для Dynamic Island PC.
// Режим 1 (System): UserNotificationListener — если Windows дала доступ.
// Режим 2 (Poll):   fallback без разрешений — таймер опрашивает оболочку
//                   (EnumWindows) на предмет всплывающих toast-окон
//                   ShellExperienceHost/ShellHost/explorer и читает их текст
//                   через UI Automation. Работает у unpackaged-приложений всегда.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf.Services
{
    public sealed class NotificationItem
    {
        public uint Id { get; set; }
        public string AppName { get; set; } = "";
        public string AppId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime Time { get; set; }
        public BitmapImage? Logo { get; set; }
        public bool IsRead { get; set; }
        public string Group { get; set; } = "Прочее";
        public bool FromHook { get; set; }
    }

    public sealed class NotificationService : IDisposable
    {
        // ==================== СОСТОЯНИЕ ====================
        private UserNotificationListener? _listener;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly Dictionary<uint, NotificationItem> _map = new();
        private readonly Dictionary<string, BitmapImage?> _logoCache = new();
        private readonly Dictionary<string, DateTime> _seenKeys = new();
        private readonly HashSet<IntPtr> _knownHwnds = new();
        private CancellationTokenSource? _debounce;
        private Timer? _poll;
        private Timer? _scan;
        private bool _disposed;
        private bool _started;
        private bool _listenerHooked;
        private uint _nextHookId = 10_000_000;

        public string AccessStatus { get; private set; } = "Проверка доступа…";
        public bool AccessGranted { get; private set; }
        public bool UsingPoll { get; private set; }

        public event Action<NotificationItem>? Added;
        public event Action? Changed;
        public event Action? AccessChanged;

        private const int MaxItems = 80;

        // ==================== WIN32 ====================
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        private static readonly string[] ShellProcesses =
        {
            "shellexperiencehost", "shellhost", "explorer", "notificationhost"
        };

        private static readonly string[] ToastClasses =
        {
            "windows.ui.core.corewindow", "shell_dialog", "chrome_widgetwin_1"
        };

        private EnumWindowsProc? _enumProc;   // держим делегат живым

        // ==================== СТАРТ ====================
        public async Task StartAsync(CancellationToken ct)
        {
            if (_started || _disposed) return;
            _started = true;

            bool listenerOk = await TryStartListenerAsync(ct);
            if (!listenerOk && !_disposed)
                StartPollFallback("Системный listener недоступен — включён перехват всплывающих окон");
        }

        public async Task<bool> RequestAccessAsync()
        {
            bool ok = await TryStartListenerAsync(CancellationToken.None);
            if (!ok && !UsingPoll && !_disposed)
                StartPollFallback("Системный listener недоступен — включён перехват всплывающих окон");
            return ok || UsingPoll;
        }

        private async Task<bool> TryStartListenerAsync(CancellationToken ct)
        {
            try
            {
                _listener = UserNotificationListener.Current;
                if (_listener == null)
                {
                    SetStatus("Служба уведомлений Windows не найдена", false);
                    return false;
                }

                var access = await _listener.RequestAccessAsync();
                if (_disposed || ct.IsCancellationRequested) return false;

                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    SetStatus($"Системный доступ запрещён ({access})", false);
                    return false;
                }

                if (!_listenerHooked)
                {
                    _listener.NotificationChanged += OnNotificationChanged;
                    _listenerHooked = true;
                }

                UsingPoll = false;
                StopScan();
                SetStatus("Системный перехват уведомлений активен", true);
                await RefreshAsync();
                StartPoll();
                Log.Info("[Notify] listener активен");
                return true;
            }
            catch (Exception ex)
            {
                string detail = $"{ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}".Trim();
                SetStatus($"Системный перехват недоступен: {detail}", false);
                Log.Error("[Notify] listener", ex);
                return false;
            }
        }

        private void SetStatus(string text, bool granted)
        {
            AccessStatus = text;
            AccessGranted = granted;
            AccessChanged?.Invoke();
        }

        // ==================== FALLBACK: ОПРОС ОБОЛОЧКИ ====================
        private void StartPollFallback(string reason)
        {
            if (UsingPoll || _disposed) return;
            UsingPoll = true;
            SetStatus(reason + " (опрос оболочки, без прав)", true);
            _scan ??= new Timer(_ => ScanToasts(), null, 500, 700);
            Log.Info("[Notify] poll-режим включён");
        }

        private void StopScan()
        {
            _scan?.Dispose();
            _scan = null;
        }

        private void ScanToasts()
        {
            if (_disposed || !UsingPoll) return;
            try
            {
                var found = new List<IntPtr>();
                _enumProc ??= (h, _) =>
                {
                    if (IsShellToastWindow(h)) found.Add(h);
                    return true;
                };
                found.Clear();
                EnumWindows(_enumProc, IntPtr.Zero);

                var fresh = new List<IntPtr>();
                lock (_knownHwnds)
                {
                    foreach (var h in found)
                    {
                        if (!_knownHwnds.Contains(h))
                        {
                            _knownHwnds.Add(h);
                            fresh.Add(h);
                        }
                    }
                    // окна, которые исчезли, забываем — повторный тост поймаем снова
                    foreach (var old in _knownHwnds.Where(x => !found.Contains(x) || !IsWindow(x)).ToList())
                        _knownHwnds.Remove(old);
                }

                foreach (var h in fresh)
                    _ = ReadToastAsync(h);
            }
            catch (Exception ex) { Log.Warn("[Notify] scan: " + ex.Message); }
        }

        private static bool IsShellToastWindow(IntPtr hwnd)
        {
            try
            {
                if (!IsWindowVisible(hwnd)) return false;

                var sb = new StringBuilder(256);
                if (GetClassName(hwnd, sb, sb.Capacity) == 0) return false;
                string cls = sb.ToString().ToLowerInvariant();

                bool classOk = false;
                foreach (var c in ToastClasses)
                {
                    if (cls == c) { classOk = true; break; }
                }
                if (!classOk) return false;

                GetWindowThreadProcessId(hwnd, out uint pid);
                using var proc = Process.GetProcessById((int)pid);
                string name = proc.ProcessName.ToLowerInvariant();
                foreach (var s in ShellProcesses)
                {
                    if (name == s) return true;
                }
                return false;
            }
            catch { return false; }
        }

        private async Task ReadToastAsync(IntPtr hwnd)
        {
            try
            {
                await Task.Delay(300);
                if (_disposed) return;

                var lines = await Task.Run(() => ReadToastTexts(hwnd));
                if (lines == null || lines.Count == 0) return;

                string app, title, body;
                if (lines.Count >= 3) { app = lines[0]; title = lines[1]; body = string.Join(" · ", lines.Skip(2).Take(2)); }
                else if (lines.Count == 2) { app = lines[0]; title = lines[1]; body = ""; }
                else { app = "Уведомление"; title = lines[0]; body = ""; }

                if (title.Length == 0 && body.Length == 0) return;
                if (app.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase)) return;

                Emit(new NotificationItem
                {
                    AppName = app,
                    AppId = app,
                    Title = title,
                    Body = body,
                    Time = DateTime.Now,
                    Logo = null,
                    IsRead = false,
                    Group = Categorize(app, app),
                    FromHook = true
                }, null);
            }
            catch (Exception ex) { Log.Warn("[Notify] read toast: " + ex.Message); }
        }

        private static List<string> ReadToastTexts(IntPtr hwnd)
        {
            var result = new List<string>();
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                if (root == null) return result;

                var texts = root.FindAll(
                    TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));

                foreach (AutomationElement el in texts)
                {
                    string s;
                    try { s = el.Current.Name; }
                    catch { continue; }
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    s = s.Trim();
                    if (!result.Contains(s)) result.Add(s);
                    if (result.Count >= 4) break;
                }
            }
            catch { }
            return result;
        }

        // ==================== ОБЩИЙ ЭМИТТЕР (дедупликация) ====================
        private void Emit(NotificationItem item, uint? listenerId)
        {
            string key = (item.AppName + "|" + item.Title + "|" + item.Body).ToLowerInvariant();
            lock (_seenKeys)
            {
                if (_seenKeys.ContainsKey(key)) return;
                _seenKeys[key] = DateTime.UtcNow;
                if (_seenKeys.Count > 256)
                {
                    var old = DateTime.UtcNow.AddMinutes(-5);
                    foreach (var k in _seenKeys.Where(p => p.Value < old).Select(p => p.Key).ToList())
                        _seenKeys.Remove(k);
                }
            }

            item.Id = listenerId ?? _nextHookId++;
            if (listenerId.HasValue) _map[listenerId.Value] = item;

            Added?.Invoke(item);
            Changed?.Invoke();
        }

        // Кнопка «🧪 Тест» в панели — проверяет конвейер без Windows.
        public void InjectTest()
        {
            Emit(new NotificationItem
            {
                AppName = "Тест",
                AppId = "test",
                Title = "Проверка перехвата",
                Body = $"UI и конвейер работают · {DateTime.Now:HH:mm:ss}",
                Time = DateTime.Now,
                Logo = null,
                IsRead = false,
                Group = "Прочее",
                FromHook = true
            }, null);
        }

        // ==================== SYSTEM LISTENER ====================
        private void StartPoll()
        {
            _poll ??= new Timer(async _ => await RefreshAsync(), null,
                TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
        }

        private async void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
        {
            try
            {
                _debounce?.Cancel();
                _debounce?.Dispose();
                _debounce = new CancellationTokenSource();
                var token = _debounce.Token;
                await Task.Delay(220, token);
                if (token.IsCancellationRequested) return;
                await RefreshAsync();
            }
            catch (TaskCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log.Warn("[Notify] changed: " + ex.Message); }
        }

        public async Task RefreshAsync()
        {
            if (_disposed || _listener == null || !AccessGranted || UsingPoll) return;
            if (!await _gate.WaitAsync(0)) return;

            try
            {
                var raw = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
                if (_disposed || raw == null) return;

                var alive = new HashSet<uint>();
                var fresh = new List<NotificationItem>();

                foreach (var n in raw.OrderByDescending(x => x.CreationTime).Take(MaxItems))
                {
                    alive.Add(n.Id);

                    string appId = "";
                    try { appId = n.AppInfo?.AppUserModelId ?? ""; } catch { }
                    if (appId.Contains("DynamicIsland", StringComparison.OrdinalIgnoreCase)) continue;

                    if (_map.TryGetValue(n.Id, out var known))
                    {
                        known.Time = n.CreationTime.LocalDateTime;
                        continue;
                    }

                    var item = Build(n, appId);
                    if (item == null) continue;
                    fresh.Add(item);
                }

                foreach (uint gone in _map.Keys.Where(k => !alive.Contains(k)).ToList())
                    _map.Remove(gone);

                foreach (var item in fresh.OrderBy(x => x.Time))
                    Emit(item, item.Id);

                Changed?.Invoke();
            }
            catch (Exception ex) { Log.Warn("[Notify] refresh: " + ex.Message); }
            finally { _gate.Release(); }
        }

        private NotificationItem? Build(UserNotification n, string appId)
        {
            try
            {
                string app = appId;
                try { app = n.AppInfo?.DisplayInfo?.DisplayName ?? appId; } catch { }
                if (string.IsNullOrWhiteSpace(app)) app = "Приложение";

                var texts = ExtractTexts(n);
                string title = texts.Count > 0 ? texts[0] : "";
                string body = texts.Count > 1 ? string.Join(" · ", texts.Skip(1).Take(2)) : "";
                if (title.Length == 0 && body.Length == 0) title = app;

                if (!_logoCache.TryGetValue(appId, out BitmapImage? logo))
                {
                    logo = TryLoadLogo(n);
                    _logoCache[appId] = logo;
                }

                return new NotificationItem
                {
                    Id = n.Id,
                    AppName = app,
                    AppId = appId,
                    Title = title,
                    Body = body,
                    Time = n.CreationTime.LocalDateTime,
                    Logo = logo,
                    IsRead = false,
                    Group = Categorize(app, appId),
                    FromHook = false
                };
            }
            catch (Exception ex) { Log.Warn("[Notify] build: " + ex.Message); return null; }
        }

        private static List<string> ExtractTexts(UserNotification n)
        {
            var result = new List<string>();
            try
            {
                object? payload = n.Notification;
                if (payload == null) return result;

                var t = payload.GetType();
                object? binding = t.GetMethod("GetBinding", new[] { typeof(string) })
                    ?.Invoke(payload, new object[] { "ToastGeneric" });

                if (binding == null)
                {
                    object? data = t.GetProperty("Data")?.GetValue(payload);
                    if (data != null)
                        binding = data.GetType().GetMethod("GetBinding", new[] { typeof(string) })
                            ?.Invoke(data, new object[] { "ToastGeneric" });
                }

                if (binding != null)
                {
                    if (binding.GetType().GetMethod("GetTextElements", Type.EmptyTypes)
                            ?.Invoke(binding, null) is System.Collections.IEnumerable elements)
                    {
                        foreach (object? el in elements)
                        {
                            if (el?.GetType().GetProperty("Text")?.GetValue(el) is string s &&
                                !string.IsNullOrWhiteSpace(s))
                                result.Add(s.Trim());
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        private static BitmapImage? TryLoadLogo(UserNotification n)
        {
            try
            {
                var info = n.AppInfo?.DisplayInfo;
                if (info == null) return null;
                var reference = info.GetLogo(new Windows.Foundation.Size(32, 32));
                if (reference == null) return null;

                using var stream = reference.OpenReadAsync().AsTask().GetAwaiter().GetResult();
                using var managed = stream.AsStreamForRead();
                var ms = new MemoryStream();
                managed.CopyTo(ms);
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 32;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        // ==================== ОБЩЕЕ ====================
        public static string Categorize(string app, string appId)
        {
            string s = ((app ?? "") + " " + (appId ?? "")).ToLowerInvariant();
            if (s.Contains("telegram") || s.Contains("discord") || s.Contains("whatsapp") ||
                s.Contains("slack") || s.Contains("teams") || s.Contains("viber") || s.Contains("signal"))
                return "Мессенджеры";
            if (s.Contains("steam") || s.Contains("epic") || s.Contains("battle.net") ||
                s.Contains("xbox") || s.Contains("gog") || s.Contains("riot"))
                return "Игры";
            if (s.Contains("outlook") || s.Contains("mail") || s.Contains("почта") || s.Contains("gmail"))
                return "Почта";
            if (s.Contains("chrome") || s.Contains("edge") || s.Contains("firefox") || s.Contains("браузер"))
                return "Браузеры";
            if (s.Contains("windows") || s.Contains("explorer") || s.Contains("defender") ||
                s.Contains("security") || s.Contains("update") || s.Contains("system") ||
                s.Contains("безопасн") || s.Contains("обновлен"))
                return "Система";
            return "Прочее";
        }

        public void Remove(uint id)
        {
            try
            {
                _map.Remove(id);
                if (!UsingPoll) _listener?.RemoveNotification(id);
                Changed?.Invoke();
            }
            catch (Exception ex) { Log.Warn("[Notify] remove: " + ex.Message); }
        }

        public void Clear()
        {
            try
            {
                if (!UsingPoll)
                {
                    foreach (uint id in _map.Keys.ToList())
                    {
                        try { _listener?.RemoveNotification(id); } catch { }
                    }
                }
                _map.Clear();
                lock (_seenKeys) _seenKeys.Clear();
                Changed?.Invoke();
            }
            catch (Exception ex) { Log.Warn("[Notify] clear: " + ex.Message); }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _poll?.Dispose();
                StopScan();
                _debounce?.Cancel();
                _debounce?.Dispose();
                if (_listener != null && _listenerHooked)
                    _listener.NotificationChanged -= OnNotificationChanged;
            }
            catch { }
            _map.Clear();
            _logoCache.Clear();
            _gate.Dispose();
        }
    }
}