using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Services;

namespace DynamicIslandPC.Wpf.Views
{
    public partial class ImagePreviewWindow : Window
    {
        private readonly string _path;
        private BitmapSource? _image;
        private double _fitScale = 1, _zoom = 1, _offsetX, _offsetY;
        private bool _panning, _closing, _firstLayout = true;
        private Point _panStart;
        private double _panOriginX, _panOriginY;

        private Button _copyBtn = null!, _openBtn = null!, _folderBtn = null!, _deleteBtn = null!, _zoomBtn = null!, _closeBtn = null!;
        private TextBlock _zoomText = null!;

        public bool Deleted { get; private set; }

        private static readonly FontFamily IconFont = new("Segoe MDL2 Assets");

        public ImagePreviewWindow(string path)
        {
            InitializeComponent();
            _path = path;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
            BuildToolbar();
            LoadImage();
        }

        private void BuildToolbar()
        {
            _copyBtn = MakeToolButton("\uE8C8", "Копировать", "C", CopyImage);
            _openBtn = MakeToolButton("\uE8A7", "Открыть", "O", () => ShelfService.Open(_path));
            _folderBtn = MakeToolButton("\uE8B7", "Папка", "F", () => ShelfService.OpenFolder(_path));
            _deleteBtn = MakeToolButton("\uE74D", "Удалить", "Del", DeleteFile, Color.FromRgb(248, 113, 113));
            _zoomBtn = MakeToolButton("\uE740", "Вписать", "0", ResetZoom);
            _closeBtn = MakeToolButton("\uE711", "Закрыть", "Esc", CloseAnimated);

            _zoomText = new TextBlock
            {
                Text = "100%",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 46,
                TextAlignment = TextAlignment.Center
            };
            var zoomPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            };
            zoomPanel.Children.Add(_zoomText);
            zoomPanel.Children.Add(_zoomBtn);

            Toolbar.Children.Add(_copyBtn);
            Toolbar.Children.Add(Sep());
            Toolbar.Children.Add(_openBtn);
            Toolbar.Children.Add(_folderBtn);
            Toolbar.Children.Add(Sep());
            Toolbar.Children.Add(zoomPanel);
            Toolbar.Children.Add(Sep());
            Toolbar.Children.Add(_deleteBtn);
            Toolbar.Children.Add(_closeBtn);
        }

        private static Border Sep() => new Border
        {
            Width = 1,
            Height = 24,
            Background = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)),
            Margin = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        private Button MakeToolButton(string glyph, string label, string hint, Action onClick, Color? fg = null)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = glyph,
                FontFamily = IconFont,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 122, 140)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 1, 0, 0)
            });

            var b = new Button { Style = (Style)FindResource("ToolButton"), Content = sp };
            if (fg.HasValue) b.Foreground = new SolidColorBrush(fg.Value);
            b.Click += (s, e) => onClick();
            return b;
        }

        private void LoadImage()
        {
            FileNameText.Text = Path.GetFileName(_path);
            if (!File.Exists(_path)) { ShowError("Файл не найден"); return; }
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(_path);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.EndInit();
                bi.Freeze();

                _image = bi;
                PreviewImage.Source = bi;

                var fi = new FileInfo(_path);
                FileMetaText.Text = $"{bi.PixelWidth} × {bi.PixelHeight}  ·  {FormatSize(fi.Length)}  ·  " +
                                    fi.LastWriteTime.ToString("d MMM yyyy, HH:mm", new CultureInfo("ru-RU"));
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть изображение: " + ex.Message);
            }
        }

        private void ShowError(string msg)
        {
            ErrorText.Text = msg;
            ErrorText.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
            _copyBtn.IsEnabled = false;
            _openBtn.IsEnabled = false;
            _zoomBtn.IsEnabled = false;
            FileMetaText.Text = "файл недоступен";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Activate();
            Focus();
            Keyboard.Focus(Root);
            Root.Opacity = 0;
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            RecomputeFit();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => RecomputeFit();

        private void RecomputeFit()
        {
            if (_image == null) return;
            double availW = Math.Max(50, Viewport.ActualWidth);
            double availH = Math.Max(50, Viewport.ActualHeight);
            _fitScale = Math.Min(availW / _image.PixelWidth, availH / _image.PixelHeight);
            if (_fitScale <= 0 || double.IsInfinity(_fitScale) || double.IsNaN(_fitScale)) _fitScale = 1;

            if (_firstLayout)
            {
                _firstLayout = false;
                _zoom = 1; _offsetX = 0; _offsetY = 0;
                ImgTranslate.X = 0; ImgTranslate.Y = 0;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var anim = new DoubleAnimation(_fitScale * 0.94, _fitScale, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
                ImgScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
                ImgScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            else
            {
                _zoom = 1; _offsetX = 0; _offsetY = 0;
                ApplyTransform();
            }
            UpdateZoomText();
        }

        private void ApplyTransform()
        {
            ImgScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ImgScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            double s = _fitScale * _zoom;
            ImgScale.ScaleX = s;
            ImgScale.ScaleY = s;
            ImgTranslate.X = _offsetX;
            ImgTranslate.Y = _offsetY;
            UpdateZoomText();
        }

        private void UpdateZoomText() => _zoomText.Text = $"{(int)Math.Round(_zoom * 100)}%";

        private void ResetZoom()
        {
            _zoom = 1; _offsetX = 0; _offsetY = 0;
            ApplyTransform();
        }

        private void ZoomBy(double f)
        {
            if (_image == null) return;
            _zoom = Math.Clamp(_zoom * f, 0.2, 12);
            ApplyTransform();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_image == null) return;
            double oldScale = _fitScale * _zoom;
            if (oldScale <= 0) return;
            double factor = e.Delta > 0 ? 1.18 : 1 / 1.18;
            double newZoom = Math.Clamp(_zoom * factor, 0.2, 12);
            double newScale = _fitScale * newZoom;

            Point p = e.GetPosition(Viewport);
            double cx = p.X - Viewport.ActualWidth / 2;
            double cy = p.Y - Viewport.ActualHeight / 2;
            double ix = (cx - _offsetX) / oldScale;
            double iy = (cy - _offsetY) / oldScale;

            _zoom = newZoom;
            _offsetX = cx - ix * newScale;
            _offsetY = cy - iy * newScale;
            ApplyTransform();
            e.Handled = true;
        }

        private void Viewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_image == null) return;
            if (e.ClickCount == 2)
            {
                if (Math.Abs(_zoom - 1) < 0.01) _zoom = 2;
                else { _zoom = 1; _offsetX = 0; _offsetY = 0; }
                ApplyTransform();
                e.Handled = true;
                return;
            }
            if (_zoom > 1.001)
            {
                _panning = true;
                _panStart = e.GetPosition(Viewport);
                _panOriginX = _offsetX;
                _panOriginY = _offsetY;
                Viewport.CaptureMouse();
                Viewport.Cursor = Cursors.SizeAll;
            }
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(Viewport);
            _offsetX = _panOriginX + (p.X - _panStart.X);
            _offsetY = _panOriginY + (p.Y - _panStart.Y);
            ApplyTransform();
        }

        private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_panning) return;
            _panning = false;
            Viewport.ReleaseMouseCapture();
            Viewport.Cursor = Cursors.Arrow;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape: CloseAnimated(); e.Handled = true; break;
                case Key.C: CopyImage(); e.Handled = true; break;
                case Key.O: ShelfService.Open(_path); e.Handled = true; break;
                case Key.F: ShelfService.OpenFolder(_path); e.Handled = true; break;
                case Key.Delete: DeleteFile(); e.Handled = true; break;
                case Key.D0:
                case Key.NumPad0: ResetZoom(); e.Handled = true; break;
                case Key.OemPlus:
                case Key.Add: ZoomBy(1.25); e.Handled = true; break;
                case Key.OemMinus:
                case Key.Subtract: ZoomBy(1 / 1.25); e.Handled = true; break;
            }
        }

        private void CopyImage()
        {
            if (_image == null) return;
            try
            {
                Clipboard.SetImage(_image);
                FlashButton(_copyBtn, "\uE73E", "Скопировано", Color.FromRgb(52, 211, 153));
            }
            catch
            {
                FlashButton(_copyBtn, "\uE711", "Ошибка", Color.FromRgb(248, 113, 113));
            }
        }

        private void DeleteFile()
        {
            Deleted = true;
            CloseAnimated();
        }

        private void FlashButton(Button b, string glyph, string label, Color color)
        {
            if (b.Content is not StackPanel sp || sp.Children.Count < 2) return;
            if (sp.Children[0] is not TextBlock icon || sp.Children[1] is not TextBlock text) return;

            string oldGlyph = icon.Text, oldLabel = text.Text;
            var oldFg = b.Foreground;
            icon.Text = glyph;
            text.Text = label;
            b.Foreground = new SolidColorBrush(color);

            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                icon.Text = oldGlyph;
                text.Text = oldLabel;
                b.Foreground = oldFg;
            };
            t.Start();
        }

        private void CloseAnimated()
        {
            if (_closing) return;
            _closing = true;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (s, e) => Close();
            Root.BeginAnimation(OpacityProperty, fade);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} Б";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} КБ";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} МБ";
            return $"{bytes / (1024.0 * 1024 * 1024):0.##} ГБ";
        }
    }
}