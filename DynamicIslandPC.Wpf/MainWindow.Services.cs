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
        private bool _hasWindowUnder;

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
            _notifService.Added += OnNotificationAdded;
            _notifService.Changed += OnNotificationsChanged;
            _notifService.AccessChanged += OnNotificationAccessChanged;
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
            _notifService.Added -= OnNotificationAdded;
            _notifService.Changed -= OnNotificationsChanged;
            _notifService.AccessChanged -= OnNotificationAccessChanged;
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

        private void OnAudioDevicesChanged(object? sender, EventArgs e) => DispatchToUi(() =>
        {
            BuildDeviceStack();
            if (_isTileFocused && AudioPanel.Visibility == Visibility.Visible) BuildDeviceLargeStack();
        });

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
            UpdateMediaBlurredCover(cover);
            AddTrackToHistory(title, artist);
            if (_isTileFocused && MediaPanel.Visibility == Visibility.Visible) UpdateMediaPanel();
        });

        private void OnMediaPlaybackStateChanged(bool isPlaying) => DispatchToUi(() =>
        {
            string glyph = isPlaying ? "\uE769" : "\uE768";
            PlayPauseBtn.Content = glyph;
            if (MediaPlayPauseBtn != null) MediaPlayPauseBtn.Content = glyph;
        });

        private void OnMediaTimelineChanged(TimeSpan position, TimeSpan duration) => DispatchToUi(() =>
        {
            _mediaPosition = position;
            _mediaDuration = duration;

            if (duration.TotalSeconds <= 0)
            {
                TimelineGrid.Visibility = Visibility.Collapsed;
                TrackProgress.Value = 0;
                if (MediaProgress != null)
                {
                    MediaProgress.Value = 0;
                    MediaCurrentTime.Text = "0:00";
                    MediaTotalTime.Text = "0:00";
                }
                return;
            }

            TimelineGrid.Visibility = Visibility.Visible;
            TrackProgress.Maximum = duration.TotalSeconds;
            TrackProgress.Value = Math.Min(position.TotalSeconds, duration.TotalSeconds);
            TimelineCurrentText.Text = FormatTime(position);
            TimelineTotalText.Text = FormatTime(duration);

            if (MediaProgress != null)
            {
                MediaProgress.Maximum = duration.TotalSeconds;
                MediaProgress.Value = Math.Min(position.TotalSeconds, duration.TotalSeconds);
                MediaCurrentTime.Text = FormatTime(position);
                MediaTotalTime.Text = FormatTime(duration);
            }
        });

        private void OnVpnStatusChanged(bool isConnected) => DispatchToUi(() => UpdateVpnUi(isConnected));
        private void OnVpnError(string message) => DispatchToUi(() => VpnDetailsText.Text = "⚠ " + message);

        private void UpdateOsdUi(string appName, int volume)
        {
            SmallOsdIcon.Text = "🔊"; LargeOsdIcon.Text = "🔊";
            SmallOsdAppName.Text = appName; LargeOsdAppName.Text = appName;
            SmallOsdPercentText.Text = $"{volume}%"; LargeOsdPercentText.Text = $"{volume}%";
            SmallOsdProgress.Visibility = Visibility.Visible;
            LargeOsdProgress.Visibility = Visibility.Visible;
            SmallOsdProgress.Value = volume; LargeOsdProgress.Value = volume;
        }

        private void ShowOSD()
        {
            if (_dnd || _isExpanded || _hasWindowUnder) return;
            HideNotifPeek();
            _osdHideTimer.Interval = TimeSpan.FromSeconds(2);
            _osdHideTimer.Stop(); _osdHideTimer.Start(); _osdShown = true;
            SmallOsd.Visibility = Visibility.Visible; LargeOsd.Visibility = Visibility.Collapsed;
            OsdGrid.Visibility = Visibility.Visible;
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
            if (_isExpanded || !_hasWindowUnder)
            {
                TopPanel.Visibility = Visibility.Visible;
                TopPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            ClockText.Text = now.ToString("HH:mm");
            DateText.Text = now.ToString("dddd, d MMMM", new CultureInfo("ru-RU"));
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

        // ==================== ОКНО ПОД ОСТРОВОМ ====================
        private bool IsFullScreenApp()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == _hwnd) return false;
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect)) return false;
            return rect.Left <= 0 && rect.Top <= 0 &&
                   rect.Right >= (int)SystemParameters.PrimaryScreenWidth &&
                   rect.Bottom >= (int)SystemParameters.PrimaryScreenHeight;
        }

        private bool IsWindowUnderIsland()
        {
            try
            {
                if (_hwnd == IntPtr.Zero || IslandBorder == null) return false;
                var p = IslandBorder.PointToScreen(new Point(0, 0));
                double w = _isExpanded ? IslandBorder.ActualWidth : COLLAPSED_WIDTH;
                double h = _isExpanded ? IslandBorder.ActualHeight : COLLAPSED_HEIGHT;
                if (w <= 0 || h <= 0) return false;
                var island = new Rect(p.X, p.Y, w, h);

                IntPtr fg = NativeMethods.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == _hwnd) return false;
                if (!NativeMethods.GetWindowRect(fg, out NativeMethods.RECT r)) return false;

                var win = new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
                if (win.Width < 40 || win.Height < 40) return false;
                return island.IntersectsWith(win);
            }
            catch { return false; }
        }

        private void CheckHover()
        {
            if (_isAnimating || _isDraggingOut || _isVpnToggling) return;
            if (IslandBorder.ContextMenu?.IsOpen == true) return;
            if (_isExpanded && Mouse.Captured != null) return;

            _isFullScreen = IsFullScreenApp();
            bool under = IsWindowUnderIsland();
            bool kindChanged = under != _hasWindowUnder;
            _hasWindowUnder = under;

            if (_pinned) return;

            NativeMethods.GetCursorPos(out var cur);
            var pos = IslandBorder.PointToScreen(new Point(0, 0));
            int buffer = _hasWindowUnder && !_isExpanded ? 12 : 0;
            bool inside = cur.X >= pos.X - buffer && cur.X <= pos.X + IslandBorder.ActualWidth + buffer &&
                          cur.Y >= pos.Y - buffer && cur.Y <= pos.Y + IslandBorder.ActualHeight + buffer;

            if (inside && !_isExpanded) { HideNotifPeek(); ToggleExpand(); return; }
            if (!inside && _isExpanded) { ToggleExpand(); return; }
            if (kindChanged && !_isExpanded) ApplyCollapsedKind();
        }

        private void ApplyCollapsedKind()
        {
            if (_isExpanded || _isAnimating) return;
            _isAnimating = true;
            HideNotifPeek();

            var dur = TimeSpan.FromMilliseconds(280);
            var spline = new KeySpline(0.16, 1, 0.3, 1);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            int tw = _hasWindowUnder ? DOT_WIDTH : COLLAPSED_WIDTH;
            int th = _hasWindowUnder ? DOT_HEIGHT : COLLAPSED_HEIGHT;

            var wA = new DoubleAnimationUsingKeyFrames { Duration = dur };
            wA.KeyFrames.Add(new SplineDoubleKeyFrame(tw, KeyTime.FromPercent(1.0), spline));
            var hA = new DoubleAnimationUsingKeyFrames { Duration = dur };
            hA.KeyFrames.Add(new SplineDoubleKeyFrame(th, KeyTime.FromPercent(1.0), spline));
            hA.Completed += (s, e) => _isAnimating = false;

            IslandBorder.BeginAnimation(WidthProperty, null);
            IslandBorder.BeginAnimation(HeightProperty, null);
            IslandBorder.BeginAnimation(WidthProperty, wA);
            IslandBorder.BeginAnimation(HeightProperty, hA);

            if (_hasWindowUnder)
            {
                IslandBorder.CornerRadius = new CornerRadius(10);
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)) { EasingFunction = ease };
                fade.Completed += (s, e) => { if (_hasWindowUnder && !_isExpanded) TopPanel.Visibility = Visibility.Collapsed; };
                TopPanel.BeginAnimation(OpacityProperty, fade);
                DotFace.Visibility = Visibility.Visible;
                DotFace.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)) { BeginTime = TimeSpan.FromMilliseconds(80), EasingFunction = ease });
            }
            else
            {
                IslandBorder.CornerRadius = new CornerRadius(22);
                DotFace.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(100)) { EasingFunction = ease });
                TopPanel.Visibility = Visibility.Visible;
                TopPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(60), EasingFunction = ease });
                TopPanel.BeginAnimation(HeightProperty, new DoubleAnimation(HEAD_COLLAPSED, dur) { EasingFunction = ease });
                ClockViewbox.BeginAnimation(HeightProperty, new DoubleAnimation(CLOCK_COLLAPSED, dur) { EasingFunction = ease });
                foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox })
                {
                    vb.BeginAnimation(WidthProperty, new DoubleAnimation(RING_COLLAPSED, dur) { EasingFunction = ease });
                    vb.BeginAnimation(HeightProperty, new DoubleAnimation(RING_COLLAPSED, dur) { EasingFunction = ease });
                }
                CountersPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                var hideDate = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
                hideDate.Completed += (s, e) => { if (!_isExpanded) DatePanel.Visibility = Visibility.Collapsed; };
                DatePanel.BeginAnimation(OpacityProperty, hideDate);
            }
        }

        // ==================== РАСКРЫТИЕ ====================
        private void ToggleExpand()
        {
            if (_isAnimating) return;
            _isAnimating = true;
            _isExpanded = !_isExpanded;

            HideNotifPeek();
            _osdShown = false;
            _osdHideTimer.Stop();
            OsdGrid.Visibility = Visibility.Collapsed;
            TopPanel.BeginAnimation(OpacityProperty, null);
            TopPanel.Opacity = 1;

            var dur = TimeSpan.FromMilliseconds(350);
            var spline = new KeySpline(0.16, 1, 0.3, 1);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (!_isExpanded) _hasWindowUnder = IsWindowUnderIsland();

            int tw, th;
            if (_isExpanded) { tw = EXPANDED_WIDTH; th = EXPANDED_HEIGHT; }
            else { tw = _hasWindowUnder ? DOT_WIDTH : COLLAPSED_WIDTH; th = _hasWindowUnder ? DOT_HEIGHT : COLLAPSED_HEIGHT; }

            var wA = new DoubleAnimationUsingKeyFrames { Duration = dur };
            wA.KeyFrames.Add(new SplineDoubleKeyFrame(tw, KeyTime.FromPercent(1.0), spline));
            var hA = new DoubleAnimationUsingKeyFrames { Duration = dur };
            hA.KeyFrames.Add(new SplineDoubleKeyFrame(th, KeyTime.FromPercent(1.0), spline));
            hA.Completed += (s, e) => _isAnimating = false;

            IslandBorder.BeginAnimation(WidthProperty, null);
            IslandBorder.BeginAnimation(HeightProperty, null);
            IslandBorder.BeginAnimation(WidthProperty, wA);
            IslandBorder.BeginAnimation(HeightProperty, hA);

            if (_isExpanded)
            {
                IslandBorder.CornerRadius = new CornerRadius(24);
                DotFace.Visibility = Visibility.Collapsed;
                DotFace.BeginAnimation(OpacityProperty, null);
                DotFace.Opacity = 1;
                TopPanel.Visibility = Visibility.Visible;

                TopPanel.BeginAnimation(HeightProperty, null);
                ClockViewbox.BeginAnimation(HeightProperty, null);
                foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox })
                { vb.BeginAnimation(WidthProperty, null); vb.BeginAnimation(HeightProperty, null); }

                TopPanel.BeginAnimation(HeightProperty, new DoubleAnimation(HEAD_EXPANDED, dur) { EasingFunction = ease });
                ClockViewbox.BeginAnimation(HeightProperty, new DoubleAnimation(CLOCK_EXPANDED, dur) { EasingFunction = ease });
                foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox })
                {
                    vb.BeginAnimation(WidthProperty, new DoubleAnimation(RING_EXPANDED, dur) { EasingFunction = ease });
                    vb.BeginAnimation(HeightProperty, new DoubleAnimation(RING_EXPANDED, dur) { EasingFunction = ease });
                }

                DatePanel.Visibility = Visibility.Visible;
                DatePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                CountersPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(120)));

                BoardGrid.BeginAnimation(OpacityProperty, null);
                BoardGrid.Visibility = Visibility.Visible;
                BoardGrid.Opacity = 0;
                BoardGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(220))
                { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = ease });

                BuildDeviceStack();
                BuildNotificationTile();
            }
            else
            {
                HideTileFocus();

                var hideContent = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
                hideContent.Completed += (s, e) => { if (!_isExpanded) BoardGrid.Visibility = Visibility.Collapsed; };
                BoardGrid.BeginAnimation(OpacityProperty, hideContent);

                if (_hasWindowUnder)
                {
                    IslandBorder.CornerRadius = new CornerRadius(10);
                    TopPanel.Visibility = Visibility.Collapsed;
                    DotFace.Visibility = Visibility.Visible;
                    DotFace.BeginAnimation(OpacityProperty, null);
                    DotFace.Opacity = 1;
                }
                else
                {
                    IslandBorder.CornerRadius = new CornerRadius(22);
                    TopPanel.Visibility = Visibility.Visible;
                    DotFace.Visibility = Visibility.Collapsed;

                    TopPanel.BeginAnimation(HeightProperty, null);
                    ClockViewbox.BeginAnimation(HeightProperty, null);
                    foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox })
                    { vb.BeginAnimation(WidthProperty, null); vb.BeginAnimation(HeightProperty, null); }

                    TopPanel.BeginAnimation(HeightProperty, new DoubleAnimation(HEAD_COLLAPSED, dur) { EasingFunction = ease });
                    ClockViewbox.BeginAnimation(HeightProperty, new DoubleAnimation(CLOCK_COLLAPSED, dur) { EasingFunction = ease });
                    foreach (var vb in new[] { CpuViewbox, RamViewbox, GpuViewbox })
                    {
                        vb.BeginAnimation(WidthProperty, new DoubleAnimation(RING_COLLAPSED, dur) { EasingFunction = ease });
                        vb.BeginAnimation(HeightProperty, new DoubleAnimation(RING_COLLAPSED, dur) { EasingFunction = ease });
                    }
                    CountersPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
                    var hideDate = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
                    hideDate.Completed += (s, e) => { if (!_isExpanded) DatePanel.Visibility = Visibility.Collapsed; };
                    DatePanel.BeginAnimation(OpacityProperty, hideDate);
                }
            }
        }
    }
}