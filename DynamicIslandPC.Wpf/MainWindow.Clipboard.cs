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

        private void BuildClipboardList()
        {
            ClipboardStack.Children.Clear();
            if (_clipboard.History.Count == 0) { ClipboardStack.Children.Add(EmptyHint("Пусто")); return; }
            foreach (var item in _clipboard.History)
            {
                var grid = RowShell(30);
                grid.Cursor = Cursors.Hand; grid.ToolTip = "Клик — вставить снова";
                if (item.Type == ClipboardItemType.Color && item.Color.HasValue)
                    grid.Children.Add(new Border { Background = new SolidColorBrush(item.Color.Value), Width = 16, Height = 16, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                else if (item.Type == ClipboardItemType.Image && item.Image != null)
                    grid.Children.Add(new Image { Source = item.Image, Width = 20, Height = 20, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                else
                    grid.Children.Add(CenterIcon(item.Type == ClipboardItemType.Text ? "\uD83D\uDCDD" : "\uD83D\uDCCB", 20));
                grid.Children.Add(RowText(item.DisplayText, 1, 10));
                var del = RowActionBtn("\uE74D", "Удалить", () => _clipboard.Remove(item), isDanger: true);
                Grid.SetColumn(del, 2); del.Margin = new Thickness(0, 0, 4, 0); grid.Children.Add(del);
                grid.MouseLeftButtonUp += (s, e) => _clipboard.RestoreItem(item);
                ClipboardStack.Children.Add(grid);
            }
        }

        private void BuildClipboardLargeList()
        {
            ClipboardLargeStack.Children.Clear();
            var items = _clipboard.History.AsEnumerable();
            if (_clipboardFilter != "all")
            {
                items = items.Where(i => _clipboardFilter switch
                {
                    "text" => i.Type == ClipboardItemType.Text,
                    "image" => i.Type == ClipboardItemType.Image,
                    "color" => i.Type == ClipboardItemType.Color,
                    "url" => i.DisplayText.StartsWith("http", StringComparison.OrdinalIgnoreCase),
                    _ => true
                });
            }
            if (!string.IsNullOrEmpty(_clipboardSearchQuery))
                items = items.Where(i => i.DisplayText.Contains(_clipboardSearchQuery, StringComparison.OrdinalIgnoreCase));
            var list = items.ToList();
            if (list.Count == 0) { ClipboardLargeStack.Children.Add(EmptyHint("Ничего не найдено")); return; }
            foreach (var item in list)
            {
                var grid = RowShell(36);
                grid.Cursor = Cursors.Hand; grid.ToolTip = "Клик — вставить снова";
                if (item.Type == ClipboardItemType.Color && item.Color.HasValue)
                    grid.Children.Add(new Border { Background = new SolidColorBrush(item.Color.Value), Width = 20, Height = 20, CornerRadius = new CornerRadius(5), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                else if (item.Type == ClipboardItemType.Image && item.Image != null)
                    grid.Children.Add(new Image { Source = item.Image, Width = 24, Height = 24, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
                else
                    grid.Children.Add(CenterIcon(item.Type == ClipboardItemType.Text ? "\uD83D\uDCDD" : "\uD83D\uDCCB", 22));
                grid.Children.Add(RowText(item.DisplayText, 1, 11));
                var del = RowActionBtn("\uE74D", "Удалить", () => _clipboard.Remove(item), isDanger: true);
                Grid.SetColumn(del, 2); del.Margin = new Thickness(0, 0, 4, 0); grid.Children.Add(del);
                grid.MouseLeftButtonUp += (s, e) => _clipboard.RestoreItem(item);
                ClipboardLargeStack.Children.Add(grid);
            }
        }

        private void ClipboardSearchBox_TextChanged(object sender, TextChangedEventArgs e) { _clipboardSearchQuery = ClipboardSearchBox.Text; BuildClipboardLargeList(); }
        private void FilterClip_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is string tag) { _clipboardFilter = tag; BuildClipboardLargeList(); } }
    }
}