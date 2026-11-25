using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIslandPC.Wpf.Models;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private string _clipboardFilter = "all";
        private string _clipboardSearchQuery = "";

        private static bool LooksLikeUrl(string s) =>
            !string.IsNullOrWhiteSpace(s) && s.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase);

        private void BuildClipboardList()
        {
            if (ClipboardStack == null) return;
            ClipboardStack.Children.Clear();
            if (_clipboard.History.Count == 0) { ClipboardStack.Children.Add(EmptyHint("Пусто")); return; }

            foreach (var item in _clipboard.History.Take(4))
            {
                var grid = RowShell(30);
                grid.Cursor = Cursors.Hand;
                grid.ToolTip = "Клик — вставить снова";
                grid.Children.Add(ClipIcon(item.Type, item.Color, item.Image, item.DisplayText, 20));
                grid.Children.Add(RowText(item.DisplayText, 1, 10));

                var captured = item;
                var del = RowActionBtn("\uE74D", "Удалить", () => _clipboard.Remove(captured), isDanger: true);
                Grid.SetColumn(del, 2);
                del.Margin = new Thickness(0, 0, 6, 0);
                grid.Children.Add(del);

                grid.MouseLeftButtonUp += (s, e) =>
                {
                    if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
                    _clipboard.RestoreItem(captured);
                };
                ClipboardStack.Children.Add(grid);
            }
        }

        private void BuildClipboardLargeList()
        {
            if (ClipboardLargeStack == null) return;
            ClipboardLargeStack.Children.Clear();
            RefreshClipChips();

            var items = _clipboard.History.AsEnumerable();
            if (_clipboardFilter != "all")
            {
                items = items.Where(i => _clipboardFilter switch
                {
                    "text" => i.Type == ClipboardItemType.Text,
                    "image" => i.Type == ClipboardItemType.Image,
                    "color" => i.Type == ClipboardItemType.Color,
                    "url" => i.Type == ClipboardItemType.Text && LooksLikeUrl(i.DisplayText),
                    _ => true
                });
            }
            if (!string.IsNullOrWhiteSpace(_clipboardSearchQuery))
                items = items.Where(i => i.DisplayText.Contains(_clipboardSearchQuery, StringComparison.OrdinalIgnoreCase));

            var list = items.ToList();
            if (list.Count == 0)
            {
                ClipboardLargeStack.Children.Add(EmptyHint(_clipboard.History.Count == 0 ? "Буфер пуст" : "Ничего не найдено"));
                return;
            }

            foreach (var item in list)
            {
                var grid = RowShell(38);
                grid.Cursor = Cursors.Hand;
                grid.ToolTip = "Клик — вставить снова";
                grid.Children.Add(ClipIcon(item.Type, item.Color, item.Image, item.DisplayText, 24));

                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 6, 0) };
                text.Children.Add(new TextBlock
                {
                    Text = item.DisplayText,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                text.Children.Add(new TextBlock
                {
                    Text = ClipKind(item.Type, item.DisplayText),
                    FontSize = 9.5,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    Margin = new Thickness(0, 1, 0, 0)
                });
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);

                var captured = item;
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
                actions.Children.Add(RowActionBtn("\uE8C8", "Копировать снова", () => _clipboard.RestoreItem(captured)));
                actions.Children.Add(RowActionBtn("\uE74D", "Удалить", () => _clipboard.Remove(captured), isDanger: true));
                Grid.SetColumn(actions, 2);
                grid.Children.Add(actions);

                grid.MouseLeftButtonUp += (s, e) =>
                {
                    if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
                    _clipboard.RestoreItem(captured);
                };
                ClipboardLargeStack.Children.Add(grid);
            }

            ClipboardLargeStack.Children.Add(new TextBlock
            {
                Text = $"{list.Count} из {_clipboard.History.Count} записей",
                FontSize = 10,
                Foreground = (SolidColorBrush)FindResource("TextFaint"),
                Margin = new Thickness(0, 10, 0, 4)
            });
        }

        private static string ClipKind(ClipboardItemType type, string display)
        {
            if (type == ClipboardItemType.Color) return "ЦВЕТ";
            if (type == ClipboardItemType.Image) return "ИЗОБРАЖЕНИЕ";
            return LooksLikeUrl(display) ? "ССЫЛКА" : "ТЕКСТ";
        }

        private static UIElement ClipIcon(ClipboardItemType type, Color? color, ImageSource? image, string display, double size)
        {
            if (type == ClipboardItemType.Color && color.HasValue)
                return new Border
                {
                    Background = new SolidColorBrush(color.Value),
                    Width = size * 0.8,
                    Height = size * 0.8,
                    CornerRadius = new CornerRadius(5),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

            if (type == ClipboardItemType.Image && image != null)
                return new Image
                {
                    Source = image,
                    Width = size,
                    Height = size,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

            string glyph = LooksLikeUrl(display) ? "🔗" : (type == ClipboardItemType.Text ? "📝" : "📋");
            return CenterIcon(glyph, (int)size);
        }

        private void RefreshClipChips()
        {
            if (ClipChips == null) return;
            foreach (var b in ClipChips.Children.OfType<Button>())
            {
                bool on = b.Tag is string t && t == _clipboardFilter;
                b.Background = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("CardBg");
                b.Foreground = on ? (SolidColorBrush)FindResource("OnAccent") : (SolidColorBrush)FindResource("TextPrimary");
                b.BorderBrush = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("Hair");
            }
        }

        private void ClipboardSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _clipboardSearchQuery = ClipboardSearchBox.Text;
            BuildClipboardLargeList();
        }

        private void FilterClip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag) return;
            _clipboardFilter = tag;
            BuildClipboardLargeList();
            AnimateStaggered(ClipboardLargeStack, 0, 22);
        }
    }
}