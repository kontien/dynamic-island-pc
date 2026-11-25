using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private sealed class GlassSettings
        {
            public double IslandOp { get; set; } = 82;
            public double PanelOp { get; set; } = 93;
            public double TileOp { get; set; } = 40;
            public double Stroke { get; set; } = 10;
            public double Radius { get; set; } = 16;
            public double Hover { get; set; } = 103;
            public double Glow { get; set; } = 20;
            public double Hue { get; set; } = 199;
            public double Speed { get; set; } = 100;
            public bool Sheen { get; set; } = true;
            public bool Shadow { get; set; } = true;
            public bool Edge { get; set; } = true;
        }

        private GlassSettings _glass = new();
        private bool _glassReady;
        private readonly Dictionary<Border, (ScaleTransform Scale, DropShadowEffect Glow)> _tileFx = new();

        private static readonly IEasingFunction GlassEase = new CubicEase { EasingMode = EasingMode.EaseOut };

        internal double GlassMs(double ms) => ms * 100.0 / Math.Max(40, _glass.Speed);
        internal double GlassIslandRadius => _glass.Radius;
        internal double GlassTileRadius => Math.Max(3, _glass.Radius - 6);
        internal double GlassPanelRadius => Math.Max(4, _glass.Radius - 4);
        internal Color GlassAccent => HsvToRgb(_glass.Hue, 0.62, 1.0);

        private static string GlassConfigPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandPC");
                return Path.Combine(dir, "glass.json");
            }
        }

        // ==================== ИНИЦИАЛИЗАЦИЯ ====================
        private void InitGlass()
        {
            try
            {
                if (File.Exists(GlassConfigPath))
                {
                    var loaded = JsonSerializer.Deserialize<GlassSettings>(File.ReadAllText(GlassConfigPath));
                    if (loaded != null) _glass = loaded;
                }
            }
            catch (Exception ex) { Log.Warn("[Glass] load: " + ex.Message); }

            ClampGlass();

            GlIsland.Value = _glass.IslandOp;
            GlPanel.Value = _glass.PanelOp;
            GlTile.Value = _glass.TileOp;
            GlStroke.Value = _glass.Stroke;
            GlRadius.Value = _glass.Radius;
            GlHover.Value = _glass.Hover;
            GlGlow.Value = _glass.Glow;
            GlHue.Value = _glass.Hue;
            GlSpeed.Value = _glass.Speed;
            GlSheen.IsChecked = _glass.Sheen;
            GlShadow.IsChecked = _glass.Shadow;
            GlEdge.IsChecked = _glass.Edge;

            GlIsland.ValueChanged += (s, e) => { _glass.IslandOp = e.NewValue; ApplyGlass(); };
            GlPanel.ValueChanged += (s, e) => { _glass.PanelOp = e.NewValue; ApplyGlass(); };
            GlTile.ValueChanged += (s, e) => { _glass.TileOp = e.NewValue; ApplyGlass(); };
            GlStroke.ValueChanged += (s, e) => { _glass.Stroke = e.NewValue; ApplyGlass(); };
            GlRadius.ValueChanged += (s, e) => { _glass.Radius = e.NewValue; ApplyGlass(); };
            GlHover.ValueChanged += (s, e) => { _glass.Hover = e.NewValue; ApplyGlass(); PulsePreview(); };
            GlGlow.ValueChanged += (s, e) => { _glass.Glow = e.NewValue; ApplyGlass(); PulsePreview(); };
            GlHue.ValueChanged += (s, e) => { _glass.Hue = e.NewValue; ApplyGlass(); };
            GlSpeed.ValueChanged += (s, e) => { _glass.Speed = e.NewValue; ApplyGlass(); PulsePreview(); };

            GlSheen.Checked += GlassToggleChanged; GlSheen.Unchecked += GlassToggleChanged;
            GlShadow.Checked += GlassToggleChanged; GlShadow.Unchecked += GlassToggleChanged;
            GlEdge.Checked += GlassToggleChanged; GlEdge.Unchecked += GlassToggleChanged;

            GlReset.Click += (s, e) => { _glass = new GlassSettings(); SyncGlassControls(); ApplyGlass(); PulsePreview(); ShowScreenshotConfirmation("Стекло сброшено к исходному"); };
            GlRand.Click += GlassRandomize;
            GlSysAccent.Click += GlassUseSystemAccent;
            GlExport.Click += GlassExport;

            GlassCloseBtn.Click += (s, e) => HideTileFocus();
            QuickGlass.Click += (s, e) => OpenGlassPanel();
            GlassMenuItem.Click += (s, e) => { if (!_isExpanded) ToggleExpand(); OpenGlassPanel(); };

            AttachTileHover();
            _glassReady = true;
            ApplyGlass();
        }

        private void GlassToggleChanged(object sender, RoutedEventArgs e)
        {
            _glass.Sheen = GlSheen.IsChecked == true;
            _glass.Shadow = GlShadow.IsChecked == true;
            _glass.Edge = GlEdge.IsChecked == true;
            ApplyGlass();
        }

        private void ClampGlass()
        {
            _glass.IslandOp = Math.Min(100, Math.Max(55, _glass.IslandOp));
            _glass.PanelOp = Math.Min(100, Math.Max(70, _glass.PanelOp));
            _glass.TileOp = Math.Min(72, Math.Max(12, _glass.TileOp));
            _glass.Stroke = Math.Min(30, Math.Max(0, _glass.Stroke));
            _glass.Radius = Math.Min(28, Math.Max(0, _glass.Radius));
            _glass.Hover = Math.Min(112, Math.Max(100, _glass.Hover));
            _glass.Glow = Math.Min(44, Math.Max(0, _glass.Glow));
            _glass.Hue = ((_glass.Hue % 360) + 360) % 360;
            _glass.Speed = Math.Min(200, Math.Max(40, _glass.Speed));
        }

        private void SyncGlassControls()
        {
            GlIsland.Value = _glass.IslandOp;
            GlPanel.Value = _glass.PanelOp;
            GlTile.Value = _glass.TileOp;
            GlStroke.Value = _glass.Stroke;
            GlRadius.Value = _glass.Radius;
            GlHover.Value = _glass.Hover;
            GlGlow.Value = _glass.Glow;
            GlHue.Value = _glass.Hue;
            GlSpeed.Value = _glass.Speed;
            GlSheen.IsChecked = _glass.Sheen;
            GlShadow.IsChecked = _glass.Shadow;
            GlEdge.IsChecked = _glass.Edge;
        }

        private void OpenGlassPanel()
        {
            if (_isTileFocused) HideTileFocus();
            ShowTileFocus(GlassPanel, TileQuick);
            PulsePreview();
        }

        // ==================== HOVER ПЛИТОК ====================
        private void AttachTileHover()
        {
            foreach (var tile in GlassTiles())
            {
                var scale = new ScaleTransform(1, 1);
                tile.RenderTransformOrigin = new Point(0.5, 0.5);
                tile.RenderTransform = scale;

                var glow = new DropShadowEffect
                {
                    Color = GlassAccent,
                    BlurRadius = 0,
                    ShadowDepth = 0,
                    Opacity = 0
                };
                tile.Effect = glow;
                _tileFx[tile] = (scale, glow);

                tile.MouseEnter += (s, e) => HoverTile(tile, true);
                tile.MouseLeave += (s, e) => HoverTile(tile, false);
            }
        }

        private Border[] GlassTiles() => new[]
        {
            TileMedia, TileClip, TileVol, TileShelf, TileOut, TileSys, VpnTileBorder, TileQuick
        };

        private void HoverTile(Border tile, bool on)
        {
            if (!_tileFx.TryGetValue(tile, out var fx)) return;
            if (_isTileFocused && on) return;

            double target = on ? _glass.Hover / 100.0 : 1.0;
            double blur = on ? _glass.Glow : 0;
            double op = on && _glass.Glow > 0 ? 0.9 : 0;
            var dur = TimeSpan.FromMilliseconds(GlassMs(on ? 150 : 200));

            fx.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, dur) { EasingFunction = GlassEase });
            fx.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, dur) { EasingFunction = GlassEase });
            fx.Glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(blur, dur) { EasingFunction = GlassEase });
            fx.Glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(op, dur) { EasingFunction = GlassEase });
        }

        private void PulsePreview()
        {
            if (GlPreview == null) return;
            var scale = GlPreview.RenderTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(1, 1);
                GlPreview.RenderTransformOrigin = new Point(0.5, 0.5);
                GlPreview.RenderTransform = scale;
            }
            if (GlPreview.Effect is not DropShadowEffect fx)
            {
                fx = new DropShadowEffect { Color = GlassAccent, ShadowDepth = 0, BlurRadius = 0, Opacity = 0 };
                GlPreview.Effect = fx;
            }
            fx.Color = GlassAccent;

            double up = _glass.Hover / 100.0;
            var d = TimeSpan.FromMilliseconds(GlassMs(180));
            var grow = new DoubleAnimationUsingKeyFrames();
            grow.KeyFrames.Add(new EasingDoubleKeyFrame(up, KeyTime.FromTimeSpan(d), GlassEase));
            grow.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(GlassMs(460))), GlassEase));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow.Clone());

            var glow = new DoubleAnimationUsingKeyFrames();
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(_glass.Glow, KeyTime.FromTimeSpan(d), GlassEase));
            glow.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(GlassMs(460))), GlassEase));
            fx.BeginAnimation(DropShadowEffect.BlurRadiusProperty, glow);
            fx.BeginAnimation(DropShadowEffect.OpacityProperty, glow.Clone());
        }

        // ==================== ПРИМЕНЕНИЕ ====================
        private void ApplyGlass()
        {
            ClampGlass();

            Color accent = GlassAccent;
            Color onAccent = Luminance(accent) > 0.5 ? Color.FromRgb(0x06, 0x18, 0x24) : Colors.White;
            Color light = Mix(accent, Colors.White, 0.34);

            byte islandA = (byte)Math.Round(_glass.IslandOp / 100.0 * 255);
            byte panelA = (byte)Math.Round(_glass.PanelOp / 100.0 * 255);
            byte tileA = (byte)Math.Round(_glass.TileOp / 100.0 * 255);
            byte tileHoverA = (byte)Math.Min(255, Math.Round(_glass.TileOp / 100.0 * 255 * 1.35));
            double stroke = _glass.Stroke / 10.0;

            Color baseTop = Color.FromRgb(0x23, 0x23, 0x29);
            Color baseMid = Color.FromRgb(0x1B, 0x1B, 0x20);
            Color baseBot = Color.FromRgb(0x16, 0x16, 0x19);

            var island = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(islandA, baseTop.R, baseTop.G, baseTop.B), 0),
                    new GradientStop(Color.FromArgb(islandA, baseMid.R, baseMid.G, baseMid.B), 0.55),
                    new GradientStop(Color.FromArgb(islandA, baseBot.R, baseBot.G, baseBot.B), 1)
                }
            };
            island.Freeze();

            SetBrush("IslandBgGrad", island);
            SetBrush("PanelBg", Color.FromArgb(panelA, 0x17, 0x17, 0x1B));
            SetBrush("CardBg", Color.FromArgb(tileA, 0x33, 0x33, 0x3A));
            SetBrush("CardBgHover", Color.FromArgb(tileHoverA, 0x4A, 0x4A, 0x55));
            SetBrush("Scrim", Color.FromArgb((byte)Math.Round(panelA * 0.78), 0x12, 0x12, 0x16));

            SetBrush("AccentColor", accent);
            SetBrush("Azure", accent);
            SetBrush("OnAccent", onAccent);
            SetBrush("Violet", light);
            SetBrush("AccentSoft", Color.FromArgb(0x33, accent.R, accent.G, accent.B));
            Resources["AccentCyan"] = accent;

            var grad = new LinearGradientBrush(accent, light, 0);
            grad.Freeze();
            Resources["AccentGradient"] = grad;

            SetBrush("Hair", Color.FromArgb(0x38, 255, 255, 255));
            SetBrush("HairHover", _glass.Edge
                ? Color.FromArgb(0xD9, accent.R, accent.G, accent.B)
                : Color.FromArgb(0x66, 255, 255, 255));
            SetBrush("Track", Color.FromArgb(0x33, 255, 255, 255));
            SetBrush("RowBg", Color.FromArgb(0x14, 255, 255, 255));
            SetBrush("RowBgHover", Color.FromArgb(0x24, 255, 255, 255));

            var sheen = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, -0.2),
                Center = new Point(0.5, -0.2),
                RadiusX = 0.95,
                RadiusY = 0.8,
                GradientStops =
                {
                    new GradientStop(Color.FromArgb((byte)(_glass.Sheen ? 0x16 : 0), 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }
            };
            sheen.Freeze();
            Resources["Sheen"] = sheen;
            if (SheenLayer != null) SheenLayer.Visibility = _glass.Sheen ? Visibility.Visible : Visibility.Collapsed;

            IslandBorder.BorderThickness = new Thickness(stroke);
            IslandBorder.Effect = _glass.Shadow
                ? new DropShadowEffect { Color = Colors.Black, BlurRadius = 30, ShadowDepth = 10, Opacity = 0.5, Direction = 270 }
                : null;
            IslandBorder.CornerRadius = _isExpanded
                ? new CornerRadius(GlassIslandRadius)
                : new CornerRadius(_isFullScreen ? 10 : 22);

            foreach (var tile in GlassTiles())
            {
                tile.CornerRadius = new CornerRadius(GlassTileRadius);
                tile.BorderThickness = new Thickness(stroke);
                if (_tileFx.TryGetValue(tile, out var fx)) fx.Glow.Color = accent;
            }

            foreach (var panel in new[] { MixerPanel, MediaPanel, ClipboardPanel, ShelfPanel, AudioPanel, SystemPanel, GlassPanel })
            {
                panel.CornerRadius = new CornerRadius(GlassPanelRadius);
                panel.BorderThickness = new Thickness(stroke);
            }

            if (GlPreview != null)
            {
                GlPreview.CornerRadius = new CornerRadius(GlassTileRadius);
                GlPreview.BorderThickness = new Thickness(Math.Max(1, stroke));
            }

            GlIslandV.Text = $"{(int)_glass.IslandOp}%";
            GlPanelV.Text = $"{(int)_glass.PanelOp}%";
            GlTileV.Text = $"{(int)_glass.TileOp}%";
            GlStrokeV.Text = stroke.ToString("0.0", CultureInfo.InvariantCulture);
            GlRadiusV.Text = ((int)_glass.Radius).ToString(CultureInfo.InvariantCulture);
            GlHoverV.Text = $"{(int)_glass.Hover}%";
            GlGlowV.Text = ((int)_glass.Glow).ToString(CultureInfo.InvariantCulture);
            GlHueV.Text = $"{(int)_glass.Hue}°";
            GlSpeedV.Text = (_glass.Speed / 100.0).ToString("0.0", CultureInfo.InvariantCulture) + "×";

            if (!_glassReady) return;

            UpdateVpnUi(_vpnService.IsConnected);
            BuildDeviceStack();
            RefreshQuickToggles();
            RefreshMediaToggles();

            if (_isTileFocused)
            {
                if (MixerPanel.Visibility == Visibility.Visible) BuildAppVolumeList();
                if (ClipboardPanel.Visibility == Visibility.Visible) BuildClipboardLargeList();
                if (ShelfPanel.Visibility == Visibility.Visible) BuildShelfLargeList();
                if (AudioPanel.Visibility == Visibility.Visible) BuildDeviceLargeStack();
                if (MediaPanel.Visibility == Visibility.Visible) UpdateMediaPanel();
            }

            SaveGlass();
        }

        private void SetBrush(string key, Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            Resources[key] = b;
        }

        private void SetBrush(string key, Brush b) => Resources[key] = b;

        private void SaveGlass()
        {
            try
            {
                string path = GlassConfigPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_glass, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log.Warn("[Glass] save: " + ex.Message); }
        }

        // ==================== ДЕЙСТВИЯ ====================
        private void GlassRandomize(object sender, RoutedEventArgs e)
        {
            var r = new Random();
            _glass.IslandOp = r.Next(62, 94);
            _glass.PanelOp = Math.Min(100, _glass.IslandOp + r.Next(6, 16));
            _glass.TileOp = r.Next(22, 62);
            _glass.Stroke = r.Next(4, 22);
            _glass.Radius = r.Next(4, 26);
            _glass.Hover = r.Next(101, 110);
            _glass.Glow = r.Next(8, 40);
            _glass.Hue = r.Next(0, 360);
            _glass.Speed = r.Next(70, 160);
            _glass.Edge = r.Next(0, 4) != 0;
            SyncGlassControls();
            ApplyGlass();
            PulsePreview();
            ShowScreenshotConfirmation("Случайное стекло применено");
        }

        private void GlassUseSystemAccent(object sender, RoutedEventArgs e)
        {
            try
            {
                var v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                if (v is int i)
                {
                    byte r = (byte)(i & 0xFF), g = (byte)((i >> 8) & 0xFF), b = (byte)((i >> 16) & 0xFF);
                    if (r + g + b > 24)
                    {
                        _glass.Hue = RgbToHue(Color.FromRgb(r, g, b));
                        GlHue.Value = _glass.Hue;
                        ApplyGlass();
                        PulsePreview();
                        ShowScreenshotConfirmation("Акцент Windows применён");
                        return;
                    }
                }
                ShowScreenshotConfirmation("Акцент Windows не найден", isError: true);
            }
            catch (Exception ex)
            {
                Log.Warn("[Glass] accent: " + ex.Message);
                ShowScreenshotConfirmation("Акцент Windows недоступен", isError: true);
            }
        }

        private void GlassExport(object sender, RoutedEventArgs e)
        {
            string xaml = BuildGlassXaml();
            bool copied = false, saved = false;

            try { Clipboard.SetText(xaml); copied = true; }
            catch (Exception ex) { Log.Warn("[Glass] clipboard: " + ex.Message); }

            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandPC");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "glass-theme.xaml"), xaml, Encoding.UTF8);
                saved = true;
            }
            catch (Exception ex) { Log.Warn("[Glass] export: " + ex.Message); }

            ShowScreenshotConfirmation(copied && saved
                ? "XAML скопирован и сохранён"
                : copied ? "XAML скопирован в буфер"
                : saved ? "XAML сохранён в LocalAppData"
                : "Не удалось экспортировать", isError: !copied && !saved);
        }

        private string BuildGlassXaml()
        {
            Color a = GlassAccent;
            Color light = Mix(a, Colors.White, 0.34);
            byte ia = (byte)Math.Round(_glass.IslandOp / 100.0 * 255);
            byte pa = (byte)Math.Round(_glass.PanelOp / 100.0 * 255);
            byte ta = (byte)Math.Round(_glass.TileOp / 100.0 * 255);
            byte tha = (byte)Math.Min(255, Math.Round(_glass.TileOp / 100.0 * 255 * 1.35));
            string stroke = (_glass.Stroke / 10.0).ToString("0.0", CultureInfo.InvariantCulture);

            var sb = new StringBuilder();
            sb.AppendLine("<!-- Dynamic Island PC · Smoked Glass -->");
            sb.AppendLine($"<!-- island {_glass.IslandOp:0}% · panels {_glass.PanelOp:0}% · tiles {_glass.TileOp:0}% · hover {_glass.Hover:0}% · glow {_glass.Glow:0} -->");
            sb.AppendLine("<SolidColorBrush x:Key=\"TextPrimary\" Color=\"#FFFFFF\"/>");
            sb.AppendLine("<SolidColorBrush x:Key=\"TextSecondary\" Color=\"#D2D2D6\"/>");
            sb.AppendLine("<SolidColorBrush x:Key=\"TextFaint\" Color=\"#9A9AA2\"/>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"CardBg\" Color=\"{Hex(Color.FromArgb(ta, 0x33, 0x33, 0x3A))}\"/>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"CardBgHover\" Color=\"{Hex(Color.FromArgb(tha, 0x4A, 0x4A, 0x55))}\"/>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"PanelBg\" Color=\"{Hex(Color.FromArgb(pa, 0x17, 0x17, 0x1B))}\"/>");
            sb.AppendLine("<SolidColorBrush x:Key=\"Hair\" Color=\"#38FFFFFF\"/>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"HairHover\" Color=\"{Hex(_glass.Edge ? Color.FromArgb(0xD9, a.R, a.G, a.B) : Color.FromArgb(0x66, 255, 255, 255))}\"/>");
            sb.AppendLine("<SolidColorBrush x:Key=\"Track\" Color=\"#33FFFFFF\"/>");
            sb.AppendLine("<SolidColorBrush x:Key=\"RowBg\" Color=\"#14FFFFFF\"/>");
            sb.AppendLine("<SolidColorBrush x:Key=\"RowBgHover\" Color=\"#24FFFFFF\"/>");
            sb.AppendLine($"<Color x:Key=\"AccentCyan\">{Hex(a)}</Color>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"AccentColor\" Color=\"{Hex(a)}\"/>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"OnAccent\" Color=\"{Hex(Luminance(a) > 0.5 ? Color.FromRgb(6, 24, 36) : Colors.White)}\"/>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"Azure\" Color=\"{Hex(a)}\"/>");
            sb.AppendLine($"<SolidColorBrush x:Key=\"Violet\" Color=\"{Hex(light)}\"/>");
            sb.AppendLine("<LinearGradientBrush x:Key=\"IslandBgGrad\" StartPoint=\"0,0\" EndPoint=\"0,1\">");
            sb.AppendLine($"  <GradientStop Color=\"{Hex(Color.FromArgb(ia, 0x23, 0x23, 0x29))}\" Offset=\"0\"/>");
            sb.AppendLine($"  <GradientStop Color=\"{Hex(Color.FromArgb(ia, 0x1B, 0x1B, 0x20))}\" Offset=\"0.55\"/>");
            sb.AppendLine($"  <GradientStop Color=\"{Hex(Color.FromArgb(ia, 0x16, 0x16, 0x19))}\" Offset=\"1\"/>");
            sb.AppendLine("</LinearGradientBrush>");
            sb.AppendLine($"<!-- CornerRadius: island {GlassIslandRadius:0} · tile {GlassTileRadius:0} · panel {GlassPanelRadius:0} · BorderThickness {stroke} -->");
            sb.AppendLine($"<!-- Hover: ScaleTransform {_glass.Hover / 100.0:0.00} + DropShadowEffect BlurRadius {_glass.Glow:0} Color {Hex(a)} -->");
            sb.AppendLine($"<!-- Motion: {GlassMs(250):0} ms · CubicEase EaseOut -->");
            return sb.ToString();
        }

        // ==================== ЦВЕТ ====================
        private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private static Color Mix(Color a, Color b, double t) =>
            Color.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));

        private static Color HsvToRgb(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60.0) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; }
            else if (h < 120) { r = x; g = c; }
            else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; }
            else if (h < 300) { r = x; b = c; }
            else { r = c; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }

        private static double RgbToHue(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            if (d < 0.0001) return 199;
            double h;
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
            return ((h % 360) + 360) % 360;
        }

        // ==================== КАСКАД ====================
        internal void AnimateStaggered(Panel panel, double startDelay = 60, double step = 28)
        {
            if (panel == null) return;
            int i = 0;
            foreach (UIElement child in panel.Children)
            {
                if (i > 18) { child.Opacity = 1; child.RenderTransform = null; continue; }
                child.Opacity = 0;
                var tt = new TranslateTransform(0, 8);
                child.RenderTransform = tt;
                var begin = TimeSpan.FromMilliseconds(GlassMs(startDelay + i * step));
                child.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(GlassMs(200))) { BeginTime = begin, EasingFunction = GlassEase });
                tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(GlassMs(240))) { BeginTime = begin, EasingFunction = GlassEase });
                i++;
            }
        }
    }
}