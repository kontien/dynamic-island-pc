using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private Border[] FocusTiles => new[]
        {
            TileMedia, TileClip, TileVol, TileShelf, TileOut, TileSys, TileNotif, VpnTileBorder, TileQuick
        };

        private Border[] FocusPanels => new[]
        {
            MixerPanel, MediaPanel, ClipboardPanel, ShelfPanel, AudioPanel, SystemPanel, NotifPanel
        };

        private void ShowTileFocus(Border panel, Border sourceTile)
        {
            if (_isTileFocused) return;
            _isTileFocused = true;
            HideNotifPeek();
            BackHint.Visibility = Visibility.Visible;

            panel.Visibility = Visibility.Visible;
            panel.Opacity = 0;
            panel.UpdateLayout();

            double pw = panel.ActualWidth, ph = panel.ActualHeight;
            if (pw <= 0 || ph <= 0 || double.IsNaN(pw) || double.IsNaN(ph))
            {
                panel.RenderTransform = null;
                panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
                foreach (var t in FocusTiles) t.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(130)));
                return;
            }

            var tilePos = sourceTile.TransformToAncestor(BoardGrid).Transform(new Point(0, 0));
            var transformGroup = new TransformGroup();
            var scale = new ScaleTransform();
            var translate = new TranslateTransform();
            transformGroup.Children.Add(scale);
            transformGroup.Children.Add(translate);
            panel.RenderTransformOrigin = new Point(0, 0);
            panel.RenderTransform = transformGroup;

            scale.ScaleX = Math.Max(0.04, sourceTile.ActualWidth / pw);
            scale.ScaleY = Math.Max(0.04, sourceTile.ActualHeight / ph);
            translate.X = tilePos.X - panel.Margin.Left;
            translate.Y = tilePos.Y - panel.Margin.Top;

            var dur = TimeSpan.FromMilliseconds(350);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur) { EasingFunction = ease });

            foreach (var tile in FocusTiles)
                tile.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
        }

        private void HideTileFocus()
        {
            if (!_isTileFocused) return;
            _isTileFocused = false;
            BackHint.Visibility = Visibility.Collapsed;

            var dur = TimeSpan.FromMilliseconds(250);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            foreach (var panel in FocusPanels)
            {
                if (panel.Visibility != Visibility.Visible) continue;
                var p = panel;
                var anim = new DoubleAnimation(0, dur) { EasingFunction = ease };
                anim.Completed += (s, e) =>
                {
                    if (!_isTileFocused)
                    {
                        p.Visibility = Visibility.Collapsed;
                        p.BeginAnimation(OpacityProperty, null);
                        p.Opacity = 0;
                        p.RenderTransform = null;
                    }
                };
                panel.BeginAnimation(OpacityProperty, anim);
            }

            foreach (var tile in FocusTiles)
            {
                tile.BeginAnimation(OpacityProperty, null);
                tile.Opacity = 0;
                tile.BeginAnimation(OpacityProperty, new DoubleAnimation(1, dur)
                { BeginTime = TimeSpan.FromMilliseconds(50), EasingFunction = ease });
            }
        }

        private void TopPanel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        { if (_isTileFocused) { HideTileFocus(); e.Handled = true; } }

        private void Panel_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        { if (_isTileFocused) { HideTileFocus(); e.Handled = true; } }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape && _isTileFocused) { HideTileFocus(); e.Handled = true; }
        }

        private void TileMedia_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(MediaPanel, TileMedia);
            UpdateMediaPanel();
        }

        private void TileClip_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(ClipboardPanel, TileClip);
            BuildClipboardLargeList();
        }

        private void TileVol_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(MixerPanel, TileVol);
            _appVolume.RefreshAppVolumes();
            BuildAppVolumeList();
        }

        private void TileShelf_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(ShelfPanel, TileShelf);
            BuildShelfLargeList();
        }

        private void TileOut_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(AudioPanel, TileOut);
            BuildDeviceLargeStack();
        }

        private void TileSys_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
            if (_isTileFocused) { HideTileFocus(); return; }
            ShowTileFocus(SystemPanel, TileSys);
            BuildProcessList();
        }

        private void MixerCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void MediaCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void ClipboardCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void ShelfCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void AudioCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
        private void SystemCloseBtn_Click(object sender, RoutedEventArgs e) => HideTileFocus();
    }
}