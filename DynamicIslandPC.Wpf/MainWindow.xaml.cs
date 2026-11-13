using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Models;
using DynamicIslandPC.Wpf.Services;
using IOPath = System.IO.Path;

namespace DynamicIslandPC.Wpf
{
    public sealed class SpringEase : EasingFunctionBase
    {
        public double Stiffness { get; set; } = 170;
        public double Damping { get; set; } = 20;
        private static double S(double t, double w0, double z)
        {
            if (z < 1) { double wd = w0 * Math.Sqrt(1 - z * z); return 1 - Math.Exp(-z * w0 * t) * (Math.Cos(wd * t) + (z * w0 / wd) * Math.Sin(wd * t)); }
            return 1 - Math.Exp(-w0 * t) * (1 + w0 * t);
        }
        protected override double EaseInCore(double t)
        {
            double w0 = Math.Sqrt(Stiffness), z = Damping / (2 * w0);
            return S(t, w0, z) / Math.Max(1e-6, S(1, w0, z));
        }
        protected override Freezable CreateInstanceCore() => new SpringEase();
    }

    public partial class MainWindow : Window
    {
        private const int COLLAPSED_WIDTH = 320;
        private const int COLLAPSED_HEIGHT = 48;
        private const int EXPANDED_WIDTH = 620;
        private const int EXPANDED_HEIGHT = 360;
        private const int DOT_WIDTH = 22;
        private const int DOT_HEIGHT = 22;
        private const double SWIPE_THRESHOLD = 60;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTTRANSPARENT = -1;

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");
        private static readonly TabType[] TabOrder = { TabType.Sound, TabType.Clipboard, TabType.Shelf, TabType.Vpn };

        private readonly AudioService _audio;
        private readonly AppVolumeService _appVolume = new();
        private readonly ClipboardService _clipboard;
        private readonly LayoutService _layout;
        private readonly ShelfService _shelf;
        private readonly SystemMonitorService _systemMonitor = new();
        private readonly MediaSessionService _mediaSession = new();
        private readonly VpnService _vpnService = new();

        private IntPtr _hwnd;
        private bool _isExpanded, _isAnimating, _isUpdatingVolume, _osdShown, _isVpnToggling, _isFullScreen;
        private bool _swipeArmed, _swipeMoved, _isDraggingOut;
        private double _swipeStartX;
        private Point _dragStartPoint;
        private TabType _activeTab = TabType.Sound;

        private readonly Dictionary<TabType, (Button btn, Grid panel)> _tabs = new();
        private readonly DispatcherTimer _clockTimer = new();
        private readonly DispatcherTimer _osdHideTimer = new();
        private readonly DispatcherTimer _layoutTimer = new();
        private readonly DispatcherTimer _hoverTimer = new();
        private readonly DispatcherTimer _systemMonitorTimer = new();

        public MainWindow()
        {
            string svvPath = ResolveSvvPath();

            _audio = new AudioService(svvPath);
            _clipboard = new ClipboardService();
            _layout = new LayoutService();
            _shelf = new ShelfService();

            InitializeComponent();
            _appVolume.Start();

            _tabs[TabType.Sound] = (SoundTabBtn, SoundTab);
            _tabs[TabType.Clipboard] = (ClipboardTabBtn, ClipboardTab);
            _tabs[TabType.Shelf] = (ShelfTabBtn, ShelfTab);
            _tabs[TabType.Vpn] = (VpnTabBtn, VpnTab);

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

            SubscribeServices();

            _mediaSession.TrackChanged += (title, artist, cover) => Dispatcher.Invoke(() =>
            {
                PlayerTitleText.Text = string.IsNullOrEmpty(title) ? "Нет трека" : title;
                PlayerArtistText.Text = string.IsNullOrEmpty(artist) ? "YouTube / Spotify" : artist;
                if (cover != null) { PlayerCoverBorder.Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill }; PlayerCoverText.Visibility = Visibility.Collapsed; }
                else { PlayerCoverBorder.Background = new SolidColorBrush(Color.FromRgb(38, 51, 74)); PlayerCoverText.Visibility = Visibility.Visible; }
            });
            _mediaSession.PlaybackStateChanged += (isPlaying) => Dispatcher.Invoke(() =>
            {
                PlayPauseBtn.Content = isPlaying ? "\uE769" : "\uE768";
            });
            _mediaSession.Start();

            _vpnService.StatusChanged += (isConnected) => Dispatcher.Invoke(() => UpdateVpnUi(isConnected));
            UpdateVpnUi(_vpnService.IsConnected);

            _isUpdatingVolume = true;
            VolumeSlider.Value = _audio.CurrentVolume;
            _isUpdatingVolume = false;
            VolumeValueText.Text = $"{_audio.CurrentVolume}%";
            UpdateOsdUi("Система", _audio.CurrentVolume);
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            BuildShelfList();
            BuildClipboardList();
            SelectTab(TabType.Sound);

            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - 800) / 2;
            Top = SystemParameters.WorkArea.Top;

            Loaded += MainWindow_Loaded;
        }

        private static string ResolveSvvPath()
        {
            var candidates = new List<string>();
            string? exeDir = IOPath.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(exeDir)) candidates.Add(IOPath.Combine(exeDir, "tools", "SoundVolumeView.exe"));
            candidates.Add(IOPath.Combine(AppContext.BaseDirectory, "tools", "SoundVolumeView.exe"));
            try
            {
                string projectDir = IOPath.GetFullPath(IOPath.Combine(AppContext.BaseDirectory, "..", "..", ".."));
                candidates.Add(IOPath.Combine(projectDir, "tools", "SoundVolumeView.exe"));
            }
            catch { }
            foreach (var p in candidates) if (File.Exists(p)) return p;
            return candidates[0];
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            var st = new ScaleTransform(0.94, 0.94);
            IslandBorder.RenderTransformOrigin = new Point(0.5, 0.5);
            IslandBorder.RenderTransform = st;
            IslandBorder.Opacity = 0;
            Anim(IslandBorder, OpacityProperty, 1, 320, EaseOut());
            Anim(st, ScaleTransform.ScaleXProperty, 1, 520, Spring());
            Anim(st, ScaleTransform.ScaleYProperty, 1, 520, Spring());
        }

        private static SpringEase Spring() => new() { Stiffness = 180, Damping = 22, EasingMode = EasingMode.EaseIn };
        private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };
        private static CubicEase EaseIn() => new() { EasingMode = EasingMode.EaseIn };

        private static void Anim(IAnimatable o, DependencyProperty p, double to, double ms, IEasingFunction? ease = null, double delay = 0, EventHandler? done = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms));
            if (ease != null) a.EasingFunction = ease;
            if (delay > 0) a.BeginTime = TimeSpan.FromMilliseconds(delay);
            if (done != null) a.Completed += done;
            o.BeginAnimation(p, a);
        }

        private static TranslateTransform EnsureTranslate(UIElement e)
        {
            if (e.RenderTransform is TranslateTransform t) return t;
            t = new TranslateTransform(); e.RenderTransform = t; return t;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.AddClipboardFormatListener(_hwnd);
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                int x = (short)(lParam.ToInt64() & 0xFFFF);
                int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                Point pt = PointFromScreen(new Point(x, y));
                var pos = IslandBorder.TranslatePoint(new Point(0, 0), this);
                var rect = new Rect(pos.X, pos.Y, IslandBorder.ActualWidth, IslandBorder.ActualHeight);
                if (!rect.Contains(pt)) { handled = true; return new IntPtr(HTTRANSPARENT); }
            }
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                try { _clipboard.OnClipboardChanged(); } catch (Exception ex) { Debug.WriteLine($"[Clipboard] {ex.Message}"); }
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void SubscribeServices()
        {
            _audio.VolumeChanged += (s, vol) => Dispatcher.Invoke(() =>
            {
                if (_isUpdatingVolume) return;
                _isUpdatingVolume = true;
                try { VolumeSlider.Value = vol; VolumeValueText.Text = $"{vol}%"; UpdateOsdUi("Система", vol); ShowOSD(); }
                finally { _isUpdatingVolume = false; }
            });
            _audio.DevicesChanged += (s, e) => Dispatcher.Invoke(UpdateAudioButtons);
            _clipboard.HistoryChanged += (s, e) => Dispatcher.Invoke(() => { UpdateClipboardCount(); BuildClipboardList(); });
            _layout.Changed += (s, d) => Dispatcher.Invoke(() =>
            {
                LayoutText.Text = d.layout;
                CapsBadge.Visibility = d.capsLock ? Visibility.Visible : Visibility.Collapsed;
            });
            _shelf.Changed += (s, e) => Dispatcher.Invoke(() => { UpdateShelfCount(); BuildShelfList(); });
            _systemMonitor.Updated += (s, d) => Dispatcher.Invoke(() => UpdateSystemUi(d));
            _appVolume.AppVolumeChanged += (name, vol, _) => Dispatcher.Invoke(() => { UpdateOsdUi(name, vol); ShowOSD(); });
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            ClockText.Text = now.ToString("HH:mm");
            DateText.Text = now.ToString("dddd, d MMMM", new CultureInfo("ru-RU"));
        }

        private void UpdateVpnUi(bool isConnected)
        {
            VpnToggleBtn.ApplyTemplate();
            var bg = VpnToggleBtn.Template.FindName("BtnBg", VpnToggleBtn) as Ellipse;
            if (isConnected)
            {
                if (bg != null) { bg.Fill = (Brush)FindResource("VpnOnGradient"); bg.Stroke = new SolidColorBrush(Color.FromRgb(52, 211, 153)); }
                VpnIcon.Text = "\uE72E"; VpnIcon.Foreground = Brushes.White;
                VpnStatusText.Text = "Вкл"; VpnStatusText.Foreground = Brushes.White;
                VpnDetailsText.Text = "Соединение защищено";
                StartPulseAnimation();
                VpnIndicatorText.Visibility = Visibility.Visible;
            }
            else
            {
                if (bg != null) { bg.Fill = (Brush)FindResource("VpnOffGradient"); bg.Stroke = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)); }
                VpnIcon.Text = "\uE7E8"; VpnIcon.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                VpnStatusText.Text = "Выкл"; VpnStatusText.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184));
                VpnDetailsText.Text = "Нажми, чтобы защитить соединение";
                StopPulseAnimation();
                VpnIndicatorText.Visibility = Visibility.Collapsed;
            }
        }

        private void StartPulseAnimation()
        {
            Pulse(PulseRing1, 1.18, 0.7, 0);
            Pulse(PulseRing2, 1.32, 0.5, 300);
        }
        private void Pulse(Ellipse ring, double scaleTo, double opacityFrom, double delayMs)
        {
            if (ring.RenderTransform is not ScaleTransform st) { st = new ScaleTransform(1, 1); ring.RenderTransform = st; }
            var dur = TimeSpan.FromSeconds(2.2);
            var grow = new DoubleAnimation(0.82, scaleTo, dur) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EaseOut(), BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            var fade = new DoubleAnimation(opacityFrom, 0, dur) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = EaseIn(), BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, fade);
        }
        private void StopPulseAnimation()
        {
            foreach (var r in new[] { PulseRing1, PulseRing2 })
            {
                r.BeginAnimation(OpacityProperty, null); r.Opacity = 0;
                if (r.RenderTransform is ScaleTransform st) { st.BeginAnimation(ScaleTransform.ScaleXProperty, null); st.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
            }
        }

        private async void VpnToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            _isVpnToggling = true;
            VpnToggleBtn.IsEnabled = false;
            VpnIcon.Text = "\uE895"; VpnStatusText.Text = "Связь…";
            try { await _vpnService.ToggleAsync(); } catch (Exception ex) { Debug.WriteLine($"[VPN] {ex.Message}"); }
            await Task.Delay(450);
            VpnToggleBtn.IsEnabled = true;
            _isVpnToggling = false;
        }

        private bool IsFullScreenApp()
        {
            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == _hwnd) return false;
            if (!GetWindowRect(h, out RECT r)) return false;
            return r.Left <= 0 && r.Top <= 0 && r.Right >= (int)SystemParameters.PrimaryScreenWidth && r.Bottom >= (int)SystemParameters.PrimaryScreenHeight;
        }

        private void CheckHover()
        {
            if (_isAnimating || _isDraggingOut || _isVpnToggling) return;
            if (IslandBorder.ContextMenu?.IsOpen == true) return;
            if (_isExpanded && Mouse.Captured != null) return;

            bool wasFull = _isFullScreen;
            _isFullScreen = IsFullScreenApp();

            NativeMethods.GetCursorPos(out var cur);
            var pos = IslandBorder.PointToScreen(new Point(0, 0));
            int buffer = _isFullScreen && !_isExpanded ? 10 : 0;
            bool inside = cur.X >= pos.X - buffer && cur.X <= pos.X + IslandBorder.ActualWidth + buffer &&
                          cur.Y >= pos.Y - buffer && cur.Y <= pos.Y + IslandBorder.ActualHeight + buffer;

            if (inside && !_isExpanded) ToggleExpand();
            else if (!inside && _isExpanded) ToggleExpand();
            else if (wasFull != _isFullScreen && !_isExpanded && !_isAnimating) ApplyCollapsedVisual();
        }

        private void ApplyCollapsedVisual()
        {
            if (_isExpanded) return;
            int tw = _isFullScreen ? DOT_WIDTH : COLLAPSED_WIDTH;
            int th = _isFullScreen ? DOT_HEIGHT : COLLAPSED_HEIGHT;
            Anim(IslandBorder, WidthProperty, tw, 300, EaseOut());
            Anim(IslandBorder, HeightProperty, th, 300, EaseOut());
            IslandBorder.CornerRadius = new CornerRadius(_isFullScreen ? 11 : 24);
            TopPanel.Visibility = _isFullScreen || _osdShown ? Visibility.Collapsed : Visibility.Visible;
            if (_isFullScreen) { TabBar.Visibility = Visibility.Collapsed; TabContentGrid.Visibility = Visibility.Collapsed; }
        }

        private void ToggleExpand()
        {
            if (_isAnimating) return;
            _isAnimating = true;
            _isExpanded = !_isExpanded;

            int tw = _isExpanded ? EXPANDED_WIDTH : (_isFullScreen ? DOT_WIDTH : COLLAPSED_WIDTH);
            int th = _isExpanded ? EXPANDED_HEIGHT : (_isFullScreen ? DOT_HEIGHT : COLLAPSED_HEIGHT);

            Anim(IslandBorder, WidthProperty, tw, 430, Spring(), done: (s, e) => _isAnimating = false);
            Anim(IslandBorder, HeightProperty, th, 430, Spring());

            if (_isExpanded)
            {
                IslandBorder.CornerRadius = new CornerRadius(26);
                var tb = EnsureTranslate(TabBar); var tc = EnsureTranslate(TabContentGrid);

                TabBar.BeginAnimation(OpacityProperty, null);
                TabContentGrid.BeginAnimation(OpacityProperty, null);
                TabBar.Visibility = Visibility.Visible; TabContentGrid.Visibility = Visibility.Visible;
                TabBar.Opacity = 0; TabContentGrid.Opacity = 0; tb.Y = 10; tc.Y = 14;

                Anim(TabBar, OpacityProperty, 1, 220, EaseOut(), 60);
                Anim(TabContentGrid, OpacityProperty, 1, 260, EaseOut(), 90);
                Anim(tb, TranslateTransform.YProperty, 0, 430, Spring(), 60);
                Anim(tc, TranslateTransform.YProperty, 0, 470, Spring(), 90);

                TopPanel.Visibility = _osdShown ? Visibility.Collapsed : Visibility.Visible;
                DatePanel.Visibility = Visibility.Visible;
                Anim(TopPanel, HeightProperty, 78, 430, Spring());
                Anim(ClockViewbox, HeightProperty, 46, 430, Spring());
                Anim(DatePanel, OpacityProperty, 1, 260, EaseOut(), 80);
                Anim(CountersPanel, OpacityProperty, 0, 120, EaseOut());
                foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox })
                { Anim(vb, WidthProperty, 46, 430, Spring()); Anim(vb, HeightProperty, 46, 430, Spring()); }
                UpdateAudioButtons();
                UpdateTabPillWidth();
            }
            else
            {
                IslandBorder.CornerRadius = new CornerRadius(_isFullScreen ? 11 : 24);
                Anim(TabBar, OpacityProperty, 0, 140, EaseIn());
                Anim(TabContentGrid, OpacityProperty, 0, 140, EaseIn(), done: (s, e) =>
                {
                    if (!_isExpanded) { TabContentGrid.Visibility = Visibility.Collapsed; TabBar.Visibility = Visibility.Collapsed; }
                });

                if (_isFullScreen) TopPanel.Visibility = Visibility.Collapsed;
                else
                {
                    TopPanel.Visibility = _osdShown ? Visibility.Collapsed : Visibility.Visible;
                    Anim(TopPanel, HeightProperty, 48, 430, Spring());
                    Anim(ClockViewbox, HeightProperty, 30, 430, Spring());
                    Anim(DatePanel, OpacityProperty, 0, 120, EaseIn(), done: (s, e) => { if (!_isExpanded) DatePanel.Visibility = Visibility.Collapsed; });
                    Anim(CountersPanel, OpacityProperty, 1, 260, EaseOut(), 80);
                    foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox })
                    { Anim(vb, WidthProperty, 24, 430, Spring()); Anim(vb, HeightProperty, 24, 430, Spring()); }
                }
            }

            if (_osdShown)
            {
                bool dot = !_isExpanded && _isFullScreen;
                SmallOsd.Visibility = _isExpanded || dot ? Visibility.Collapsed : Visibility.Visible;
                LargeOsd.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TabGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTabPillWidth();

        private void UpdateTabPillWidth()
        {
            if (TabGrid.ActualWidth == 0) return;
            TabPill.Width = TabGrid.ActualWidth / 4.0;
            TabPill.Height = Math.Max(0, TabGrid.ActualHeight - 4);
            int index = Array.IndexOf(TabOrder, _activeTab);
            ((TranslateTransform)TabPill.RenderTransform).X = index * TabPill.Width;
        }

        private void SelectTab(TabType tab)
        {
            if (_activeTab == tab && _tabs[tab].panel.Visibility == Visibility.Visible) return;
            int oldIndex = Array.IndexOf(TabOrder, _activeTab);
            int newIndex = Array.IndexOf(TabOrder, tab);
            int dir = newIndex >= oldIndex ? 1 : -1;

            Anim((TranslateTransform)TabPill.RenderTransform, TranslateTransform.XProperty, newIndex * TabPill.Width, 380, Spring());

            var oldPanel = _tabs[_activeTab].panel;
            var newPanel = _tabs[tab].panel;
            _activeTab = tab;

            Color accent = tab switch
            {
                TabType.Sound => Color.FromRgb(34, 211, 238),
                TabType.Clipboard => Color.FromRgb(167, 139, 250),
                TabType.Shelf => Color.FromRgb(245, 158, 11),
                TabType.Vpn => Color.FromRgb(52, 211, 153),
                _ => Colors.White
            };
            Resources["AccentColor"] = new SolidColorBrush(accent);
            Resources["AccentGlow"] = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B));
            Resources["AccentColorValue"] = accent;

            foreach (var kv in _tabs)
                kv.Value.btn.Foreground = kv.Key == tab ? Brushes.White : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            UpdateAudioButtons();

            if (oldPanel.Visibility == Visibility.Collapsed && newPanel.Visibility == Visibility.Collapsed)
            { newPanel.Visibility = Visibility.Visible; newPanel.Opacity = 1; return; }

            var oldT = new TranslateTransform(); oldPanel.RenderTransform = oldT;
            var newT = new TranslateTransform { X = 26 * dir }; newPanel.RenderTransform = newT;
            var oldOp = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = EaseIn() };
            oldOp.Completed += (s, e) => { if (!ReferenceEquals(_tabs[_activeTab].panel, oldPanel)) { oldPanel.Visibility = Visibility.Collapsed; oldPanel.RenderTransform = null; } };
            newPanel.Visibility = Visibility.Visible;
            Anim(oldT, TranslateTransform.XProperty, -26 * dir, 220, EaseOut());
            oldPanel.BeginAnimation(OpacityProperty, oldOp);
            Anim(newT, TranslateTransform.XProperty, 0, 360, Spring());
            Anim(newPanel, OpacityProperty, 1, 240, EaseOut(), 40);
        }

        private void SoundTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Sound);
        private void ClipboardTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Clipboard);
        private void ShelfTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Shelf);
        private void VpnTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Vpn);

        private void SwitchTabRelative(int dir)
        {
            int i = Array.IndexOf(TabOrder, _activeTab) + dir;
            if (i < 0) i = TabOrder.Length - 1;
            if (i >= TabOrder.Length) i = 0;
            SelectTab(TabOrder[i]);
        }

        private void TabContent_PreviewDown(object sender, MouseButtonEventArgs e)
        {
            _swipeMoved = false;
            if (IsInteractiveOrigin(e.OriginalSource as DependencyObject)) { _swipeArmed = false; return; }
            _swipeArmed = true; _swipeStartX = e.GetPosition(TabContentGrid).X;
            TabContentGrid.CaptureMouse();
        }
        private void TabContent_PreviewMove(object sender, MouseEventArgs e)
        {
            if (!_swipeArmed || e.LeftButton != MouseButtonState.Pressed) return;
            if (Math.Abs(e.GetPosition(TabContentGrid).X - _swipeStartX) > SWIPE_THRESHOLD) _swipeMoved = true;
        }
        private void TabContent_PreviewUp(object sender, MouseButtonEventArgs e)
        {
            if (!_swipeArmed) return;
            _swipeArmed = false; TabContentGrid.ReleaseMouseCapture();
            double dx = e.GetPosition(TabContentGrid).X - _swipeStartX;
            if (_swipeMoved && Math.Abs(dx) >= SWIPE_THRESHOLD) SwitchTabRelative(dx < 0 ? 1 : -1);
        }
        private bool IsInteractiveOrigin(DependencyObject? d)
        {
            while (d != null && !ReferenceEquals(d, TabContentGrid))
            {
                if (d is Slider || d is ButtonBase || d == ShelfStack || d == ClipboardStack) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files)
            { _shelf.AddFiles(files); if (_isExpanded) SelectTab(TabType.Shelf); }
            e.Handled = true;
        }
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingVolume) return;
            _isUpdatingVolume = true;
            try { _audio.SetVolume((int)e.NewValue); VolumeValueText.Text = $"{(int)e.NewValue}%"; UpdateOsdUi("Система", (int)e.NewValue); ShowOSD(); }
            finally { _isUpdatingVolume = false; }
        }
        private async void PlayPause_Click(object sender, RoutedEventArgs e) { try { await _mediaSession.TogglePlayPauseAsync(); } catch { } }
        private async void Prev_Click(object sender, RoutedEventArgs e) { try { await _mediaSession.PrevAsync(); } catch { } }
        private async void Next_Click(object sender, RoutedEventArgs e) { try { await _mediaSession.NextAsync(); } catch { } }
        private void OsdGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        { try { Process.Start(new ProcessStartInfo("sndvol.exe") { UseShellExecute = true }); } catch { } e.Handled = true; }

        private void UpdateSystemUi((double CpuPercent, double RamPercent, double GpuPercent, ulong UsedRamMb, ulong TotalRamMb) d)
        {
            BigCpuValue.Text = $"{(int)d.CpuPercent}";
            BigRamValue.Text = $"{(int)d.RamPercent}";
            BigGpuValue.Text = $"{(int)d.GpuPercent}";
            SetRingValue(CpuRing, d.CpuPercent);
            SetRingValue(RamRing, d.RamPercent);
            SetRingValue(GpuRing, d.GpuPercent);
        }
        private void SetRingValue(Ellipse ring, double percent)
        {
            double dash = 2 * Math.PI * 24 / 4;
            double target = (1 - Math.Clamp(percent, 0, 100) / 100.0) * dash;
            Anim(ring, Shape.StrokeDashOffsetProperty, target, 520, EaseOut());
        }

        private void UpdateAudioButtons()
        {
            var activeId = _audio.ActiveDeviceId;
            var dev = _audio.Devices;
            var accent = (SolidColorBrush)Resources["AccentColor"];
            var glow = (SolidColorBrush)Resources["AccentGlow"];
            var surface = (SolidColorBrush)FindResource("Surface");
            var stroke = (SolidColorBrush)FindResource("Stroke");

            void Set(Button b, int i)
            {
                bool active = dev[i].ID == activeId;
                bool head = dev[i].FriendlyName.Contains("Head", StringComparison.OrdinalIgnoreCase) || dev[i].FriendlyName.Contains("Науш", StringComparison.OrdinalIgnoreCase);
                b.Content = CreatePillContent(head ? "\uE7F6" : "\uE7F5", dev[i].FriendlyName);
                b.Background = active ? glow : surface;
                b.BorderBrush = active ? accent : stroke;
                b.Foreground = active ? accent : Brushes.White;
                b.IsEnabled = true;
            }
            if (dev.Count > 0) Set(BtnAudioSpeakers, 0);
            else { BtnAudioSpeakers.IsEnabled = false; BtnAudioSpeakers.Content = CreatePillContent("\uE74F", "Нет устройств"); BtnAudioSpeakers.Background = surface; BtnAudioSpeakers.BorderBrush = stroke; BtnAudioSpeakers.Foreground = Brushes.White; }
            if (dev.Count > 1) { BtnAudioHeadphones.Visibility = Visibility.Visible; Set(BtnAudioHeadphones, 1); }
            else BtnAudioHeadphones.Visibility = Visibility.Collapsed;
        }

        private StackPanel CreatePillContent(string glyph, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
            var ic = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var tx = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 92 };
            var bind = new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) };
            ic.SetBinding(TextBlock.ForegroundProperty, bind);
            tx.SetBinding(TextBlock.ForegroundProperty, bind);
            sp.Children.Add(ic); sp.Children.Add(tx);
            return sp;
        }
        private void BtnAudioSpeakers_Click(object sender, RoutedEventArgs e) { if (_audio.Devices.Count > 0) _audio.SwitchDevice(_audio.Devices[0].ID); }
        private void BtnAudioHeadphones_Click(object sender, RoutedEventArgs e) { if (_audio.Devices.Count > 1) _audio.SwitchDevice(_audio.Devices[1].ID); }

        private static (string glyph, Color color) FileGlyph(string ext) => ext.ToUpperInvariant().TrimStart('.') switch
        {
            "PDF" or "DOC" or "DOCX" or "TXT" or "MD" or "RTF" => ("\uE8A5", Color.FromRgb(96, 165, 250)),
            "XLS" or "XLSX" or "CSV" => ("\uE80A", Color.FromRgb(52, 211, 153)),
            "JPG" or "JPEG" or "PNG" or "GIF" or "WEBP" or "BMP" or "SVG" => ("\uEB9F", Color.FromRgb(167, 139, 250)),
            "MP3" or "WAV" or "FLAC" or "OGG" or "M4A" => ("\uE8D6", Color.FromRgb(34, 211, 238)),
            "MP4" or "AVI" or "MKV" or "MOV" or "WEBM" => ("\uE8B2", Color.FromRgb(244, 114, 182)),
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" => ("\uE8B7", Color.FromRgb(245, 158, 11)),
            "CS" or "CPP" or "PY" or "JS" or "TS" or "JSON" or "XML" or "HTML" or "CSS" => ("\uE943", Color.FromRgb(52, 211, 153)),
            "EXE" or "MSI" or "BAT" or "PS1" => ("\uE713", Color.FromRgb(148, 163, 184)),
            _ => ("\uE7C3", Color.FromRgb(148, 163, 184))
        };

        private void BuildShelfList()
        {
            ShelfStack.Children.Clear();
            if (_shelf.Files.Count == 0) { ShelfStack.Children.Add(EmptyHint("Перетащи файлы сюда")); return; }
            foreach (string file in _shelf.Files)
            {
                string fileName = IOPath.GetFileName(file);
                var (glyph, color) = FileGlyph(IOPath.GetExtension(file));
                var grid = RowShell(48);
                grid.PreviewMouseLeftButtonDown += (s, e) => _dragStartPoint = e.GetPosition(null);
                grid.MouseMove += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed && !_isDraggingOut)
                    {
                        Vector diff = _dragStartPoint - e.GetPosition(null);
                        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                        {
                            _isDraggingOut = true;
                            try { DragDrop.DoDragDrop(grid, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy); }
                            finally { _isDraggingOut = false; }
                        }
                    }
                };
                grid.Children.Add(IconChip(glyph, color));
                grid.Children.Add(RowText(fileName, 1));
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(actions, 2);
                actions.Children.Add(IconBtn("\uE76C", () => ShelfService.Open(file)));
                actions.Children.Add(IconBtn("\uE838", () => ShelfService.OpenFolder(file)));
                actions.Children.Add(IconBtn("\uE711", () => _shelf.Remove(file), Color.FromRgb(239, 68, 68)));
                grid.Children.Add(actions);
                ShelfStack.Children.Add(grid);
            }
        }

        private void BuildClipboardList()
        {
            ClipboardStack.Children.Clear();
            if (_clipboard.History.Count == 0) { ClipboardStack.Children.Add(EmptyHint("Скопируй что-нибудь")); return; }
            foreach (var item in _clipboard.History)
            {
                var grid = RowShell(48);
                grid.Cursor = Cursors.Hand;
                if (item.Type == ClipboardItemType.Color && item.Color.HasValue)
                    grid.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(item.Color.Value), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                else if (item.Type == ClipboardItemType.Image && item.Image != null)
                    grid.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(38, 51, 74)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Child = new Image { Source = item.Image, Stretch = Stretch.UniformToFill } });
                else grid.Children.Add(IconChip(item.Type == ClipboardItemType.Text ? "\uE8C8" : "\uE77F", Color.FromRgb(167, 139, 250)));

                grid.Children.Add(RowText(item.DisplayText, 1));
                Button paste = null!;
                Action restore = () => { _clipboard.RestoreItem(item); FlashRestoreFeedback(paste); };
                paste = IconBtn("\uE77F", restore);
                Grid.SetColumn(paste, 2); paste.VerticalAlignment = VerticalAlignment.Center; paste.Margin = new Thickness(0, 0, 6, 0);
                grid.Children.Add(paste);
                grid.MouseLeftButtonUp += (s, e) => { if (!_swipeMoved) restore(); };
                ClipboardStack.Children.Add(grid);
            }
        }

        private void FlashRestoreFeedback(Button btn)
        {
            if (btn.Tag is DispatcherTimer old) old.Stop();
            btn.Content = "\uE73E"; btn.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            btn.Tag = t;
            t.Tick += (s, e) => { t.Stop(); btn.Tag = null; btn.Content = "\uE77F"; btn.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)); };
            t.Start();
        }

        private void ClearClipboard_Click(object sender, RoutedEventArgs e) => _clipboard.Clear();
        private void MenuClearClipboard_Click(object sender, RoutedEventArgs e) => _clipboard.Clear();
        private void MenuClearShelf_Click(object sender, RoutedEventArgs e) => _shelf.Clear();
        private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void UpdateOsdUi(string appName, int volume)
        {
            string g = volume == 0 ? "\uE74F" : "\uE767";
            SmallOsdIcon.Text = g; LargeOsdIcon.Text = g;
            SmallOsdAppName.Text = appName; LargeOsdAppName.Text = appName;
            SmallOsdPercentText.Text = $"{volume}%"; LargeOsdPercentText.Text = $"{volume}%";
            Anim(SmallOsdProgress, ProgressBar.ValueProperty, volume, 220, EaseOut());
            Anim(LargeOsdProgress, ProgressBar.ValueProperty, volume, 220, EaseOut());
        }

        private void ShowOSD()
        {
            if (!_isExpanded && _isFullScreen) return;
            _osdHideTimer.Stop(); _osdHideTimer.Start(); _osdShown = true;

            SmallOsd.Visibility = _isExpanded ? Visibility.Collapsed : Visibility.Visible;
            LargeOsd.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
            OsdGrid.Visibility = Visibility.Visible;

            // Синхронный кроссфейд: шапка и OSD одной длины, без скачка размеров
            Anim(OsdGrid, OpacityProperty, 1, 160, EaseOut());
            if (TopPanel.Visibility == Visibility.Visible)
                Anim(TopPanel, OpacityProperty, 0, 160, EaseIn(), done: (s, e) => { if (_osdShown) TopPanel.Visibility = Visibility.Collapsed; });
        }

        private void HideOSD()
        {
            _osdHideTimer.Stop();
            if (!_osdShown) return;
            _osdShown = false;
            Anim(OsdGrid, OpacityProperty, 0, 160, EaseIn(), done: (s, e) => { if (!_osdShown) OsdGrid.Visibility = Visibility.Collapsed; });
            TopPanel.Visibility = (!_isExpanded && _isFullScreen) ? Visibility.Collapsed : Visibility.Visible;
            if (TopPanel.Visibility == Visibility.Visible) Anim(TopPanel, OpacityProperty, 1, 160, EaseOut());
        }

        private void UpdateShelfCount()
        {
            ShelfCountText.Text = _shelf.Files.Count.ToString();
            ShelfCountText.Visibility = _shelf.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        private void UpdateClipboardCount()
        {
            ClipboardCountText.Text = _clipboard.History.Count.ToString();
            ClipboardCountText.Visibility = _clipboard.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private Grid RowShell(double height = 44)
        {
            var g = new Grid { Height = height, Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var normal = new SolidColorBrush(Color.FromArgb(14, 255, 255, 255));
            var hover = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            g.Background = normal;
            g.MouseEnter += (s, e) => g.Background = hover;
            g.MouseLeave += (s, e) => g.Background = normal;
            return g;
        }
        private Border IconChip(string glyph, Color color) => new()
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(30, color.R, color.G, color.B)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 15, Foreground = new SolidColorBrush(color), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        private TextBlock RowText(string text, int col)
        {
            var t = new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(t, col); return t;
        }
        private TextBlock EmptyHint(string text) => new()
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 16, 0, 16)
        };
        private Button IconBtn(string glyph, Action onClick, Color? fg = null)
        {
            var b = new Button { Style = (Style)FindResource("MediaButton"), Content = glyph, FontSize = 13, Width = 30, Height = 30 };
            b.Foreground = fg.HasValue ? new SolidColorBrush(fg.Value) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            b.Click += (s, e) => onClick();
            return b;
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer.Stop(); _osdHideTimer.Stop(); _layoutTimer.Stop(); _hoverTimer.Stop(); _systemMonitorTimer.Stop();
            if (_hwnd != IntPtr.Zero) NativeMethods.RemoveClipboardFormatListener(_hwnd);
            _audio.Dispose(); _systemMonitor.Dispose(); _mediaSession.Dispose(); _appVolume.Dispose(); _vpnService.Dispose();
            base.OnClosed(e);
        }
    }
}