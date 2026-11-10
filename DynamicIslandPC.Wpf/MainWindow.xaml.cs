using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Models;
using DynamicIslandPC.Wpf.Services;

using IOPath = System.IO.Path;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow : Window
    {
        private const int COLLAPSED_WIDTH = 280;
        private const int COLLAPSED_HEIGHT = 44;
        private const int EXPANDED_WIDTH = 420;
        private const int EXPANDED_HEIGHT = 520;
        private const int ANIM_MS = 450;
        private const double SWIPE_THRESHOLD = 60;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private readonly AudioService _audio;
        private readonly ClipboardService _clipboard;
        private readonly LayoutService _layout;
        private readonly ShelfService _shelf;
        private readonly PlayerService _player = new();
        private readonly SystemMonitorService _systemMonitor = new();

        private IntPtr _hwnd;
        private bool _isExpanded;
        private bool _isAnimating;
        private bool _isUpdatingVolume;
        private TabType _activeTab = TabType.Sound;
        private bool _swipeArmed, _swipeMoved;
        private double _swipeStartX;
        private bool _isUpdatingSeek;

        private readonly Dictionary<TabType, (Button btn, Grid panel)> _tabs = new();

        private readonly System.Windows.Threading.DispatcherTimer _clockTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _osdHideTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _layoutTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _hoverTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _playerPosTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _activeWindowTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _systemMonitorTimer = new();

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

            InitializeComponent();

            _tabs[TabType.Sound] = (SoundTabBtn, SoundTab);
            _tabs[TabType.Shelf] = (ShelfTabBtn, ShelfTab);
            _tabs[TabType.Clipboard] = (ClipboardTabBtn, ClipboardTab);
            _tabs[TabType.Player] = (PlayerTabBtn, PlayerTab);
            _tabs[TabType.System] = (SystemTabBtn, SystemTab);

            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (s, e) => ClockText.Text = DateTime.Now.ToString("HH:mm");
            _clockTimer.Start();

            _osdHideTimer.Interval = TimeSpan.FromSeconds(2);
            _osdHideTimer.Tick += (s, e) => HideOSD();

            _layoutTimer.Interval = TimeSpan.FromMilliseconds(200);
            _layoutTimer.Tick += (s, e) => _layout.Poll();
            _layoutTimer.Start();

            _hoverTimer.Interval = TimeSpan.FromMilliseconds(80);
            _hoverTimer.Tick += (s, e) => CheckHover();
            _hoverTimer.Start();

            _playerPosTimer.Interval = TimeSpan.FromMilliseconds(500);
            _playerPosTimer.Tick += (s, e) => UpdatePlayerPosition();
            _playerPosTimer.Start();

            _activeWindowTimer.Interval = TimeSpan.FromSeconds(1);
            _activeWindowTimer.Tick += (s, e) => UpdateActiveWindow();
            _activeWindowTimer.Start();

            _systemMonitorTimer.Interval = TimeSpan.FromSeconds(1);
            _systemMonitorTimer.Tick += (s, e) => _systemMonitor.Poll();
            _systemMonitorTimer.Start();

            SubscribeServices();

            VolumeSlider.Value = _audio.CurrentVolume;
            VolumeValueText.Text = $"{_audio.CurrentVolume}%";
            OsdPercentText.Text = $"{_audio.CurrentVolume}%";
            OsdProgress.Value = _audio.CurrentVolume;
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            PlayerVolSlider.Value = _player.Volume * 100;
            PlayerVolText.Text = $"{(int)(_player.Volume * 100)}%";
            PlayerVolSlider.ValueChanged += PlayerVolSlider_ValueChanged;
            PlayerSeekSlider.ValueChanged += PlayerSeekSlider_ValueChanged;

            BuildDeviceList();
            BuildShelfList();
            BuildClipboardList();
            BuildPlaylist();
            UpdatePlayerTrackUi();
            UpdateActiveWindow();

            SelectTab(TabType.Sound);

            Left = (SystemParameters.WorkArea.Width - COLLAPSED_WIDTH) / 2;
            Top = SystemParameters.WorkArea.Top + 6;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            NativeMethods.AddClipboardFormatListener(_hwnd);
            var src = System.Windows.Interop.HwndSource.FromHwnd(_hwnd);
            src?.AddHook(WndProc);

            int pref = 2;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            int border = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE) { _clipboard.OnClipboardChanged(); handled = true; }
            return IntPtr.Zero;
        }

        private void SubscribeServices()
        {
            _audio.VolumeChanged += (s, vol) => Dispatcher.Invoke(() =>
            {
                if (_isUpdatingVolume) return;
                _isUpdatingVolume = true;
                try
                {
                    VolumeSlider.Value = vol;
                    VolumeValueText.Text = $"{vol}%";
                    OsdPercentText.Text = $"{vol}%";
                    OsdProgress.Value = vol;
                    ShowOSD();
                }
                finally { _isUpdatingVolume = false; }
            });
            _audio.DevicesChanged += (s, e) => Dispatcher.Invoke(BuildDeviceList);
            _clipboard.HistoryChanged += (s, e) => Dispatcher.Invoke(() => { UpdateClipboardCount(); BuildClipboardList(); });
            _layout.Changed += (s, data) => Dispatcher.Invoke(() =>
            {
                LayoutText.Text = data.layout;
                CapsText.Visibility = data.capsLock ? Visibility.Visible : Visibility.Collapsed;
            });
            _shelf.Changed += (s, e) => Dispatcher.Invoke(() => { UpdateShelfCount(); BuildShelfList(); });
            _player.TrackChanged += (s, e) => Dispatcher.Invoke(() => { BuildPlaylist(); UpdatePlayerTrackUi(); });
            _player.StateChanged += (s, e) => Dispatcher.Invoke(UpdatePlayerStateUi);
            _systemMonitor.Updated += (s, data) => Dispatcher.Invoke(() => UpdateSystemUi(data));
        }

        private void UpdateSystemUi((double CpuPercent, double RamPercent, double GpuPercent, ulong UsedRamMb, ulong TotalRamMb) data)
        {
            CpuPercentText.Text = $"{data.CpuPercent:F1}%";
            CpuProgressBar.Value = Math.Min(data.CpuPercent, 100);

            RamPercentText.Text = $"{data.RamPercent:F1}%";
            RamProgressBar.Value = Math.Min(data.RamPercent, 100);
            RamText.Text = $"{data.UsedRamMb / 1024.0:F1} / {data.TotalRamMb / 1024.0:F1} ГБ";

            GpuPercentText.Text = $"{data.GpuPercent:F1}%";
            GpuProgressBar.Value = Math.Min(data.GpuPercent, 100);

            UpdateIndicator(CpuIndicator, CpuIndicatorText, data.CpuPercent);
            UpdateIndicator(RamIndicator, RamIndicatorText, data.RamPercent);
            UpdateIndicator(GpuIndicator, GpuIndicatorText, data.GpuPercent);
        }

        private void UpdateIndicator(Border indicator, TextBlock textBlock, double percent)
        {
            textBlock.Text = $"{(int)percent}";

            if (percent > 80)
            {
                indicator.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            }
            else if (percent > 50)
            {
                indicator.Background = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            }
            else
            {
                indicator.Background = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            }
        }

        private void UpdateActiveWindow()
        {
            string title = GetActiveWindowTitle();
            ActiveWindowText.Text = string.IsNullOrWhiteSpace(title) || title == "Program Manager"
                ? "Рабочий стол"
                : (title.Length > 30 ? title.Substring(0, 27) + "..." : title);
        }

        private string GetActiveWindowTitle()
        {
            IntPtr hwnd = GetForegroundWindow();
            System.Text.StringBuilder buff = new System.Text.StringBuilder(256);
            return GetWindowText(hwnd, buff, 256) > 0 ? buff.ToString() : "";
        }

        private void SelectTab(TabType tab)
        {
            _activeTab = tab;
            foreach (var kv in _tabs)
            {
                bool active = kv.Key == tab;
                kv.Value.panel.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                kv.Value.btn.Foreground = active ? new SolidColorBrush(Color.FromRgb(226, 232, 240)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
                kv.Value.btn.Background = active ? new SolidColorBrush(Color.FromArgb(20, 34, 211, 238)) : Brushes.Transparent;
            }
            var target = _tabs[tab].panel;
            target.Opacity = 0;
            target.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void SoundTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Sound);
        private void ShelfTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Shelf);
        private void ClipboardTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Clipboard);
        private void PlayerTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.Player);
        private void SystemTab_Click(object sender, RoutedEventArgs e) => SelectTab(TabType.System);

        private void SwitchTabRelative(int dir)
        {
            var order = new[] { TabType.Sound, TabType.Shelf, TabType.Clipboard, TabType.Player, TabType.System };
            int i = Array.IndexOf(order, _activeTab) + dir;
            if (i < 0) i = order.Length - 1;
            if (i >= order.Length) i = 0;
            SelectTab(order[i]);
        }

        private void TabContent_PreviewDown(object sender, MouseButtonEventArgs e)
        {
            _swipeMoved = false;
            if (IsInteractiveOrigin(e.OriginalSource as DependencyObject)) { _swipeArmed = false; return; }
            _swipeArmed = true;
            _swipeStartX = e.GetPosition(TabContentGrid).X;
        }

        private void TabContent_PreviewMove(object sender, MouseEventArgs e)
        {
            if (!_swipeArmed || e.LeftButton != MouseButtonState.Pressed) return;
            if (Math.Abs(e.GetPosition(TabContentGrid).X - _swipeStartX) > SWIPE_THRESHOLD) _swipeMoved = true;
        }

        private void TabContent_PreviewUp(object sender, MouseButtonEventArgs e)
        {
            if (!_swipeArmed) return;
            _swipeArmed = false;
            double dx = e.GetPosition(TabContentGrid).X - _swipeStartX;
            if (_swipeMoved && Math.Abs(dx) >= SWIPE_THRESHOLD) SwitchTabRelative(dx < 0 ? +1 : -1);
        }

        private bool IsInteractiveOrigin(DependencyObject? d)
        {
            while (d != null && !ReferenceEquals(d, TabContentGrid))
            {
                if (d is Slider || d is ButtonBase) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private void CheckHover()
        {
            if (_isAnimating) return;
            NativeMethods.GetCursorPos(out var cur);
            NativeMethods.GetWindowRect(_hwnd, out var r);
            bool inside = cur.X >= r.Left && cur.X <= r.Right && cur.Y >= r.Top && cur.Y <= r.Bottom;
            if (inside && !_isExpanded) ToggleExpand();
            else if (!inside && _isExpanded) ToggleExpand();
        }

        private void ToggleExpand()
        {
            if (_isAnimating) return;
            _isAnimating = true;
            _isExpanded = !_isExpanded;
            _osdHideTimer.Stop();
            HideOSD();

            var dur = TimeSpan.FromMilliseconds(ANIM_MS);
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 };
            int tw = _isExpanded ? EXPANDED_WIDTH : COLLAPSED_WIDTH;
            int th = _isExpanded ? EXPANDED_HEIGHT : COLLAPSED_HEIGHT;

            var wA = new DoubleAnimation(tw, dur) { EasingFunction = ease };
            var hA = new DoubleAnimation(th, dur) { EasingFunction = ease };
            var lA = new DoubleAnimation((SystemParameters.WorkArea.Width - tw) / 2, dur) { EasingFunction = ease };
            hA.Completed += (s, e) => _isAnimating = false;

            BeginAnimation(WidthProperty, wA);
            BeginAnimation(HeightProperty, hA);
            BeginAnimation(LeftProperty, lA);

            if (_isExpanded)
            {
                ExpandedGrid.Visibility = Visibility.Visible;
                ExpandedGrid.Opacity = 0;
                ExpandedGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            else
            {
                ExpandedGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files) _shelf.AddFiles(files);
            e.Handled = true;
        }
        private void Player_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void Player_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files) _player.AddTracks(files);
            e.Handled = true;
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingVolume) return;
            _isUpdatingVolume = true;
            try
            {
                _audio.SetVolume((int)e.NewValue);
                VolumeValueText.Text = $"{(int)e.NewValue}%";
                OsdPercentText.Text = $"{(int)e.NewValue}%";
                OsdProgress.Value = e.NewValue;
                ShowOSD();
            }
            finally { _isUpdatingVolume = false; }
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e) => _player.TogglePlayPause();
        private void Prev_Click(object sender, RoutedEventArgs e) => _player.Prev();
        private void Next_Click(object sender, RoutedEventArgs e) => _player.Next();
        private void Shuffle_Click(object sender, RoutedEventArgs e) { _player.Shuffle = !_player.Shuffle; UpdatePlayerStateUi(); }
        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            _player.Repeat = _player.Repeat switch { RepeatMode.Off => RepeatMode.All, RepeatMode.All => RepeatMode.One, _ => RepeatMode.Off };
            UpdatePlayerStateUi();
        }
        private void PlayerVolSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { _player.Volume = (float)(e.NewValue / 100.0); PlayerVolText.Text = $"{(int)e.NewValue}%"; }
        private void PlayerSeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingSeek) return;
            var d = _player.Duration;
            if (d <= TimeSpan.Zero) return;
            _player.Seek(TimeSpan.FromSeconds(e.NewValue));
        }
        private void UpdatePlayerPosition()
        {
            var pos = _player.Position; var dur = _player.Duration;
            PlayerPosText.Text = Fmt(pos); PlayerDurText.Text = Fmt(dur);
            _isUpdatingSeek = true;
            PlayerSeekSlider.Maximum = dur.TotalSeconds > 0 ? dur.TotalSeconds : 100;
            PlayerSeekSlider.Value = Math.Min(pos.TotalSeconds, PlayerSeekSlider.Maximum);
            _isUpdatingSeek = false;
        }
        private void UpdatePlayerTrackUi()
        {
            var track = _player.CurrentTrack;
            if (track == null) { PlayerTitleText.Text = "Нет трека"; PlayerArtistText.Text = "Брось mp3/wav сюда"; PlayerCoverText.Text = "\uD83C\uDFB5"; }
            else { PlayerTitleText.Text = IOPath.GetFileNameWithoutExtension(track); PlayerArtistText.Text = "Локальный файл"; PlayerCoverText.Text = "\uD83C\uDFB5"; }
            UpdatePlayerPosition();
        }
        private void UpdatePlayerStateUi()
        {
            PlayPauseBtn.Content = _player.IsPlaying ? "\u23F8" : "\u25B6";
            ShuffleBtn.Foreground = _player.Shuffle ? new SolidColorBrush(Color.FromRgb(34, 211, 238)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            RepeatBtn.Foreground = _player.Repeat switch { RepeatMode.All => new SolidColorBrush(Color.FromRgb(34, 211, 238)), RepeatMode.One => new SolidColorBrush(Color.FromRgb(245, 158, 11)), _ => new SolidColorBrush(Color.FromRgb(148, 163, 184)) };
            RepeatBtn.Content = _player.Repeat == RepeatMode.One ? "\uD83D\uDD01" : "\uD83D\uDD02";
        }
        private static string Fmt(TimeSpan t) => t.TotalSeconds < 0 ? "0:00" : $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

        private void BuildDeviceList()
        {
            DevicesStack.Children.Clear();
            string activeId = _audio.ActiveDeviceId ?? "";

            foreach (var device in _audio.Devices)
            {
                bool isActive = device.ID == activeId;

                var card = new Border
                {
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 12),
                    Cursor = Cursors.Hand
                };

                if (isActive)
                {
                    card.Background = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                    card.BorderBrush = new SolidColorBrush(Color.FromArgb(179, 34, 211, 238));
                    card.BorderThickness = new Thickness(1.5);
                }
                else
                {
                    card.Background = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                    card.BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
                    card.BorderThickness = new Thickness(0.5);
                }

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock
                {
                    Text = DeviceName(device.FriendlyName),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = isActive ? new SolidColorBrush(Color.FromRgb(226, 232, 240)) : new SolidColorBrush(Color.FromRgb(203, 213, 225))
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"{_audio.CurrentVolume}% \u2022 {(isActive ? "default" : "inactive")}",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                if (isActive)
                {
                    var swapBtn = new Button
                    {
                        Content = "Swap",
                        Style = (Style)FindResource("SwapButton"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    swapBtn.Click += async (s, e) =>
                    {
                        var inactiveDevice = _audio.Devices.FirstOrDefault(d => d.ID != activeId);
                        if (inactiveDevice != null)
                        {
                            _audio.SwitchDevice(inactiveDevice.ID);
                            await Task.Delay(400);
                            BuildDeviceList();
                        }
                    };
                    Grid.SetColumn(swapBtn, 1);
                    grid.Children.Add(swapBtn);
                }
                else
                {
                    card.MouseLeftButtonUp += async (s, e) =>
                    {
                        _audio.SwitchDevice(device.ID);
                        await Task.Delay(400);
                        BuildDeviceList();
                    };
                }

                card.Child = grid;
                DevicesStack.Children.Add(card);
            }
        }

        private static string DeviceName(string name)
        {
            int p = name.IndexOf('(');
            return p > 0 ? name.Substring(0, p).Trim() : name;
        }

        private void BuildShelfList()
        {
            ShelfStack.Children.Clear();
            if (_shelf.Files.Count == 0) { ShelfStack.Children.Add(EmptyHint("Перетащи файлы сюда")); return; }
            foreach (string file in _shelf.Files)
            {
                string fileName = IOPath.GetFileName(file); string ext = IOPath.GetExtension(file);
                var grid = RowShell();
                grid.Children.Add(CenterIcon(ShelfService.GetFileIcon(ext)));
                grid.Children.Add(RowText(fileName, 1));
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(actions, 2);
                actions.Children.Add(IconBtn("\u25B6", () => ShelfService.Open(file)));
                actions.Children.Add(IconBtn("\uD83D\uDCC2", () => ShelfService.OpenFolder(file)));
                actions.Children.Add(IconBtn("\uD83D\uDCCB", () => ShelfService.CopyToClipboard(file)));
                actions.Children.Add(IconBtn("\u2715", () => _shelf.Remove(file), Color.FromRgb(248, 113, 113)));
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
                var grid = RowShell(44);
                if (item.Type == ClipboardItemType.Color && item.Color.HasValue)
                    grid.Children.Add(new Border { Background = new SolidColorBrush(item.Color.Value), Width = 22, Height = 22, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                else if (item.Type == ClipboardItemType.Image && item.Image != null)
                    grid.Children.Add(new Image { Source = item.Image, Width = 28, Height = 28, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                else grid.Children.Add(CenterIcon(item.Type == ClipboardItemType.Text ? "\uD83D\uDCDD" : "\uD83D\uDCCB"));
                grid.Children.Add(RowText(item.DisplayText, 1));
                var paste = IconBtn("\uD83D\uDCCB", () => RestoreClipboardItem(item));
                Grid.SetColumn(paste, 2); paste.VerticalAlignment = VerticalAlignment.Center; paste.Margin = new Thickness(0, 0, 4, 0);
                grid.Children.Add(paste);
                grid.MouseLeftButtonUp += (s, e) => { if (!_swipeMoved) RestoreClipboardItem(item); };
                ClipboardStack.Children.Add(grid);
            }
        }

        private void BuildPlaylist()
        {
            PlaylistStack.Children.Clear();
            if (_player.Playlist.Count == 0) { PlaylistStack.Children.Add(EmptyHint("Брось mp3/wav сюда")); return; }
            for (int i = 0; i < _player.Playlist.Count; i++)
            {
                int idx = i; bool cur = i == _player.CurrentIndex;
                var grid = RowShell(36);
                grid.Background = cur ? new SolidColorBrush(Color.FromArgb(30, 34, 211, 238)) : new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
                var num = new TextBlock { Text = cur && _player.IsPlaying ? "\u25B6" : (idx + 1).ToString(), FontSize = 11, Foreground = cur ? new SolidColorBrush(Color.FromRgb(34, 211, 238)) : new SolidColorBrush(Color.FromRgb(148, 163, 184)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(num, 0); grid.Children.Add(num);
                var title = new TextBlock { Text = IOPath.GetFileNameWithoutExtension(_player.Playlist[idx]), FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(title, 1); grid.Children.Add(title);
                var del = IconBtn("\u2715", () => _player.RemoveAt(idx), Color.FromRgb(248, 113, 113));
                Grid.SetColumn(del, 2); del.VerticalAlignment = VerticalAlignment.Center; del.Margin = new Thickness(0, 0, 4, 0);
                grid.Children.Add(del);
                grid.MouseLeftButtonUp += (s, e) => { if (!_swipeMoved) _player.LoadAndPlay(idx); };
                PlaylistStack.Children.Add(grid);
            }
        }

        private void RestoreClipboardItem(ClipboardItem item)
        {
            _clipboard.RestoreItem(item);
            string o = ClipboardCountText.Text; var oc = ClipboardCountText.Foreground;
            ClipboardCountText.Text = "\u2713 Готово"; ClipboardCountText.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (s, e) => { ClipboardCountText.Text = o; ClipboardCountText.Foreground = oc; t.Stop(); };
            t.Start();
        }
        private void ClearClipboard_Click(object sender, RoutedEventArgs e) => _clipboard.Clear();

        private void ShowOSD()
        {
            _osdHideTimer.Stop();

            if (OsdGrid.Visibility == Visibility.Visible)
            {
                _osdHideTimer.Start();
                return;
            }

            HeaderGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            OsdGrid.Opacity = 0;
            OsdGrid.RenderTransform = new ScaleTransform(0.95, 0.95);
            OsdGrid.RenderTransformOrigin = new Point(0.5, 0.5);
            OsdGrid.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            OsdGrid.BeginAnimation(OpacityProperty, fadeIn);

            var scaleAnim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.2 } };
            var scaleTransform = new ScaleTransform(0.95, 0.95);
            OsdGrid.RenderTransform = scaleTransform;
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            var hideHeader = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            hideHeader.Completed += (s, e) => HeaderGrid.Visibility = Visibility.Collapsed;
            HeaderGrid.BeginAnimation(OpacityProperty, hideHeader);

            _osdHideTimer.Start();
        }

        private void HideOSD()
        {
            if (OsdGrid.Visibility != Visibility.Visible) return;

            _osdHideTimer.Stop();

            var fadeOutOsd = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOutOsd.Completed += (s, e) => OsdGrid.Visibility = Visibility.Collapsed;
            OsdGrid.BeginAnimation(OpacityProperty, fadeOutOsd);

            HeaderGrid.Visibility = Visibility.Visible;
            HeaderGrid.Opacity = 0;
            var fadeInHeader = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            HeaderGrid.BeginAnimation(OpacityProperty, fadeInHeader);
        }

        private void UpdateShelfCount() { ShelfCountText.Text = $"\uD83D\uDCCE {_shelf.Files.Count}"; ShelfCountText.Visibility = _shelf.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
        private void UpdateClipboardCount() { ClipboardCountText.Text = $"\uD83D\uDCCB {_clipboard.History.Count}"; ClipboardCountText.Visibility = _clipboard.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }

        private Grid RowShell(double height = 40)
        {
            var g = new Grid { Height = height, Margin = new Thickness(0, 0, 0, 4), Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return g;
        }
        private TextBlock RowText(string text, int col) { var t = new TextBlock { Text = text, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(226, 232, 240)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(t, col); return t; }
        private TextBlock EmptyHint(string text) => new TextBlock { Text = text, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
        private TextBlock CenterIcon(string glyph) => new TextBlock { Text = glyph, FontSize = 15, FontFamily = new FontFamily("Segoe UI Emoji"), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        private Button IconBtn(string glyph, Action onClick, Color? fg = null)
        {
            var b = new Button { Style = (Style)FindResource("IconButton"), Content = glyph, FontFamily = new FontFamily("Segoe UI Emoji"), FontSize = 12 };
            if (fg.HasValue) b.Foreground = new SolidColorBrush(fg.Value);
            b.Click += (s, e) => onClick();
            return b;
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer.Stop(); _osdHideTimer.Stop(); _layoutTimer.Stop(); _hoverTimer.Stop(); _playerPosTimer.Stop(); _activeWindowTimer.Stop(); _systemMonitorTimer.Stop();
            if (_hwnd != IntPtr.Zero) NativeMethods.RemoveClipboardFormatListener(_hwnd);
            _audio.Dispose(); _player.Dispose(); _systemMonitor.Dispose();
            base.OnClosed(e);
        }
    }
}