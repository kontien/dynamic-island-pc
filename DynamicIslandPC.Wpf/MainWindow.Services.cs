using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Infrastructure;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private void SubscribeServices()
        {
            _audio.VolumeChanged += OnAudioVolumeChanged;
            _audio.DevicesChanged += OnAudioDevicesChanged;
            _clipboard.HistoryChanged += OnClipboardHistoryChanged;
            _layout.Changed += OnLayoutChanged;
            _shelf.Changed += OnShelfChanged;
            _systemMonitor.Updated += OnSystemMonitorUpdated;
            _mediaSession.TrackChanged += OnMediaTrackChanged;
            _mediaSession.PlaybackStateChanged += OnMediaPlaybackStateChanged;
            _mediaSession.TimelineChanged += OnMediaTimelineChanged;
            _vpnService.StatusChanged += OnVpnStatusChanged;
            _vpnService.Error += OnVpnError;
        }

        private void UnsubscribeServices()
        {
            _audio.VolumeChanged -= OnAudioVolumeChanged;
            _audio.DevicesChanged -= OnAudioDevicesChanged;
            _clipboard.HistoryChanged -= OnClipboardHistoryChanged;
            _layout.Changed -= OnLayoutChanged;
            _shelf.Changed -= OnShelfChanged;
            _systemMonitor.Updated -= OnSystemMonitorUpdated;
            _mediaSession.TrackChanged -= OnMediaTrackChanged;
            _mediaSession.PlaybackStateChanged -= OnMediaPlaybackStateChanged;
            _mediaSession.TimelineChanged -= OnMediaTimelineChanged;
            _vpnService.StatusChanged -= OnVpnStatusChanged;
            _vpnService.Error -= OnVpnError;
        }

        private void DispatchToUi(Action action)
        {
            if (_isClosing || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            if (Dispatcher.CheckAccess()) { action(); return; }
            _ = Dispatcher.InvokeAsync(action, DispatcherPriority.Background);
        }

        private void OnAudioVolumeChanged(object? sender, int volume) => DispatchToUi(() =>
        {
            if (_isUpdatingVolume) return;
            _isUpdatingVolume = true;
            try { VolumeSlider.Value = volume; VolBigText.Text = $"{volume}%"; UpdateOsdUi("Система", volume); ShowOSD(); }
            finally { _isUpdatingVolume = false; }
        });

        private void OnAudioDevicesChanged(object? sender, EventArgs e) => DispatchToUi(BuildDeviceStack);

        private void OnClipboardHistoryChanged(object? sender, EventArgs e) => DispatchToUi(() =>
        {
            UpdateClipboardCount();
            BuildClipboardList();
            if (_isTileFocused && ClipboardPanel.Visibility == Visibility.Visible) BuildClipboardLargeList();
        });

        private void OnLayoutChanged(object? sender, (string layout, bool capsLock) data) => DispatchToUi(() =>
        {
            LayoutText.Text = $"• {data.layout}";
            CapsText.Visibility = data.capsLock ? Visibility.Visible : Visibility.Collapsed;
        });

        private void OnShelfChanged(object? sender, EventArgs e) => DispatchToUi(() =>
        {
            UpdateShelfCount();
            BuildShelfList();
            if (_isTileFocused && ShelfPanel.Visibility == Visibility.Visible) BuildShelfLargeList();
        });

        private void OnSystemMonitorUpdated(object? sender, (double CpuPercent, double RamPercent, double GpuPercent, ulong UsedRamMb, ulong TotalRamMb) data) =>
            DispatchToUi(() => UpdateSystemUi(data));

        private void OnMediaTrackChanged(string title, string artist, BitmapImage? cover) => DispatchToUi(() =>
        {
            PlayerTitleText.Text = string.IsNullOrEmpty(title) ? "Нет трека" : title;
            PlayerArtistText.Text = string.IsNullOrEmpty(artist) ? "YouTube / Spotify" : artist;
            if (cover != null)
            {
                PlayerCoverBorder.Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
                PlayerCoverText.Visibility = Visibility.Collapsed;
            }
            else
            {
                PlayerCoverBorder.Background = new SolidColorBrush(Color.FromRgb(51, 60, 80));
                PlayerCoverText.Visibility = Visibility.Visible;
            }
        });

        private void OnMediaPlaybackStateChanged(bool isPlaying) => DispatchToUi(() =>
            PlayPauseBtn.Content = isPlaying ? "\uE769" : "\uE768");

        private void OnMediaTimelineChanged(TimeSpan position, TimeSpan duration) => DispatchToUi(() =>
        {
            if (duration.TotalSeconds <= 0)
            {
                TimelineGrid.Visibility = Visibility.Collapsed;
                TrackProgress.Value = 0;
            }
            else
            {
                TimelineGrid.Visibility = Visibility.Visible;
                TrackProgress.Maximum = duration.TotalSeconds;
                TrackProgress.Value = position.TotalSeconds;
                TimelineCurrentText.Text = FormatTime(position);
                TimelineTotalText.Text = FormatTime(duration);
            }
        });

        private void OnVpnStatusChanged(bool isConnected) => DispatchToUi(() => UpdateVpnUi(isConnected));
        private void OnVpnError(string message) => DispatchToUi(() => VpnDetailsText.Text = "⚠ " + message);

        private void UpdateOsdUi(string appName, int volume)
        {
            SmallOsdIcon.Text = "🔊"; LargeOsdIcon.Text = "🔊";
            SmallOsdAppName.Text = appName; LargeOsdAppName.Text = appName;
            SmallOsdPercentText.Text = $"{volume}%"; LargeOsdPercentText.Text = $"{volume}%";
            SmallOsdProgress.Value = volume; LargeOsdProgress.Value = volume;
        }

        private void ShowOSD()
        {
            if (_dnd || _isExpanded || _isFullScreen) return;
            _osdHideTimer.Stop(); _osdHideTimer.Start(); _osdShown = true;
            SmallOsd.Visibility = Visibility.Visible; LargeOsd.Visibility = Visibility.Collapsed; OsdGrid.Visibility = Visibility.Visible;
            OsdGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var hideHeader = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            hideHeader.Completed += (s, e) => { if (_osdShown) TopPanel.Visibility = Visibility.Collapsed; };
            TopPanel.BeginAnimation(OpacityProperty, hideHeader);
        }

        private void HideOSD()
        {
            _osdHideTimer.Stop();
            if (!_osdShown) return;
            _osdShown = false;
            var fadeOutOsd = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fadeOutOsd.Completed += (s, e) => { if (!_osdShown) OsdGrid.Visibility = Visibility.Collapsed; };
            OsdGrid.BeginAnimation(OpacityProperty, fadeOutOsd);
            TopPanel.Visibility = Visibility.Visible;
            TopPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        private void UpdateClock() { var now = DateTime.Now; ClockText.Text = now.ToString("HH:mm"); DateText.Text = now.ToString("dddd, d MMMM", new CultureInfo("ru-RU")); }
        private void UpdateShelfCount() { ShelfCountText.Text = $"📎 {_shelf.Files.Count}"; ShelfCountText.Visibility = _shelf.Files.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
        private void UpdateClipboardCount() { ClipboardCountText.Text = $"📋 {_clipboard.History.Count}"; ClipboardCountText.Visibility = _clipboard.History.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }

        // НОВОЕ: Проверяем, есть ли окно под островом через пересечение прямоугольников
        private bool IsWindowUnderIsland()
        {
            if (_hwnd == IntPtr.Zero) return false;

            // Получаем координаты острова на экране
            var islandPos = IslandBorder.PointToScreen(new Point(0, 0));
            var islandRect = new Rect(islandPos.X, islandPos.Y, IslandBorder.ActualWidth, IslandBorder.ActualHeight);

            // Получаем активное окно (не наше)
            IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero || foregroundHwnd == _hwnd) return false;

            // Получаем его прямоугольник
            if (!NativeMethods.GetWindowRect(foregroundHwnd, out NativeMethods.RECT rect)) return false;

            var windowRect = new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);

            // Проверяем пересечение
            return islandRect.IntersectsWith(windowRect);
        }

        private bool IsFullScreenApp()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == _hwnd) return false;
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect)) return false;
            return rect.Left <= 0 && rect.Top <= 0 &&
                   rect.Right >= (int)SystemParameters.PrimaryScreenWidth &&
                   rect.Bottom >= (int)SystemParameters.PrimaryScreenHeight;
        }

        private void CheckHover()
        {
            if (_isAnimating || _isDraggingOut || _isVpnToggling || _pinned) return;
            if (IslandBorder.ContextMenu?.IsOpen == true) return;
            if (_isExpanded && Mouse.Captured != null) return;

            bool wasFullScreen = _isFullScreen;
            _isFullScreen = IsFullScreenApp();

            NativeMethods.GetCursorPos(out var cur);
            var pos = IslandBorder.PointToScreen(new Point(0, 0));
            int buffer = _isFullScreen && !_isExpanded ? 10 : 0;
            bool inside = cur.X >= pos.X - buffer && cur.X <= pos.X + IslandBorder.ActualWidth + buffer &&
                          cur.Y >= pos.Y - buffer && cur.Y <= pos.Y + IslandBorder.ActualHeight + buffer;

            if (inside && !_isExpanded) ToggleExpand();
            else if (!inside && _isExpanded) ToggleExpand();
            else if (wasFullScreen != _isFullScreen && !_isExpanded) ToggleExpand();
        }

        private void ToggleExpand()
        {
            if (_isAnimating) return;
            _isAnimating = true;
            _isExpanded = !_isExpanded;
            _osdShown = false; _osdHideTimer.Stop();
            OsdGrid.Visibility = Visibility.Collapsed;
            TopPanel.BeginAnimation(OpacityProperty, null); TopPanel.Opacity = 1;
            var dur = TimeSpan.FromMilliseconds(350);
            var spline = new KeySpline(0.16, 1, 0.3, 1);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            int tw, th;
            if (_isExpanded) { tw = EXPANDED_WIDTH; th = EXPANDED_HEIGHT; }
            else
            {
                // НОВОЕ: проверяем, есть ли окно под островом
                bool hasWindowUnder = IsWindowUnderIsland();

                if (hasWindowUnder)
                {
                    tw = DOT_WIDTH;
                    th = DOT_HEIGHT;
                }
                else
                {
                    tw = COLLAPSED_WIDTH;
                    th = COLLAPSED_HEIGHT;
                }
            }
            var wA = new DoubleAnimationUsingKeyFrames { Duration = dur }; wA.KeyFrames.Add(new SplineDoubleKeyFrame(tw, KeyTime.FromPercent(1.0), spline));
            var hA = new DoubleAnimationUsingKeyFrames { Duration = dur }; hA.KeyFrames.Add(new SplineDoubleKeyFrame(th, KeyTime.FromPercent(1.0), spline));
            hA.Completed += (s, e) => _isAnimating = false;
            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, null);
            IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, null);
            IslandBorder.BeginAnimation(FrameworkElement.WidthProperty, wA);
            IslandBorder.BeginAnimation(FrameworkElement.HeightProperty, hA);
            if (_isExpanded)
            {
                IslandBorder.CornerRadius = new CornerRadius(24);
                DotFace.Visibility = Visibility.Collapsed; TopPanel.Visibility = Visibility.Visible;
                TopPanel.BeginAnimation(HeightProperty, null); ClockViewbox.BeginAnimation(HeightProperty, null);
                foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox }) { vb.BeginAnimation(WidthProperty, null); vb.BeginAnimation(HeightProperty, null); }
                TopPanel.BeginAnimation(HeightProperty, new DoubleAnimation(HEAD_EXPANDED, dur) { EasingFunction = ease });
                ClockViewbox.BeginAnimation(HeightProperty, new DoubleAnimation(CLOCK_EXPANDED, dur) { EasingFunction = ease });
                foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox }) { vb.BeginAnimation(WidthProperty, new DoubleAnimation(RING_EXPANDED, dur) { EasingFunction = ease }); vb.BeginAnimation(HeightProperty, new DoubleAnimation(RING_EXPANDED, dur) { EasingFunction = ease }); }
                DatePanel.Visibility = Visibility.Visible;
                DatePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                CountersPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));
                BoardGrid.BeginAnimation(OpacityProperty, null); BoardGrid.Visibility = Visibility.Visible; BoardGrid.Opacity = 0;
                BoardGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = ease });
                BuildDeviceStack();
            }
            else
            {
                HideTileFocus();
                var hideContent = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
                hideContent.Completed += (s, e) => { if (!_isExpanded) BoardGrid.Visibility = Visibility.Collapsed; };
                BoardGrid.BeginAnimation(OpacityProperty, hideContent);

                // НОВОЕ: используем hasWindowUnder для определения точки vs времени
                bool hasWindowUnder = IsWindowUnderIsland();

                if (hasWindowUnder)
                {
                    IslandBorder.CornerRadius = new CornerRadius(10);
                    TopPanel.Visibility = Visibility.Collapsed;
                    DotFace.Visibility = Visibility.Visible;
                }
                else
                {
                    IslandBorder.CornerRadius = new CornerRadius(22);
                    TopPanel.Visibility = Visibility.Visible;
                    DotFace.Visibility = Visibility.Collapsed;
                    TopPanel.BeginAnimation(HeightProperty, null); ClockViewbox.BeginAnimation(HeightProperty, null);
                    foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox }) { vb.BeginAnimation(WidthProperty, null); vb.BeginAnimation(HeightProperty, null); }
                    TopPanel.BeginAnimation(HeightProperty, new DoubleAnimation(HEAD_COLLAPSED, dur) { EasingFunction = ease });
                    ClockViewbox.BeginAnimation(HeightProperty, new DoubleAnimation(CLOCK_COLLAPSED, dur) { EasingFunction = ease });
                    foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox }) { vb.BeginAnimation(WidthProperty, new DoubleAnimation(RING_COLLAPSED, dur) { EasingFunction = ease }); vb.BeginAnimation(HeightProperty, new DoubleAnimation(RING_COLLAPSED, dur) { EasingFunction = ease }); }
                    CountersPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                    var hideDate = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
                    hideDate.Completed += (s, e) => { if (!_isExpanded) DatePanel.Visibility = Visibility.Collapsed; };
                    DatePanel.BeginAnimation(OpacityProperty, hideDate);
                }
            }
        }
    }
}