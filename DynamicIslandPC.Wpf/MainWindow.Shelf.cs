using System;
using System.Collections.Generic;
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
        private bool _shelfDragged;

        private void BuildShelfList()
        {
            if (ShelfStack == null) return;
            ShelfStack.Children.Clear();
            if (_shelf.Files.Count == 0) { ShelfStack.Children.Add(EmptyHint("Пуста")); return; }

            foreach (string file in _shelf.Files.Take(4))
            {
                string fileName = IOPath.GetFileName(file);
                string ext = IOPath.GetExtension(file);
                bool isImage = IsImageExt(ext);

                var grid = RowShell(30);
                grid.Cursor = Cursors.Hand;
                AttachShelfDrag(grid, file);

                grid.MouseLeftButtonUp += (s, e) =>
                {
                    if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
                    if (_shelfDragged) return;
                    if (isImage) OpenPreview(file); else ShelfService.Open(file);
                };
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
            if (ShelfLargeGrid == null) return;
            ShelfLargeGrid.Children.Clear();
            PaintShelfChips();

            IEnumerable<string> files = _shelf.Files;
            if (_shelfFilter == "image") files = files.Where(f => IsImageExt(IOPath.GetExtension(f)));
            if (!string.IsNullOrWhiteSpace(_shelfSearchQuery))
                files = files.Where(f => IOPath.GetFileName(f).Contains(_shelfSearchQuery, StringComparison.OrdinalIgnoreCase));

            var list = _shelfSort switch
            {
                "name" => files.OrderBy(f => IOPath.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList(),
                "size" => files.OrderByDescending(ShelfLength).ToList(),
                _ => files.OrderByDescending(ShelfWriteTime).ToList()
            };

            if (list.Count == 0)
            {
                ShelfLargeGrid.Children.Add(EmptyHint(_shelf.Files.Count == 0
                    ? "Перетащите файлы на остров" : "Ничего не найдено"));
                return;
            }

            foreach (string file in list)
            {
                string fileName = IOPath.GetFileName(file);
                string ext = IOPath.GetExtension(file);
                bool isImage = IsImageExt(ext);
                bool exists = File.Exists(file);

                var card = new Border
                {
                    Width = 110,
                    Height = 122,
                    Margin = new Thickness(4),
                    Background = (SolidColorBrush)FindResource("CardBg"),
                    BorderBrush = (SolidColorBrush)FindResource("Hair"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Cursor = Cursors.Hand,
                    ClipToBounds = true,
                    Opacity = exists ? 1 : 0.55,
                    ToolTip = file + (exists ? "" : "  (файл недоступен)")
                };
                card.MouseEnter += (s, e) =>
                {
                    card.BorderBrush = (SolidColorBrush)FindResource("HairHover");
                    card.Background = (SolidColorBrush)FindResource("CardBgHover");
                };
                card.MouseLeave += (s, e) =>
                {
                    card.BorderBrush = (SolidColorBrush)FindResource("Hair");
                    card.Background = (SolidColorBrush)FindResource("CardBg");
                };

                var root = new Grid();
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var preview = new Border
                {
                    Height = 76,
                    Background = new SolidColorBrush(Color.FromArgb(0x55, 0x2A, 0x2A, 0x31)),
                    ClipToBounds = true
                };
                if (isImage && exists)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(file);
                        bi.DecodePixelWidth = 120;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bi.EndInit();
                        bi.Freeze();
                        preview.Child = new Image { Source = bi, Stretch = Stretch.UniformToFill };
                    }
                    catch { preview.Child = CenterIcon("🖼", 34); }
                }
                else preview.Child = CenterIcon(ShelfService.GetFileIcon(ext), 40);
                Grid.SetRow(preview, 0);
                root.Children.Add(preview);

                var meta = new StackPanel { Margin = new Thickness(6, 5, 6, 6) };
                meta.Children.Add(new TextBlock
                {
                    Text = Truncate(fileName, 15),
                    FontSize = 10,
                    Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                meta.Children.Add(new TextBlock
                {
                    Text = ShelfSizeText(ShelfLength(file)),
                    FontSize = 8.5,
                    Foreground = (SolidColorBrush)FindResource("TextFaint"),
                    Margin = new Thickness(0, 1, 0, 0)
                });
                Grid.SetRow(meta, 1);
                root.Children.Add(meta);

                var remove = new Button
                {
                    Content = "✕",
                    Width = 20,
                    Height = 20,
                    FontSize = 9,
                    Opacity = 0,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 4, 4, 0),
                    Style = (Style)FindResource("IconButton"),
                    Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x2B, 0x2B, 0x31)),
                    Foreground = (SolidColorBrush)FindResource("Coral"),
                    ToolTip = "Убрать с полки"
                };
                remove.Click += (s, e) => { e.Handled = true; _shelf.Remove(file); };
                Grid.SetRow(remove, 0);
                root.Children.Add(remove);

                card.MouseEnter += (s, e) => remove.Opacity = 1;
                card.MouseLeave += (s, e) => remove.Opacity = 0;

                card.Child = root;
                AttachShelfDrag(card, file);
                card.MouseLeftButtonUp += (s, e) =>
                {
                    if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
                    if (_shelfDragged) return;
                    if (isImage) OpenPreview(file); else ShelfService.Open(file);
                };
                card.MouseRightButtonUp += (s, e) => { ShelfService.OpenFolder(file); e.Handled = true; };

                ShelfLargeGrid.Children.Add(card);
            }
        }

        private void PaintShelfChips()
        {
            if (ShelfLargeGrid == null) return;
            var host = LogicalTreeHelper.GetParent(ShelfLargeGrid);
            while (host != null && host is not Grid) host = LogicalTreeHelper.GetParent(host);
            if (host is not Grid g) return;

            foreach (var sp in g.Children.OfType<StackPanel>())
            {
                var buttons = sp.Children.OfType<Button>().ToList();
                if (buttons.Count == 0 || buttons.Any(b => b.Tag is not string)) continue;

                foreach (var b in buttons)
                {
                    if (b.Tag is not string tag) continue;
                    bool on = tag == "image" ? _shelfFilter == "image" : _shelfSort == tag;
                    b.Background = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("CardBg");
                    b.Foreground = on ? new SolidColorBrush(Color.FromRgb(6, 20, 28)) : (SolidColorBrush)FindResource("TextPrimary");
                    b.BorderBrush = on ? (SolidColorBrush)FindResource("AccentColor") : (SolidColorBrush)FindResource("Hair");
                }
            }
        }

        private void AttachShelfDrag(FrameworkElement el, string file)
        {
            el.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
                _dragStartPoint = e.GetPosition(null);
                _shelfDragged = false;
            };
            el.MouseMove += (s, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _isDraggingOut || _shelfDragged) return;
                if (IsSourceButton(e.OriginalSource as DependencyObject)) return;
                Vector diff = _dragStartPoint - e.GetPosition(null);
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _shelfDragged = true;
                    _isDraggingOut = true;
                    try { DragDrop.DoDragDrop(el, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy); }
                    finally { _isDraggingOut = false; }
                }
            };
        }

        private static long ShelfLength(string f)
        { try { return File.Exists(f) ? new FileInfo(f).Length : 0; } catch { return 0; } }

        private static DateTime ShelfWriteTime(string f)
        { try { return File.Exists(f) ? File.GetLastWriteTime(f) : DateTime.MinValue; } catch { return DateTime.MinValue; } }

        private static string ShelfSizeText(long b) =>
            b >= 1_000_000 ? (b / 1_000_000.0).ToString("0.0") + " МБ" : Math.Max(1, b / 1000) + " КБ";

        private void OpenPreview(string file)
        {
            try
            {
                var win = new ImagePreviewWindow(file) { Owner = this };
                win.ShowDialog();
                if (win.Deleted) _shelf.Remove(file);
            }
            catch (Exception ex) { Log.Error("[Preview]", ex); }
        }

        private static bool IsImageExt(string ext) =>
            ext.Equals(".png", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);

        private void ShelfSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _shelfSearchQuery = ShelfSearchBox.Text;
            BuildShelfLargeList();
        }

        private void SortShelf_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button b || b.Tag is not string tag) return;
            _shelfSort = tag;
            BuildShelfLargeList();
        }

        private void FilterShelf_Click(object sender, RoutedEventArgs e)
        {
            _shelfFilter = _shelfFilter == "image" ? "all" : "image";
            BuildShelfLargeList();
        }
    }
}