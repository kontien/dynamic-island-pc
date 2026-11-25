using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Infrastructure;
using DynamicIslandPC.Wpf.Models;
using DynamicIslandPC.Wpf.Services;
using IOPath = System.IO.Path;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow : Window
    {
        private const int COLLAPSED_WIDTH = 280;
        private const int COLLAPSED_HEIGHT = 44;
        private const int EXPANDED_WIDTH = 640;
        private const int EXPANDED_HEIGHT = 620;
        private const int DOT_WIDTH = 20;
        private const int DOT_HEIGHT = 20;
        private const int HEAD_COLLAPSED = 44;
        private const int HEAD_EXPANDED = 82;
        private const int CLOCK_COLLAPSED = 28;
        private const int CLOCK_EXPANDED = 48;
        private const int RING_COLLAPSED = 24;
        private const int RING_EXPANDED = 42;

        private readonly AudioService _audio;
        private readonly ClipboardService _clipboard;
        private readonly LayoutService _layout;
        private readonly ShelfService _shelf;
        private readonly AppVolumeService _appVolume;
        private readonly SystemMonitorService _systemMonitor = new();
        private readonly MediaSessionService _mediaSession = new();
        private readonly NotificationService _notifService = new();
        private readonly VpnService _vpnService = new();
        private readonly CancellationTokenSource _lifetimeCts = new();

        private IntPtr _hwnd;
        private HwndSource? _hwndSource;
        private HotkeyService? _hotkeyService;
        private bool _clipboardListenerRegistered;
        private bool _isClosing;
        private bool _isExpanded;
        private bool _isAnimating;
        private bool _isUpdatingVolume;
        private bool _osdShown;
        private bool _isVpnToggling;
        private bool _isMuted;
        private bool _isFullScreen;
        private bool _isDraggingOut;
        private bool _pinned;
        private bool _dnd;
        private bool _isTileFocused;
        private bool _isUpdatingAppVolume;
        private bool _shuffleOn;
        private bool _repeatOn;
        private TimeSpan _mediaDuration = TimeSpan.Zero;
        private TimeSpan _mediaPosition = TimeSpan.Zero;
        private int _currentSessionPid;
        private readonly List<(string Title, string Artist, DateTime Time)> _trackHistory = new();
        private Point _dragStartPoint;

        private readonly DispatcherTimer _clockTimer = new();
        private readonly DispatcherTimer _osdHideTimer = new();
        private readonly DispatcherTimer _layoutTimer = new();
        private readonly DispatcherTimer _hoverTimer = new();
        private readonly DispatcherTimer _systemMonitorTimer = new();
        private readonly DispatcherTimer _eqTimer = new();
        private readonly DispatcherTimer _timelineTimer = new();
        private readonly DispatcherTimer _processTimer = new();

        public MainWindow()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string svvPath = IOPath.Combine(appDir, "tools", "SoundVolumeView.exe");
            if (!File.Exists(svvPath))
            {
                string projectDir = IOPath.GetFullPath(IOPath.Combine(appDir, "..", "..", ".."));
                svvPath = IOPath.Combine(projectDir, "tools", "SoundVolumeView.exe");
            }

            _audio = new AudioService(svvPath);
            _clipboard = new ClipboardService();
            _layout = new LayoutService();
            _shelf = new ShelfService();
            _appVolume = new AppVolumeService();

            InitializeComponent();
            Loaded += MainWindow_Loaded;

            _vpnService.ConnectionName = "MyVPN";
            _vpnService.Start();

            _appVolume.Start();
            _appVolume.AppVolumesChanged += () => DispatchToUi(BuildAppVolumeList);

            UpdateClock();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();

            _osdHideTimer.Interval = TimeSpan.FromSeconds(2);
            _osdHideTimer.Tick += (s, e) => HideOSD();

            _layoutTimer.Interval = TimeSpan.FromMilliseconds(200);
            _layoutTimer.Tick += (s, e) => _layout.Poll();
            _layoutTimer.Start();

            _hoverTimer.Interval = TimeSpan.FromMilliseconds(80);
            _hoverTimer.Tick += (s, e) => CheckHover();
            _hoverTimer.Start();

            _systemMonitorTimer.Interval = TimeSpan.FromSeconds(1);
            _systemMonitorTimer.Tick += (s, e) => _systemMonitor.Poll();
            _systemMonitorTimer.Start();

            _eqTimer.Interval = TimeSpan.FromMilliseconds(80);
            _eqTimer.Tick += (s, e) => AnimateEq();
            _eqTimer.Start();

            _timelineTimer.Interval = TimeSpan.FromSeconds(1);
            _timelineTimer.Tick += (s, e) => PollMediaTimeline();
            _timelineTimer.Start();

            _processTimer.Interval = TimeSpan.FromSeconds(2);
            _processTimer.Tick += (s, e) =>
            {
                if (_isTileFocused && SystemPanel.Visibility == Visibility.Visible) BuildProcessList();
            };
            _processTimer.Start();

            SubscribeServices();

            InitGlass();
            InitNotifications();

            _ = _mediaSession.StartAsync(_lifetimeCts.Token);
            _ = _notifService.StartAsync(_lifetimeCts.Token);

            UpdateVpnUi(_vpnService.IsConnected);

            _isUpdatingVolume = true;
            VolumeSlider.Value = _audio.CurrentVolume;
            VolBigText.Text = $"{_audio.CurrentVolume}%";
            _isUpdatingVolume = false;
            UpdateOsdUi("Система", _audio.CurrentVolume);
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            BuildShelfList();
            BuildClipboardList();
            BuildDeviceStack();
            UpdateShelfCount();
            UpdateClipboardCount();

            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - 900) / 2;
            Top = SystemParameters.WorkArea.Top;
        }

        private void PollMediaTimeline()
        {
            try
            {
                var m = _mediaSession.GetType().GetMethod("PollTimeline", Type.EmptyTypes);
                m?.Invoke(_mediaSession, null);
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            _hwndSource = HwndSource.FromHwnd(_hwnd);
            _hwndSource?.AddHook(WndProc);
            if (_hwnd != IntPtr.Zero)
            {
                _hotkeyService = new HotkeyService(_hwnd);
                _hotkeyService.RegisterScreenshot(CaptureScreenshot);
            }
            EnsureClipboardListener();
            int pref = 1;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            int border = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_hotkeyService?.HandleMessage(msg, wParam) == true) { handled = true; return IntPtr.Zero; }
            if (msg == NativeMethods.WM_NCHITTEST && IslandBorder != null)
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                Point pt = PointFromScreen(new Point(x, y));
                var origin = IslandBorder.TranslatePoint(new Point(0, 0), this);
                var rect = new Rect(origin.X, origin.Y, IslandBorder.ActualWidth, IslandBorder.ActualHeight);
                if (!rect.Contains(pt)) { handled = true; return new IntPtr(NativeMethods.HTTRANSPARENT); }
            }
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE) { try { _clipboard.OnClipboardChanged(); } catch { } handled = true; }
            return IntPtr.Zero;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureClipboardListener();
            _clipboard.OnClipboardChanged();
            UpdateClipboardCount();
            BuildClipboardList();
            UpdateShelfCount();
            BuildShelfList();
            _hasWindowUnder = IsWindowUnderIsland();
        }

        private void EnsureClipboardListener()
        {
            if (_clipboardListenerRegistered || _hwnd == IntPtr.Zero) return;
            _clipboardListenerRegistered = NativeMethods.AddClipboardFormatListener(_hwnd);
            if (!_clipboardListenerRegistered)
                Log.Warn("[Clipboard] не удалось зарегистрировать listener окна");
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosing = true;
            _lifetimeCts.Cancel();
            _clockTimer.Stop(); _osdHideTimer.Stop(); _layoutTimer.Stop(); _hoverTimer.Stop();
            _systemMonitorTimer.Stop(); _eqTimer.Stop(); _timelineTimer.Stop(); _processTimer.Stop();
            UnsubscribeServices();
            TryCleanup(() => _appVolume.Dispose(), "app volume");
            TryCleanup(() => _hotkeyService?.Dispose(), "hotkeys");
            _hotkeyService = null;
            if (_hwndSource != null) { _hwndSource.RemoveHook(WndProc); _hwndSource = null; }
            if (_clipboardListenerRegistered && _hwnd != IntPtr.Zero)
            { NativeMethods.RemoveClipboardFormatListener(_hwnd); _clipboardListenerRegistered = false; }
            TryCleanup(_audio.Dispose, "audio");
            TryCleanup(_systemMonitor.Dispose, "system monitor");
            TryCleanup(_mediaSession.Dispose, "media session");
            TryCleanup(_notifService.Dispose, "notifications");
            TryCleanup(_vpnService.Dispose, "VPN service");
            _lifetimeCts.Dispose();
            base.OnClosed(e);
        }

        private static void TryCleanup(Action cleanup, string name)
        { try { cleanup(); } catch (Exception ex) { Log.Error($"[Cleanup] {name}", ex); } }
    }
}