using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private static Grid RowShell(double height)
        {
            var g = new Grid { Height = height, Margin = new Thickness(0, 0, 0, 3), Background = Brushes.Transparent };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var host = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x00, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };
            Grid.SetColumn(host, 0);
            Grid.SetColumnSpan(host, 3);
            g.Children.Add(host);

            g.MouseEnter += (s, e) =>
            {
                host.Background = new SolidColorBrush(Color.FromArgb(0x24, 255, 255, 255));
                host.BorderBrush = new SolidColorBrush(Color.FromArgb(0x2E, 255, 255, 255));
            };
            g.MouseLeave += (s, e) =>
            {
                host.Background = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
                host.BorderBrush = new SolidColorBrush(Color.FromArgb(0x00, 255, 255, 255));
            };
            return g;
        }

        private static TextBlock RowText(string text, int col, int fontSize)
        {
            var t = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(Colors.White),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 6, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(t, col);
            return t;
        }

        private static TextBlock EmptyHint(string text) => new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xA2)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 14)
        };

        private static TextBlock CenterIcon(string glyph, int size) => new TextBlock
        {
            Text = glyph,
            FontSize = size * 0.62,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        private Button RowActionBtn(string glyph, string tooltip, Action onClick, bool isDanger = false)
        {
            var b = new Button
            {
                Style = (Style)FindResource("IconButton"),
                Content = glyph,
                FontSize = 10,
                Width = 24,
                Height = 24,
                ToolTip = tooltip,
                Foreground = isDanger
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0xA4))
                    : (SolidColorBrush)FindResource("TextSecondary")
            };
            b.Click += (s, e) => { e.Handled = true; onClick(); };
            return b;
        }

        private static bool IsSourceButton(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is Button || d is Slider || d is TextBox || d is CheckBox) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max - 1) + "…");

        private static string FormatBytes(long b) =>
            b >= 1_000_000 ? (b / 1_000_000.0).ToString("0.0") + " МБ" : Math.Max(1, b / 1000) + " КБ";
    }
}