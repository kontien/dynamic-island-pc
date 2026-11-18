using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private static Grid RowShell(double height)
        {
            var g = new Grid { Height = height, Margin = new Thickness(0, 0, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var normal = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
            var hover = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            g.Background = normal;
            g.MouseEnter += (s, e) => g.Background = hover;
            g.MouseLeave += (s, e) => g.Background = normal;
            return g;
        }

        private static TextBlock RowText(string text, int col, int fontSize)
        {
            var t = new TextBlock { Text = text, FontSize = fontSize, Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(t, col);
            return t;
        }

        private static TextBlock EmptyHint(string text) => new TextBlock { Text = text, FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(139, 151, 168)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 0, 8) };

        private static TextBlock CenterIcon(string glyph, int size) => new TextBlock { Text = glyph, FontSize = size * 0.6, FontFamily = new FontFamily("Segoe UI Emoji"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        private Button RowActionBtn(string glyph, string tooltip, Action onClick, bool isDanger = false)
        {
            var b = new Button { Style = (Style)FindResource("IconButton"), Content = glyph, FontSize = 9, Width = 22, Height = 22, ToolTip = tooltip, Foreground = isDanger ? new SolidColorBrush(Color.FromRgb(255, 138, 148)) : (SolidColorBrush)FindResource("TextSecondary") };
            b.Click += (s, e) => { e.Handled = true; onClick(); };
            return b;
        }

        private static bool IsSourceButton(DependencyObject? d)
        { while (d != null) { if (d is Button) return true; d = VisualTreeHelper.GetParent(d); } return false; }

        private static string Truncate(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

        // КАСКАДНАЯ АНИМАЦИЯ ПОЯВЛЕНИЯ ЭЛЕМЕНТОВ
        public void AnimateStaggered(Panel panel, TimeSpan startDelay = default)
        {
            if (startDelay == default) startDelay = TimeSpan.Zero;
            int delayMs = 0;
            foreach (UIElement child in panel.Children)
            {
                child.Opacity = 0;
                child.RenderTransform = new TranslateTransform(0, 10);

                var sb = new Storyboard();
                var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
                {
                    BeginTime = startDelay + TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))
                {
                    BeginTime = startDelay + TimeSpan.FromMilliseconds(delayMs),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(fade, child);
                Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
                Storyboard.SetTarget(slide, child);
                Storyboard.SetTargetProperty(slide, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                sb.Children.Add(fade);
                sb.Children.Add(slide);
                sb.Begin();

                delayMs += 40;
            }
        }
    }
}