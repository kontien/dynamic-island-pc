using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private void ShowTileFocus(Border panel, Border sourceTile)
        {
            if (_isTileFocused) return;
            _isTileFocused = true;
            BackHint.Visibility = Visibility.Visible;
            panel.Visibility = Visibility.Visible;
            panel.Opacity = 0;
            panel.UpdateLayout();

            var tilePos = sourceTile.TransformToAncestor(BoardGrid).Transform(new Point(0, 0));
            var transformGroup = new TransformGroup();
            var scale = new ScaleTransform();
            var translate = new TranslateTransform();
            transformGroup.Children.Add(scale);
            transformGroup.Children.Add(translate);
            panel.RenderTransformOrigin = new Point(0, 0);
            panel.RenderTransform = transformGroup;

            double scaleX = sourceTile.ActualWidth / panel.ActualWidth;
            double scaleY = sourceTile.ActualHeight / panel.ActualHeight;
            scale.ScaleX = scaleX;
            scale.ScaleY = scaleY;
            translate.X = tilePos.X - panel.Margin.Left;
            translate.Y = tilePos.Y - panel.Margin.Top;

            var dur = TimeSpan.FromMilliseconds(400);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });

            foreach (var tile in new[] { TileMedia, TileClip, TileVol, TileShelf, TileOut, TileSys, VpnTileBorder, TileQuick })
                tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
        }

        private void HideTileFocus()
        {
            if (!_isTileFocused) return;
            _isTileFocused = false;
            BackHint.Visibility = Visibility.Collapsed;

            var dur = TimeSpan.FromMilliseconds(250);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            foreach (var panel in new[] { MixerPanel, MediaPanel, ClipboardPanel, ShelfPanel, AudioPanel, SystemPanel })
            {
                var p = panel;
                var anim = new DoubleAnimation(0, dur) { EasingFunction = ease };
                anim.Completed += (s, e) => { if (!_isTileFocused) p.Visibility = Visibility.Collapsed; };
                panel.BeginAnimation(OpacityProperty, anim);
            }

            foreach (var tile in new[] { TileMedia, TileClip, TileVol, TileShelf, TileOut, TileSys, VpnTileBorder, TileQuick })
                tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
        }

        private void TopPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        { if (_isTileFocused) { HideTileFocus(); e.Handled = true; } }

        private void Panel_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        { if (_isTileFocused) { HideTileFocus(); e.Handled = true; } }

        protected override void OnKeyDown(KeyEventArgs e)
        { base.OnKeyDown(e); if (e.Key == Key.Escape && _isTileFocused) { HideTileFocus(); e.Handled = true; } }

        private void TileMedia_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(MediaPanel, TileMedia);
            UpdateMediaPanel();
        }

        private void TileClip_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(ClipboardPanel, TileClip);
            BuildClipboardLargeList();
            AnimateStaggered(ClipboardLargeStack, TimeSpan.FromMilliseconds(100));
        }

        private void TileVol_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(MixerPanel, TileVol);
            _appVolume.RefreshAppVolumes();
            BuildAppVolumeList();
            AnimateStaggered(AppVolumeStack, TimeSpan.FromMilliseconds(100));
        }

        private void TileShelf_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(ShelfPanel, TileShelf);
            BuildShelfLargeList();
            AnimateStaggered(ShelfLargeGrid, TimeSpan.FromMilliseconds(100));
        }

        private void TileOut_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(AudioPanel, TileOut);
            BuildDeviceLargeStack();
            AnimateStaggered(DeviceLargeStack, TimeSpan.FromMilliseconds(100));
        }

        private void TileSys_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(SystemPanel, TileSys);
            BuildProcessList();
            AnimateStaggered(CpuProcessStack, TimeSpan.FromMilliseconds(100));
            AnimateStaggered(RamProcessStack, TimeSpan.FromMilliseconds(200));
        }

        private void MixerCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void MediaCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void ClipboardCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void ShelfCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void AudioCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void SystemCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
    }
}