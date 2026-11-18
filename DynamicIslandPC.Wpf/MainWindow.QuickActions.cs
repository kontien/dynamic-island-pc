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
            PinMenuItem.Header = _pinned ? "📌 Открепить" : "📌 Закрепить";
            QuickPin.Background = _pinned ? new SolidColorBrush(Color.FromArgb(40, 255, 176, 32)) : (SolidColorBrush)FindResource("CardBg");
            QuickPin.BorderBrush = _pinned ? new SolidColorBrush(Color.FromArgb(120, 255, 176, 32)) : (SolidColorBrush)FindResource("Hair");
        }

        private void QuickShot_Click(object sender, RoutedEventArgs e) => CaptureScreenshot();
        private void CaptureScreenshot()
        {
            try
            {
                var region = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                var bmp = ScreenshotService.CaptureRegion(region);
                string path = ScreenshotService.SavePng(bmp);
                _shelf.AddFiles(new[] { path });
                DispatchToUi(() => ShowScreenshotConfirmation(IOPath.GetFileName(path)));
            }
            catch (Exception ex) { Log.Error("[Screenshot] capture/save", ex); DispatchToUi(() => ShowScreenshotConfirmation("Ошибка снимка", isError: true)); }
        }

        private void ShowScreenshotConfirmation(string message, bool isError = false)
        {
            _osdHideTimer.Stop(); _osdShown = true;
            SmallOsdIcon.Text = isError ? "⚠️" : "📸"; LargeOsdIcon.Text = isError ? "⚠️" : "📸";
            SmallOsdAppName.Text = isError ? message : $"Снимок сохранён: {message}";
            LargeOsdAppName.Text = isError ? message : $"Снимок сохранён: {message}";
            SmallOsdPercentText.Text = ""; LargeOsdPercentText.Text = "✓";
            SmallOsdProgress.Visibility = Visibility.Collapsed; LargeOsdProgress.Visibility = Visibility.Collapsed;
            SmallOsd.Visibility = Visibility.Visible; LargeOsd.Visibility = Visibility.Collapsed;
            OsdGrid.Visibility = Visibility.Visible;
            OsdGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            TopPanel.Visibility = Visibility.Visible;
            TopPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
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
            QuickDnd.Background = _dnd ? new SolidColorBrush(Color.FromArgb(40, 167, 139, 250)) : (SolidColorBrush)FindResource("CardBg");
            QuickDnd.BorderBrush = _dnd ? new SolidColorBrush(Color.FromArgb(120, 167, 139, 250)) : (SolidColorBrush)FindResource("Hair");
            if (_dnd) HideOSD();
        }

        private void Window_DragOver(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop, true) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
        private void Window_Drop(object sender, DragEventArgs e) { try { if (e.Data.GetDataPresent(DataFormats.FileDrop, true) && e.Data.GetData(DataFormats.FileDrop, true) is string[] files) _shelf.AddFiles(files); } catch (Exception ex) { Log.Error("[Shelf] drop", ex); } finally { e.Handled = true; } }
        private void IslandBorder_MouseUp(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Middle) TogglePin(); }
        private void PinMenuItem_Click(object sender, RoutedEventArgs e) => TogglePin();
        private void MenuClearClipboard_Click(object sender, RoutedEventArgs e) => _clipboard.Clear();
        private void MenuClearShelf_Click(object sender, RoutedEventArgs e) => _shelf.Clear();
        private void MenuExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
    }
}