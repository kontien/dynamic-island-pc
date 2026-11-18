using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIslandPC.Wpf.Infrastructure;
using DynamicIslandPC.Wpf.Services;
using DynamicIslandPC.Wpf.Views;
using IOPath = System.IO.Path;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private string _shelfSearchQuery = "";
        private string _shelfSort = "date";
        private string _shelfFilter = "all";

        private void BuildShelfList()
        {
            ShelfStack.Children.Clear();
            if (_shelf.Files.Count == 0) { ShelfStack.Children.Add(EmptyHint("Пуста")); return; }
            foreach (string file in _shelf.Files)
            {
                string fileName = IOPath.GetFileName(file);
                string ext = IOPath.GetExtension(file);
                bool isImage = IsImageExt(ext);
                var grid = RowShell(30);
                bool dragged = false;
                grid.PreviewMouseLeftButtonDown += (s, e) => { _dragStartPoint = e.GetPosition(null); dragged = false; };
                grid.MouseMove += (s, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed || _isDraggingOut || dragged) return;
                    Vector diff = _dragStartPoint - e.GetPosition(null);
                    if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                    { dragged = true; _isDraggingOut = true; try { DragDrop.DoDragDrop(grid, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy); } finally { _isDraggingOut = false; } }
                };
                grid.MouseLeftButtonUp += (s, e) => { if (dragged) return; if (isImage) OpenPreview(file); else ShelfService.Open(file); };
                grid.MouseRightButtonUp += (s, e) => { ShelfService.OpenFolder(file); e.Handled = true; };
                grid.Children.Add(CenterIcon(ShelfService.GetFileIcon(ext), 22));
                grid.Children.Add(RowText(fileName, 1, 10));
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
                Grid.SetColumn(actions, 2);
                actions.Children.Add(RowActionBtn("\uE768", "Открыть", () => ShelfService.Open(file)));
                actions.Children.Add(RowActionBtn("\uE8B7", "Папка", () => ShelfService.OpenFolder(file)));
                actions.Children.Add(RowActionBtn("\uE74D", "Убрать", () => _shelf.Remove(file), isDanger: true));
                grid.Children.Add(actions);
                ShelfStack.Children.Add(grid);
            }
        }

        private void BuildShelfLargeList()
        {
            ShelfLargeGrid.Children.Clear();
            IEnumerable<string> files = _shelf.Files;
            if (_shelfFilter == "image") files = files.Where(f => IsImageExt(IOPath.GetExtension(f)));
            if (!string.IsNullOrEmpty(_shelfSearchQuery)) files = files.Where(f => IOPath.GetFileName(f).Contains(_shelfSearchQuery, StringComparison.OrdinalIgnoreCase));
            var list = _shelfSort switch
            {
                "name" => files.OrderBy(f => IOPath.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList(),
                "size" => files.OrderByDescending(f => File.Exists(f) ? new FileInfo(f).Length : 0).ToList(),
                _ => files.ToList()
            };
            if (list.Count == 0) { ShelfLargeGrid.Children.Add(EmptyHint("Ничего не найдено")); return; }
            foreach (string file in list)
            {
                string fileName = IOPath.GetFileName(file);
                string ext = IOPath.GetExtension(file);
                bool isImage = IsImageExt(ext);
                var card = new Border
                {
                    Width = 110,
                    Height = 120,
                    Margin = new Thickness(4),
                    Background = (SolidColorBrush)FindResource("CardBg"),
                    BorderBrush = (SolidColorBrush)FindResource("Hair"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Cursor = Cursors.Hand,
                    ClipToBounds = true
                };
                card.MouseEnter += (s, e) => card.BorderBrush = (SolidColorBrush)FindResource("AccentColor");
                card.MouseLeave += (s, e) => card.BorderBrush = (SolidColorBrush)FindResource("Hair");
                var stack = new StackPanel();
                if (isImage && File.Exists(file))
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit(); bi.UriSource = new Uri(file); bi.DecodePixelWidth = 110; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze();
                        stack.Children.Add(new Image { Source = bi, Width = 110, Height = 80, Stretch = Stretch.UniformToFill });
                    }
                    catch { stack.Children.Add(new Border { Width = 110, Height = 80, Background = new SolidColorBrush(Color.FromRgb(51, 65, 90)), Child = new TextBlock { Text = "🖼", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }); }
                }
                else
                {
                    stack.Children.Add(new Border { Width = 110, Height = 80, Background = new SolidColorBrush(Color.FromRgb(51, 65, 90)), Child = new TextBlock { Text = ShelfService.GetFileIcon(ext), FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji") } });
                }
                stack.Children.Add(new TextBlock { Text = Truncate(fileName, 14), FontSize = 9, Foreground = (SolidColorBrush)FindResource("TextPrimary"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                card.Child = stack;
                bool dragged = false;
                card.PreviewMouseLeftButtonDown += (s, e) => { _dragStartPoint = e.GetPosition(null); dragged = false; };
                card.MouseMove += (s, e) =>
                {
                    if (e.LeftButton != MouseButtonState.Pressed || _isDraggingOut || dragged) return;
                    Vector diff = _dragStartPoint - e.GetPosition(null);
                    if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                    { dragged = true; _isDraggingOut = true; try { DragDrop.DoDragDrop(card, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy); } finally { _isDraggingOut = false; } }
                };
                card.MouseLeftButtonUp += (s, e) => { if (dragged) return; if (isImage) OpenPreview(file); else ShelfService.Open(file); };
                card.MouseRightButtonUp += (s, e) => { ShelfService.OpenFolder(file); e.Handled = true; };
                ShelfLargeGrid.Children.Add(card);
            }
        }

        private void OpenPreview(string file)
        {
            try { var win = new ImagePreviewWindow(file) { Owner = this }; win.ShowDialog(); if (win.Deleted) _shelf.Remove(file); }
            catch (Exception ex) { Log.Error("[Preview]", ex); }
        }

        private static bool IsImageExt(string ext) =>
            ext.Equals(".png", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);

        private void ShelfSearchBox_TextChanged(object sender, TextChangedEventArgs e) { _shelfSearchQuery = ShelfSearchBox.Text; BuildShelfLargeList(); }
        private void SortShelf_Click(object sender, RoutedEventArgs e) { if (sender is Button b && b.Tag is string tag) { _shelfSort = tag; BuildShelfLargeList(); } }
        private void FilterShelf_Click(object sender, RoutedEventArgs e) { _shelfFilter = _shelfFilter == "image" ? "all" : "image"; BuildShelfLargeList(); }
    }
}