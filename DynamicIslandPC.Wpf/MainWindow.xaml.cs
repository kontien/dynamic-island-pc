using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Models;
using DynamicIslandPC.Wpf.Services;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow : Window
    {
        private const int COLLAPSED_WIDTH = 280;
        private const int COLLAPSED_HEIGHT = 44;
        private const int EXPANDED_WIDTH = 420;
        private const int EXPANDED_HEIGHT = 520;
        private const int ANIM_MS = 380;

        private readonly AudioService _audio;
        private readonly ClipboardService _clipboard;
        private readonly LayoutService _layout;
        private readonly ShelfService _shelf;

        private IntPtr _hwnd;
        private bool _isExpanded;
        private bool _isAnimating;

        private readonly System.Windows.Threading.DispatcherTimer _clockTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _osdHideTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _layoutTimer = new();
        private readonly System.Windows.Threading.DispatcherTimer _hoverTimer = new();

        public MainWindow()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string svvPath = Path.Combine(appDir, "tools", "SoundVolumeView.exe");
            if (!File.Exists(svvPath))
            {
                string projectDir = Path.GetFullPath(Path.Combine(appDir, "..", "..", ".."));
                svvPath = Path.Combine(projectDir, "tools", "SoundVolumeView.exe");
            }

            _audio = new AudioService(svvPath);
            _clipboard = new ClipboardService();
            _layout = new LayoutService();
            _shelf = new ShelfService();

            InitializeComponent();

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

            SubscribeServices();

            VolumeSlider.Value = _audio.CurrentVolume;
            VolumeValueText.Text = $"{_audio.CurrentVolume}%";
            VolumePercentText.Text = $"{_audio.CurrentVolume}%";
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            BuildDeviceList();
            BuildShelfList();
            BuildClipboardList();

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
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                _clipboard.OnClipboardChanged();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void SubscribeServices()
        {
            _audio.VolumeChanged += (s, vol) => Dispatcher.Invoke(() =>
            {
                if (VolumeSlider.Value != vol) VolumeSlider.Value = vol;
                VolumeValueText.Text = $"{vol}%";
                VolumePercentText.Text = $"{vol}%";
                VolumeIconText.Text = vol == 0 ? "🔇" : vol < 50 ? "🔉" : "🔊";
                if (!_isExpanded) ShowOSD();
            });

            _audio.DevicesChanged += (s, e) => Dispatcher.Invoke(BuildDeviceList);

            _clipboard.HistoryChanged += (s, e) => Dispatcher.Invoke(() =>
            {
                UpdateClipboardCount();
                BuildClipboardList();
            });

            _layout.Changed += (s, data) => Dispatcher.Invoke(() =>
            {
                LayoutText.Text = data.layout;
                CapsText.Visibility = data.capsLock && !_isExpanded ? Visibility.Visible : Visibility.Collapsed;
            });

            _shelf.Changed += (s, e) => Dispatcher.Invoke(() =>
            {
                UpdateShelfCount();
                BuildShelfList();
            });
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _audio.SetVolume((int)e.NewValue);
            VolumeValueText.Text = $"{(int)e.NewValue}%";
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

            OsdGrid.Visibility = Visibility.Collapsed;
            _osdHideTimer.Stop();

            var dur = TimeSpan.FromMilliseconds(ANIM_MS);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

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
                HeaderGrid.Visibility = Visibility.Collapsed;
                ExpandedGrid.Visibility = Visibility.Visible;
            }
            else
            {
                ExpandedGrid.Visibility = Visibility.Collapsed;
                HeaderGrid.Visibility = Visibility.Visible;
                CapsText.Visibility = _layout.IsCapsLock ? Visibility.Visible : Visibility.Collapsed;
                ShelfCountText.Visibility = _shelf.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ClipboardCountText.Visibility = _clipboard.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                    _shelf.AddFiles(files);
            }
            e.Handled = true;
        }

        private void ClearClipboard_Click(object sender, RoutedEventArgs e) => _clipboard.Clear();

        private void ShowOSD()
        {
            HeaderGrid.Visibility = Visibility.Collapsed;
            OsdGrid.Visibility = Visibility.Visible;
            _osdHideTimer.Stop();
            _osdHideTimer.Start();
        }

        private void HideOSD()
        {
            OsdGrid.Visibility = Visibility.Collapsed;
            if (!_isExpanded)
            {
                HeaderGrid.Visibility = Visibility.Visible;
                CapsText.Visibility = _layout.IsCapsLock ? Visibility.Visible : Visibility.Collapsed;
                ShelfCountText.Visibility = _shelf.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ClipboardCountText.Visibility = _clipboard.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            _osdHideTimer.Stop();
        }

        private void UpdateShelfCount()
        {
            ShelfCountText.Text = $"📎 {_shelf.Files.Count}";
            ShelfCountText.Visibility = _shelf.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateClipboardCount()
        {
            ClipboardCountText.Text = $"📋 {_clipboard.History.Count}";
            ClipboardCountText.Visibility = _clipboard.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BuildDeviceList()
        {
            DevicesStack.Children.Clear();
            string activeId = _audio.ActiveDeviceId ?? "";

            foreach (var device in _audio.Devices)
            {
                bool isActive = device.ID == activeId;
                var btn = new Button
                {
                    Style = (Style)FindResource("DeviceButton"),
                    Content = BuildDeviceContent(isActive, device.FriendlyName),
                    Foreground = isActive ? new SolidColorBrush(Color.FromRgb(140, 120, 255)) : new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                    Background = isActive ? new SolidColorBrush(Color.FromArgb(35, 140, 120, 255)) : new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)),
                    FontSize = 11,
                    FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                    Tag = device.ID
                };
                string id = device.ID;
                btn.Click += async (s, e) =>
                {
                    _audio.SwitchDevice(id);
                    await Task.Delay(400);
                    BuildDeviceList();
                };
                DevicesStack.Children.Add(btn);
            }
        }

        private static string BuildDeviceContent(bool active, string name)
        {
            string shortName = name;
            int paren = shortName.IndexOf('(');
            if (paren > 0) shortName = shortName.Substring(0, paren).Trim();
            return $"{(active ? "●  " : "○  ")}{shortName}";
        }

        private void BuildShelfList()
        {
            ShelfStack.Children.Clear();

            if (_shelf.Files.Count == 0)
            {
                ShelfStack.Children.Add(EmptyHint("Перетащи файлы сюда"));
                return;
            }

            foreach (string file in _shelf.Files)
            {
                string fileName = Path.GetFileName(file);
                string ext = Path.GetExtension(file);

                var grid = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 4), Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                grid.Children.Add(CenterIcon(ShelfService.GetFileIcon(ext)));

                var name = new TextBlock
                {
                    Text = fileName,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(name, 1);
                grid.Children.Add(name);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(actions, 2);

                actions.Children.Add(IconBtn("▶", () => ShelfService.Open(file)));
                actions.Children.Add(IconBtn("📂", () => ShelfService.OpenFolder(file)));
                actions.Children.Add(IconBtn("📋", () => ShelfService.CopyToClipboard(file)));
                actions.Children.Add(IconBtn("✕", () => _shelf.Remove(file), Color.FromRgb(247, 110, 110)));

                grid.Children.Add(actions);
                ShelfStack.Children.Add(grid);
            }
        }

        private void BuildClipboardList()
        {
            ClipboardStack.Children.Clear();

            if (_clipboard.History.Count == 0)
            {
                ClipboardStack.Children.Add(EmptyHint("Скопируй что-нибудь"));
                return;
            }

            foreach (var item in _clipboard.History)
            {
                var grid = new Grid { MinHeight = 44, Margin = new Thickness(0, 0, 0, 4), Background = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255)) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                if (item.Type == ClipboardItemType.Color && item.Color.HasValue)
                {
                    grid.Children.Add(new Border
                    {
                        Background = new SolidColorBrush(item.Color.Value),
                        Width = 22,
                        Height = 22,
                        CornerRadius = new CornerRadius(5),
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
                else if (item.Type == ClipboardItemType.Image && item.Image != null)
                {
                    grid.Children.Add(new Image
                    {
                        Source = item.Image,
                        Width = 28,
                        Height = 28,
                        Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                }
                else
                {
                    string icon = item.Type == ClipboardItemType.Text ? "📝" : "📋";
                    grid.Children.Add(CenterIcon(icon));
                }

                var text = new TextBlock
                {
                    Text = item.DisplayText,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(235, 235, 235)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 4, 6, 4),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                };
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);

                var paste = IconBtn("📋", () => RestoreClipboardItem(item));
                Grid.SetColumn(paste, 2);
                paste.VerticalAlignment = VerticalAlignment.Center;
                paste.Margin = new Thickness(0, 0, 4, 0);
                grid.Children.Add(paste);

                grid.MouseLeftButtonDown += (s, e) => RestoreClipboardItem(item);
                ClipboardStack.Children.Add(grid);
            }
        }

        private void RestoreClipboardItem(ClipboardItem item)
        {
            _clipboard.RestoreItem(item);

            string o = ClipboardCountText.Text;
            var oc = ClipboardCountText.Foreground;
            ClipboardCountText.Text = "✓ Готово";
            ClipboardCountText.Foreground = new SolidColorBrush(Color.FromRgb(61, 220, 132));

            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            t.Tick += (s, e) => { ClipboardCountText.Text = o; ClipboardCountText.Foreground = oc; t.Stop(); };
            t.Start();
        }

        private TextBlock EmptyHint(string text) => new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 12)
        };

        private TextBlock CenterIcon(string glyph) => new TextBlock
        {
            Text = glyph,
            FontSize = 15,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        private Button IconBtn(string glyph, Action onClick, Color? fg = null)
        {
            var b = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = glyph,
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 12
            };
            if (fg.HasValue) b.Foreground = new SolidColorBrush(fg.Value);
            b.Click += (s, e) => onClick();
            return b;
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer.Stop();
            _osdHideTimer.Stop();
            _layoutTimer.Stop();
            _hoverTimer.Stop();
            if (_hwnd != IntPtr.Zero) NativeMethods.RemoveClipboardFormatListener(_hwnd);
            _audio.Dispose();
            base.OnClosed(e);
        }
    }
}