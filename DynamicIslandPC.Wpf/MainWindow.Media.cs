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
        // ==================== УПРАВЛЕНИЕ ПЛЕЕРОМ ====================
        private async void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            try { await _mediaSession.TogglePlayPauseAsync(); } catch { }
        }

        private async void Prev_Click(object sender, RoutedEventArgs e)
        {
            try { await _mediaSession.PrevAsync(); } catch { }
        }

        private async void Next_Click(object sender, RoutedEventArgs e)
        {
            try { await _mediaSession.NextAsync(); } catch { }
        }

        private void AnimateEq()
        {
            if (EqBars == null) return;
            bool isPlaying = PlayPauseBtn.Content?.ToString() == "\uE769";
            foreach (UIElement child in EqBars.Children)
            {
                if (child is Rectangle bar)
                {
                    double h = isPlaying
                        ? 4 + 14 * Math.Abs(Math.Sin(DateTime.Now.Millisecond * 0.01 + bar.GetHashCode() * 0.3))
                        : 4;
                    bar.Height = h;
                }
            }
        }

        // ==================== МЕДИА-ПАНЕЛЬ ====================
        private void ShuffleBtn_Click(object sender, RoutedEventArgs e)
        {
            _shuffleOn = !_shuffleOn;
            ShuffleBtn.Background = _shuffleOn ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("CardBg");
            ShuffleBtn.Foreground = _shuffleOn ? new SolidColorBrush(Color.FromRgb(7, 20, 24)) : (SolidColorBrush)FindResource("TextPrimary");
        }

        private void RepeatBtn_Click(object sender, RoutedEventArgs e)
        {
            _repeatOn = !_repeatOn;
            RepeatBtn.Background = _repeatOn ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("CardBg");
            RepeatBtn.Foreground = _repeatOn ? new SolidColorBrush(Color.FromRgb(7, 20, 24)) : (SolidColorBrush)FindResource("TextPrimary");
        }

        private void MediaProgress_Click(object sender, MouseButtonEventArgs e)
        {
            if (_mediaDuration.TotalSeconds <= 0 || sender is not ProgressBar pb) return;
            double ratio = Math.Max(0, Math.Min(1, e.GetPosition(pb).X / Math.Max(1, pb.ActualWidth)));
            TimeSpan target = TimeSpan.FromSeconds(_mediaDuration.TotalSeconds * ratio);
            try { _mediaSession.SeekAsync(target); } catch (Exception ex) { Log.Warn("[Media] seek: " + ex.Message); }
        }

        private void AppVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingAppVolume || _currentSessionPid == 0) return;
            int volume = (int)e.NewValue;
            AppVolumeText.Text = $"{volume}%";
            try { _appVolume.SetAppVolume(_currentSessionPid, volume); } catch (Exception ex) { Log.Warn("[Media] app vol: " + ex.Message); }
        }

        private void OpenAppBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSessionPid == 0) return;
            try
            {
                var proc = Process.GetProcessById(_currentSessionPid);
                if (proc.MainWindowHandle != IntPtr.Zero)
                    NativeMethods.SetForegroundWindow(proc.MainWindowHandle);
            }
            catch (Exception ex) { Log.Error("[Media] open app", ex); }
        }

        private (int Pid, int Volume, bool Muted)? GetCurrentSessionAudio()
        {
            foreach (var app in _appVolume.AppVolumes)
            {
                string n = app.DisplayName.ToLower();
                if (n.Contains("spotify") || n.Contains("vlc") || n.Contains("music") || n.Contains("itunes") || n.Contains("wmplayer") || n.Contains("media"))
                    return (app.ProcessId, app.Volume, app.IsMuted);
            }
            foreach (var app in _appVolume.AppVolumes)
                if (app.Volume > 0 && !app.IsMuted) return (app.ProcessId, app.Volume, app.IsMuted);
            return null;
        }

        private void UpdateMediaBlurredCover(BitmapImage? cover)
        {
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
            _trackHistory.Insert(0, (title, artist, DateTime.Now));
            if (_trackHistory.Count > 5) _trackHistory.RemoveAt(_trackHistory.Count - 1);
            BuildTrackHistoryList();
        }

        private void BuildTrackHistoryList()
        {
            if (TrackHistoryStack == null) return;
            TrackHistoryStack.Children.Clear();
            if (_trackHistory.Count == 0)
            {
                TrackHistoryStack.Children.Add(new TextBlock { Text = "История пуста", FontSize = 10, Foreground = (SolidColorBrush)FindResource("TextFaint"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) });
                return;
            }
            foreach (var t in _trackHistory)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = $"{t.Title} — {t.Artist}", FontSize = 10, Foreground = (SolidColorBrush)FindResource("TextPrimary"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                var time = new TextBlock { Text = t.Time.ToString("HH:mm"), FontSize = 9, Foreground = (SolidColorBrush)FindResource("TextFaint"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(time, 1); row.Children.Add(time);
                TrackHistoryStack.Children.Add(row);
            }
        }

        private void BuildMediaSessionsList()
        {
            if (MediaSessionsStack == null) return;
            MediaSessionsStack.Children.Clear();
            if (string.IsNullOrEmpty(PlayerTitleText.Text) || PlayerTitleText.Text == "Нет трека")
            {
                MediaSessionsStack.Children.Add(new TextBlock { Text = "Нет активных сессий", FontSize = 10, Foreground = (SolidColorBrush)FindResource("TextFaint"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) });
                return;
            }
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{PlayerTitleText.Text} — {PlayerArtistText.Text}", FontSize = 10, Foreground = (SolidColorBrush)FindResource("TextPrimary"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var btn = new Button { Content = "Активно", FontSize = 9, Style = (Style)FindResource("ChipButton"), IsEnabled = false, Opacity = 0.6 };
            Grid.SetColumn(btn, 1); row.Children.Add(btn);
            MediaSessionsStack.Children.Add(row);
        }

        private void UpdateMediaPanel()
        {
            MediaLargeTitle.Text = PlayerTitleText.Text;
            MediaLargeArtist.Text = PlayerArtistText.Text;
            MediaCurrentTime.Text = FormatTime(_mediaPosition);
            MediaTotalTime.Text = FormatTime(_mediaDuration);
            if (_mediaDuration.TotalSeconds > 0)
            {
                MediaProgress.Maximum = _mediaDuration.TotalSeconds;
                MediaProgress.Value = _mediaPosition.TotalSeconds;
            }
            else
            {
                MediaProgress.Value = 0;
            }
            var audio = GetCurrentSessionAudio();
            if (audio.HasValue)
            {
                _currentSessionPid = audio.Value.Pid;
                _isUpdatingAppVolume = true;
                AppVolumeSlider.Value = audio.Value.Volume;
                AppVolumeText.Text = $"{audio.Value.Volume}%";
                _isUpdatingAppVolume = false;
                OpenAppBtn.IsEnabled = true;
                OpenAppBtn.Opacity = 1;
            }
            else
            {
                _currentSessionPid = 0;
                _isUpdatingAppVolume = true;
                AppVolumeSlider.Value = 100;
                AppVolumeText.Text = "—";
                _isUpdatingAppVolume = false;
                OpenAppBtn.IsEnabled = false;
                OpenAppBtn.Opacity = 0.4;
            }
            BuildMediaSessionsList();
            BuildTrackHistoryList();
        }

        private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:D2}";
    }
}