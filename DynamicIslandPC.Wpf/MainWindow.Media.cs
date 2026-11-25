using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        // ==================== ТРАНСПОРТ ====================
        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        { try { await _mediaSession.TogglePlayPauseAsync(); } catch (Exception ex) { Log.Warn("[Media] toggle: " + ex.Message); } }

        private async void Prev_Click(object sender, RoutedEventArgs e)
        { try { await _mediaSession.PrevAsync(); } catch (Exception ex) { Log.Warn("[Media] prev: " + ex.Message); } }

        private async void Next_Click(object sender, RoutedEventArgs e)
        { try { await _mediaSession.NextAsync(); } catch (Exception ex) { Log.Warn("[Media] next: " + ex.Message); } }

        private void AnimateEq()
        {
            if (EqBars == null) return;
            bool isPlaying = PlayPauseBtn.Content?.ToString() == "\uE769";
            double t = DateTime.Now.TimeOfDay.TotalSeconds;
            int i = 0;
            foreach (UIElement child in EqBars.Children)
            {
                if (child is Rectangle bar)
                {
                    bar.Height = isPlaying ? 3 + 11 * Math.Abs(Math.Sin(t * 4.2 + i * 1.05)) : 3;
                    i++;
                }
            }
        }

        // ==================== ПЕРЕКЛЮЧАТЕЛИ ====================
        private void ShuffleBtn_Click(object sender, RoutedEventArgs e)
        {
            _shuffleOn = !_shuffleOn;
            RefreshMediaToggles();
        }

        private void RepeatBtn_Click(object sender, RoutedEventArgs e)
        {
            _repeatOn = !_repeatOn;
            RefreshMediaToggles();
        }

        // Вызывается из MainWindow.Glass.cs при смене акцента/стекла.
        private void RefreshMediaToggles()
        {
            if (ShuffleBtn != null) PaintMediaToggle(ShuffleBtn, _shuffleOn);
            if (RepeatBtn != null) PaintMediaToggle(RepeatBtn, _repeatOn);
        }

        private void PaintMediaToggle(Button b, bool on)
        {
            b.Background = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("CardBg");
            b.Foreground = on ? MediaOnAccent() : (SolidColorBrush)FindResource("TextPrimary");
            b.BorderBrush = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("Hair");
        }

        private static SolidColorBrush MediaOnAccent()
        {
            return new SolidColorBrush(Color.FromRgb(7, 20, 24));
        }

        // ==================== ПЕРЕМОТКА / ГРОМКОСТЬ ====================
        private async void MediaProgress_Click(object sender, MouseButtonEventArgs e)
        {
            if (_mediaDuration.TotalSeconds <= 0 || sender is not ProgressBar pb) return;
            double ratio = Math.Max(0, Math.Min(1, e.GetPosition(pb).X / Math.Max(1, pb.ActualWidth)));
            var target = TimeSpan.FromSeconds(_mediaDuration.TotalSeconds * ratio);
            try { await _mediaSession.SeekAsync(target); }
            catch (Exception ex) { Log.Warn("[Media] seek: " + ex.Message); }
        }

        private void AppVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingAppVolume || _currentSessionPid == 0) return;
            int volume = (int)e.NewValue;
            AppVolumeText.Text = $"{volume}%";
            try { _appVolume.SetAppVolume(_currentSessionPid, volume); }
            catch (Exception ex) { Log.Warn("[Media] app vol: " + ex.Message); }
        }

        private void OpenAppBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSessionPid == 0) return;
            try
            {
                using var proc = Process.GetProcessById(_currentSessionPid);
                proc.Refresh();
                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
                    return;
                }
                string? exe = null;
                try { exe = proc.MainModule?.FileName; } catch { }
                if (!string.IsNullOrEmpty(exe))
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("[Media] open app", ex); }
        }

        private (int Pid, int Volume, bool Muted)? GetCurrentSessionAudio()
        {
            string hint = ((PlayerTitleText.Text ?? "") + " " + (PlayerArtistText.Text ?? "")).ToLowerInvariant();

            foreach (var app in _appVolume.AppVolumes)
            {
                string n = (app.DisplayName ?? "").ToLowerInvariant();
                if (n.Length > 3 && hint.Contains(n)) return (app.ProcessId, app.Volume, app.IsMuted);
            }
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = (app.DisplayName ?? "").ToLowerInvariant();
                if (n.Contains("spotify") || n.Contains("vlc") || n.Contains("music") ||
                    n.Contains("itunes") || n.Contains("wmplayer") || n.Contains("media"))
                    return (app.ProcessId, app.Volume, app.IsMuted);
            }
            foreach (var app in _appVolume.AppVolumes)
                if (app.Volume > 0 && !app.IsMuted) return (app.ProcessId, app.Volume, app.IsMuted);
            return null;
        }

        // ==================== ОБЛОЖКА / ИСТОРИЯ ====================
        private void UpdateMediaBlurredCover(BitmapImage? cover)
        {
            if (MediaBlurredCover == null || MediaLargeCover == null) return;
            if (cover != null)
            {
                MediaBlurredCover.Source = cover;
                MediaLargeCover.Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
                MediaLargeCoverText.Visibility = Visibility.Collapsed;
            }
            else
            {
                MediaBlurredCover.Source = null;
                MediaLargeCover.Background = new SolidColorBrush(Color.FromRgb(51, 60, 80));
                MediaLargeCoverText.Visibility = Visibility.Visible;
            }
        }

        private void AddTrackToHistory(string title, string artist)
        {
            if (string.IsNullOrEmpty(title) || title == "Нет трека") return;
            if (_trackHistory.Count > 0 && _trackHistory[0].Title == title && _trackHistory[0].Artist == artist) return;
            _trackHistory.Insert(0, (title, artist ?? "", DateTime.Now));
            while (_trackHistory.Count > 5) _trackHistory.RemoveAt(_trackHistory.Count - 1);
            BuildTrackHistoryList();
        }

        private void BuildTrackHistoryList()
        {
            if (TrackHistoryStack == null) return;
            TrackHistoryStack.Children.Clear();
            if (_trackHistory.Count == 0)
            {
                TrackHistoryStack.Children.Add(new TextBlock
                {
                    Text = "История пуста",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 6)
                });
                return;
            }

            foreach (var t in _trackHistory)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock
                {
                    Text = $"{t.Title} — {t.Artist}",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                var time = new TextBlock
                {
                    Text = t.Time.ToString("HH:mm"),
                    FontSize = 9,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(time, 1);
                row.Children.Add(time);
                TrackHistoryStack.Children.Add(row);
            }
        }

        private void BuildMediaSessionsList()
        {
            if (MediaSessionsStack == null) return;
            MediaSessionsStack.Children.Clear();

            if (string.IsNullOrEmpty(PlayerTitleText.Text) || PlayerTitleText.Text == "Нет трека")
            {
                MediaSessionsStack.Children.Add(new TextBlock
                {
                    Text = "Нет активных сессий",
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 8, 0, 8)
                });
                return;
            }

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = $"{PlayerTitleText.Text} — {PlayerArtistText.Text}",
                FontSize = 10,
                Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var btn = new Button
            {
                Content = PlayPauseBtn.Content?.ToString() == "\uE769" ? "Играет" : "Пауза",
                FontSize = 9,
                Style = (Style)FindResource("ChipButton"),
                IsEnabled = false,
                Opacity = 0.7
            };
            Grid.SetColumn(btn, 1);
            row.Children.Add(btn);
            MediaSessionsStack.Children.Add(row);
        }

        // ==================== ОБНОВЛЕНИЕ ПАНЕЛИ ====================
        private void UpdateMediaPanel()
        {
            if (MediaLargeTitle == null) return;

            MediaLargeTitle.Text = PlayerTitleText.Text;
            MediaLargeArtist.Text = PlayerArtistText.Text;
            MediaCurrentTime.Text = FormatTime(_mediaPosition);
            MediaTotalTime.Text = FormatTime(_mediaDuration);

            if (_mediaDuration.TotalSeconds > 0)
            {
                MediaProgress.Maximum = _mediaDuration.TotalSeconds;
                MediaProgress.Value = Math.Min(_mediaPosition.TotalSeconds, _mediaDuration.TotalSeconds);
            }
            else
            {
                MediaProgress.Value = 0;
            }

            if (MediaPlayPauseBtn != null) MediaPlayPauseBtn.Content = PlayPauseBtn.Content;
            RefreshMediaToggles();

            var audio = GetCurrentSessionAudio();
            _isUpdatingAppVolume = true;
            if (audio.HasValue)
            {
                _currentSessionPid = audio.Value.Pid;
                AppVolumeSlider.Value = audio.Value.Volume;
                AppVolumeSlider.IsEnabled = true;
                AppVolumeText.Text = audio.Value.Muted ? "mute" : $"{audio.Value.Volume}%";
                OpenAppBtn.IsEnabled = _currentSessionPid > 0;
                OpenAppBtn.Opacity = OpenAppBtn.IsEnabled ? 1 : 0.4;
            }
            else
            {
                _currentSessionPid = 0;
                AppVolumeSlider.Value = 100;
                AppVolumeSlider.IsEnabled = false;
                AppVolumeText.Text = "—";
                OpenAppBtn.IsEnabled = false;
                OpenAppBtn.Opacity = 0.4;
            }
            _isUpdatingAppVolume = false;

            BuildMediaSessionsList();
            BuildTrackHistoryList();
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}";
            return $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
        }
    }
}