using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DynamicIslandPC.Wpf.Views
{
    public partial class ScreenshotOverlayWindow : Window
    {
        private enum Mode
        {
            Idle, Drawing, Moving,
            ResizeTL, ResizeTR, ResizeBL, ResizeBR,
            ResizeT, ResizeB, ResizeL, ResizeR
        }

        private readonly BitmapSource _full;
        private Mode _mode = Mode.Idle;
        private Point _dragStart;
        private Rect _selection;
        private Point _moveAnchor;
        private Rect _moveStart;
        private bool _hasSelection;

        public Rect? SelectedRegion { get; private set; }
        public BitmapSource? ResultImage { get; private set; }



        public ScreenshotOverlayWindow(BitmapSource fullScreenCapture)
        {
            InitializeComponent();
            _full = fullScreenCapture;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            FrozenImage.Width = Width;
            FrozenImage.Height = Height;
            FrozenImage.Source = _full;

            UpdateDims();
            Activate();
            Focus();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
            else if (e.Key == Key.Enter && _hasSelection) { Confirm(); e.Handled = true; }
        }

        // ==================== МЫШЬ ====================
        private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (IsInsideActionPanel(e.OriginalSource as DependencyObject)) return;

            var pos = e.GetPosition(Root);

            if (_hasSelection && TryHitHandle(pos, out var handleMode))
            {
                _mode = handleMode;
                _dragStart = pos;
                Root.CaptureMouse();
                return;
            }

            if (_hasSelection && _selection.Contains(pos))
            {
                _mode = Mode.Moving;
                _moveAnchor = pos;
                _moveStart = _selection;
                Root.CaptureMouse();
                Root.Cursor = Cursors.SizeAll;
                return;
            }

            _mode = Mode.Drawing;
            _dragStart = pos;
            _selection = new Rect(pos, new Size(0, 0));
            _hasSelection = false;
            SelectionRect.Visibility = Visibility.Visible;
            SelectionInner.Visibility = Visibility.Visible;
            HandlesCanvas.Visibility = Visibility.Collapsed;
            ActionPanel.Visibility = Visibility.Collapsed;
            Root.CaptureMouse();
        }

        private void Root_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(Root);

            UpdateCrosshair(pos);
            UpdateMagnifier(pos);

            if (_mode == Mode.Idle) return;

            if (_mode == Mode.Drawing)
            {
                _selection = Normalize(_dragStart, pos);
                UpdateSelectionVisuals();
            }
            else if (_mode == Mode.Moving)
            {
                double dx = pos.X - _moveAnchor.X;
                double dy = pos.Y - _moveAnchor.Y;
                double nx = Math.Clamp(_moveStart.X + dx, 0, Math.Max(0, Width - _moveStart.Width));
                double ny = Math.Clamp(_moveStart.Y + dy, 0, Math.Max(0, Height - _moveStart.Height));
                _selection = new Rect(nx, ny, _moveStart.Width, _moveStart.Height);
                UpdateSelectionVisuals();
            }
            else
            {
                ResizeByMode(pos);
                UpdateSelectionVisuals();
            }
        }

        private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mode == Mode.Idle) return;
            Root.ReleaseMouseCapture();
            Root.Cursor = Cursors.Cross;

            if (_mode == Mode.Drawing)
            {
                if (_selection.Width >= 4 && _selection.Height >= 4)
                {
                    _hasSelection = true;
                    HandlesCanvas.Visibility = Visibility.Visible;
                    ActionPanel.Visibility = Visibility.Visible;
                    HelpPanel.Visibility = Visibility.Collapsed;
                    MagnifierBorder.Visibility = Visibility.Collapsed;
                    CrossH.Visibility = Visibility.Collapsed;
                    CrossV.Visibility = Visibility.Collapsed;
                    UpdateSelectionVisuals();
                }
                else
                {
                    SelectionRect.Visibility = Visibility.Collapsed;
                    SelectionInner.Visibility = Visibility.Collapsed;
                    _hasSelection = false;
                    UpdateDims();
                }
            }
            else
            {
                UpdateSelectionVisuals();
            }

            _mode = Mode.Idle;
        }

        private void Root_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            Cancel();
            e.Handled = true;
        }

        private static bool IsInsideActionPanel(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is StackPanel sp && sp.Name == "ActionPanel") return true;
                if (d is Button) return true;
                d = VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        // ==================== ГЕОМЕТРИЯ ====================
        private static Rect Normalize(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X);
            double y = Math.Min(a.Y, b.Y);
            return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        private void ResizeByMode(Point pos)
        {
            double l = _selection.Left, t = _selection.Top;
            double r = _selection.Right, b = _selection.Bottom;

            switch (_mode)
            {
                case Mode.ResizeTL: l = pos.X; t = pos.Y; break;
                case Mode.ResizeTR: r = pos.X; t = pos.Y; break;
                case Mode.ResizeBL: l = pos.X; b = pos.Y; break;
                case Mode.ResizeBR: r = pos.X; b = pos.Y; break;
                case Mode.ResizeT: t = pos.Y; break;
                case Mode.ResizeB: b = pos.Y; break;
                case Mode.ResizeL: l = pos.X; break;
                case Mode.ResizeR: r = pos.X; break;
            }

            if (l > r) { var tmp = l; l = r; r = tmp; }
            if (t > b) { var tmp = t; t = b; b = tmp; }

            l = Math.Max(0, l); t = Math.Max(0, t);
            r = Math.Min(Width, r); b = Math.Min(Height, b);
            if (r - l < 2) r = l + 2;
            if (b - t < 2) b = t + 2;

            _selection = new Rect(l, t, r - l, b - t);
        }

        private bool TryHitHandle(Point pos, out Mode mode)
        {
            const double hit = 8;
            (Mode m, Point p)[] handles =
            {
                (Mode.ResizeTL, new Point(_selection.Left, _selection.Top)),
                (Mode.ResizeTR, new Point(_selection.Right, _selection.Top)),
                (Mode.ResizeBL, new Point(_selection.Left, _selection.Bottom)),
                (Mode.ResizeBR, new Point(_selection.Right, _selection.Bottom)),
                (Mode.ResizeT,  new Point((_selection.Left + _selection.Right) / 2, _selection.Top)),
                (Mode.ResizeB,  new Point((_selection.Left + _selection.Right) / 2, _selection.Bottom)),
                (Mode.ResizeL,  new Point(_selection.Left, (_selection.Top + _selection.Bottom) / 2)),
                (Mode.ResizeR,  new Point(_selection.Right, (_selection.Top + _selection.Bottom) / 2)),
            };
            foreach (var (m, p) in handles)
            {
                double dx = pos.X - p.X, dy = pos.Y - p.Y;
                if (dx * dx + dy * dy <= hit * hit) { mode = m; return true; }
            }
            mode = Mode.Idle;
            return false;
        }

        private void UpdateSelectionVisuals()
        {
            SetRect(SelectionRect, _selection);
            SetRect(SelectionInner, new Rect(_selection.X + 1, _selection.Y + 1,
                Math.Max(0, _selection.Width - 2), Math.Max(0, _selection.Height - 2)));

            PlaceHandle(HandleTL, _selection.Left, _selection.Top);
            PlaceHandle(HandleTR, _selection.Right, _selection.Top);
            PlaceHandle(HandleBL, _selection.Left, _selection.Bottom);
            PlaceHandle(HandleBR, _selection.Right, _selection.Bottom);
            PlaceHandle(HandleT, (_selection.Left + _selection.Right) / 2, _selection.Top);
            PlaceHandle(HandleB, (_selection.Left + _selection.Right) / 2, _selection.Bottom);
            PlaceHandle(HandleL, _selection.Left, (_selection.Top + _selection.Bottom) / 2);
            PlaceHandle(HandleR, _selection.Right, (_selection.Top + _selection.Bottom) / 2);

            int px = (int)(_selection.X + SystemParameters.VirtualScreenLeft);
            int py = (int)(_selection.Y + SystemParameters.VirtualScreenTop);
            SizeText.Text = $"{(int)_selection.Width} × {(int)_selection.Height} px";
            CoordsText.Text = $"@ {px}, {py}";
            InfoPanel.Visibility = Visibility.Visible;

            double infoX = Math.Clamp(_selection.Left, 4, Width - 210);
            double infoY = _selection.Top - 32;
            if (infoY < 4) infoY = _selection.Bottom + 6;
            Canvas.SetLeft(InfoPanel, infoX);
            Canvas.SetTop(InfoPanel, infoY);

            ActionPanel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double aw = ActionPanel.DesiredSize.Width;
            double ax = Math.Clamp(_selection.Right - aw, 4, Width - aw - 4);
            double ay = _selection.Bottom + 8;
            if (ay + 40 > Height) ay = Math.Max(4, _selection.Top - 44);
            Canvas.SetLeft(ActionPanel, ax);
            Canvas.SetTop(ActionPanel, ay);

            UpdateDims();
        }

        private void UpdateDims()
        {
            Rect hole = (_hasSelection || _mode != Mode.Idle) ? _selection : new Rect(0, 0, 0, 0);
            double l = Math.Max(0, hole.X);
            double t = Math.Max(0, hole.Y);
            double r = Math.Min(Width, hole.X + hole.Width);
            double b = Math.Min(Height, hole.Y + hole.Height);

            SetRectRaw(DimTop, 0, 0, Width, t);
            SetRectRaw(DimBottom, 0, b, Width, Math.Max(0, Height - b));
            SetRectRaw(DimLeft, 0, t, l, Math.Max(0, b - t));
            SetRectRaw(DimRight, r, t, Math.Max(0, Width - r), Math.Max(0, b - t));
        }

        private static void SetRect(FrameworkElement el, Rect r)
        {
            Canvas.SetLeft(el, r.X);
            Canvas.SetTop(el, r.Y);
            el.Width = r.Width;
            el.Height = r.Height;
        }

        private static void SetRectRaw(FrameworkElement el, double x, double y, double w, double h)
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            el.Width = Math.Max(0, w);
            el.Height = Math.Max(0, h);
        }

        private static void PlaceHandle(Ellipse h, double cx, double cy)
        {
            Canvas.SetLeft(h, cx - 5);
            Canvas.SetTop(h, cy - 5);
        }

        // ==================== ПЕРЕКРЕСТИЕ И ЛУПА ====================
        private void UpdateCrosshair(Point pos)
        {
            if (_hasSelection && _mode == Mode.Idle)
            {
                CrossH.Visibility = Visibility.Collapsed;
                CrossV.Visibility = Visibility.Collapsed;
                return;
            }
            CrossH.Visibility = Visibility.Visible;
            CrossV.Visibility = Visibility.Visible;
            CrossH.X1 = 0; CrossH.Y1 = pos.Y; CrossH.X2 = Width; CrossH.Y2 = pos.Y;
            CrossV.X1 = pos.X; CrossV.Y1 = 0; CrossV.X2 = pos.X; CrossV.Y2 = Height;
        }

        private void UpdateMagnifier(Point pos)
        {
            if (_hasSelection && _mode == Mode.Idle)
            {
                MagnifierBorder.Visibility = Visibility.Collapsed;
                return;
            }
            if (_full.PixelWidth < 12 || _full.PixelHeight < 12)
            {
                MagnifierBorder.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                int px = (int)pos.X, py = (int)pos.Y;
                int cx = Math.Clamp(px - 6, 0, _full.PixelWidth - 12);
                int cy = Math.Clamp(py - 6, 0, _full.PixelHeight - 12);

                MagnifierImage.Source = new CroppedBitmap(_full, new Int32Rect(cx, cy, 12, 12));

                int sx = Math.Clamp(px, 0, _full.PixelWidth - 1);
                int sy = Math.Clamp(py, 0, _full.PixelHeight - 1);
                var one = new FormatConvertedBitmap(
                    new CroppedBitmap(_full, new Int32Rect(sx, sy, 1, 1)), PixelFormats.Bgra32, null, 0);
                var buf = new byte[4];
                one.CopyPixels(buf, 4, 0);
                MagnifierText.Text = $"#{buf[2]:X2}{buf[1]:X2}{buf[0]:X2}  RGB({buf[2]},{buf[1]},{buf[0]})";

                double mx = pos.X + 20, my = pos.Y + 20;
                if (mx + 122 > Width) mx = pos.X - 142;
                if (my + 146 > Height) my = pos.Y - 166;
                Canvas.SetLeft(MagnifierBorder, Math.Max(4, mx));
                Canvas.SetTop(MagnifierBorder, Math.Max(4, my));
                MagnifierBorder.Visibility = Visibility.Visible;
            }
            catch
            {
                MagnifierBorder.Visibility = Visibility.Collapsed;
            }
        }

        // ==================== ИТОГ ====================
        private void ConfirmBtn_Click(object sender, RoutedEventArgs e) => Confirm();
        private void CancelBtn_Click(object sender, RoutedEventArgs e) => Cancel();

        private void Confirm()
        {
            if (!_hasSelection) { Cancel(); return; }

            int x = (int)Math.Round(_selection.X);
            int y = (int)Math.Round(_selection.Y);
            int w = (int)Math.Round(_selection.Width);
            int h = (int)Math.Round(_selection.Height);

            x = Math.Clamp(x, 0, _full.PixelWidth - 1);
            y = Math.Clamp(y, 0, _full.PixelHeight - 1);
            w = Math.Min(w, _full.PixelWidth - x);
            h = Math.Min(h, _full.PixelHeight - y);
            if (w < 1 || h < 1) { Cancel(); return; }

            ResultImage = new CroppedBitmap(_full, new Int32Rect(x, y, w, h));
            ResultImage.Freeze();
            SelectedRegion = new Rect(
                x + SystemParameters.VirtualScreenLeft,
                y + SystemParameters.VirtualScreenTop,
                w, h);

            DialogResult = true;
            Close();
        }

        private void Cancel()
        {
            ResultImage = null;
            SelectedRegion = null;
            DialogResult = false;
            Close();
        }
    }
}