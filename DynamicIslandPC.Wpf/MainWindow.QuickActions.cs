using DynamicIslandPC.Wpf.Infrastructure;
using DynamicIslandPC.Wpf.Services;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using IOPath = System.IO.Path;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private void QuickPin_Click(object sender, RoutedEventArgs e) => TogglePin();

        private void TogglePin()
        {
            _pinned = !_pinned;
            PinIndicator.Visibility = _pinned ? Visibility.Visible : Visibility.Collapsed;
            PinMenuItem.Header = _pinned ? "📌 Открепить остров" : "📌 Закрепить остров";
            RefreshQuickToggles();
            if (!_pinned)
            {
                _hasWindowUnder = IsWindowUnderIsland();
                if (!_isExpanded) ApplyCollapsedKind();
            }
        }

        internal void RefreshQuickToggles()
        {
            if (QuickPin == null || QuickDnd == null) return;
            PaintQuick(QuickPin, _pinned);
            PaintQuick(QuickDnd, _dnd);
        }

        private void PaintQuick(Button b, bool on)
        {
            b.Background = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("CardBg");
            b.Foreground = on ? (SolidColorBrush)FindResource("OnAccent") : (SolidColorBrush)FindResource("TextPrimary");
            b.BorderBrush = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("Hair");
        }

        private void QuickShot_Click(object sender, RoutedEventArgs e) => CaptureScreenshot();

        private void CaptureScreenshot()
        {
            try
            {
                var region = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                var bmp = ScreenshotService.CaptureRegion(region);
                string path = ScreenshotService.SavePng(bmp);
                _shelf.AddFiles(new[] { path });
                DispatchToUi(() => ShowScreenshotConfirmation(IOPath.GetFileName(path)));
            }
            catch (Exception ex)
            {
                Log.Error("[Screenshot] capture/save", ex);
                DispatchToUi(() => ShowScreenshotConfirmation("Ошибка снимка", isError: true));
            }
        }

        private void ShowScreenshotConfirmation(string message, bool isError = false)
        {
            _osdHideTimer.Stop();
            _osdShown = true;

            SmallOsdIcon.Text = isError ? "⚠️" : "📸";
            LargeOsdIcon.Text = isError ? "⚠️" : "📸";
            SmallOsdAppName.Text = isError ? message : $"Сохранено: {message}";
            LargeOsdAppName.Text = SmallOsdAppName.Text;
            SmallOsdPercentText.Text = "";
            LargeOsdPercentText.Text = isError ? "!" : "✓";
            SmallOsdProgress.Visibility = Visibility.Collapsed;
            LargeOsdProgress.Visibility = Visibility.Collapsed;
            SmallOsd.Visibility = Visibility.Visible;
            LargeOsd.Visibility = Visibility.Collapsed;
            OsdGrid.Visibility = Visibility.Visible;
            OsdGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(GlassMs(140))) { EasingFunction = GlassEase });

            var fadeHeader = new DoubleAnimation(0, TimeSpan.FromMilliseconds(GlassMs(110))) { EasingFunction = GlassEase };
            fadeHeader.Completed += (s, e) => { if (_osdShown) TopPanel.Visibility = Visibility.Collapsed; };
            TopPanel.BeginAnimation(OpacityProperty, fadeHeader);

            _osdHideTimer.Interval = TimeSpan.FromSeconds(2.5);
            _osdHideTimer.Start();
        }

        private async void QuickDev_Click(object sender, RoutedEventArgs e)
        {
            var devs = _audio.Devices;
            if (devs.Count < 2) return;
            int idx = devs.FindIndex(d => d.ID == _audio.ActiveDeviceId);
            idx = (idx + 1) % devs.Count;
            await _audio.SwitchDeviceAsync(devs[idx].ID, _lifetimeCts.Token);
        }

        private void QuickDnd_Click(object sender, RoutedEventArgs e)
        {
            _dnd = !_dnd;
            RefreshQuickToggles();
            if (_dnd) HideOSD();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop, true) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop, true) && e.Data.GetData(DataFormats.FileDrop, true) is string[] files)
                {
                    _shelf.AddFiles(files);
                    ShowScreenshotConfirmation($"На полку: {files.Length} файл(ов)");
                }
            }
            catch (Exception ex) { Log.Error("[Shelf] drop", ex); }
            finally { e.Handled = true; }
        }

        private void IslandBorder_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle) TogglePin();
        }

        private void PinMenuItem_Click(object sender, RoutedEventArgs e) => TogglePin();
        private void MenuClearClipboard_Click(object sender, RoutedEventArgs e) => _clipboard.Clear();
        private void MenuClearShelf_Click(object sender, RoutedEventArgs e) => _shelf.Clear();
        private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}