using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SD = System.Drawing;
using SDI = System.Drawing.Imaging;

namespace DynamicIslandPC.Wpf.Services
{
    public static class ScreenshotService
    {
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DynamicIslandPC", "screenshots");

        public static BitmapSource CaptureRegion(Rect r)
        {
            int x = (int)Math.Round(r.X), y = (int)Math.Round(r.Y);
            int w = Math.Max(1, (int)Math.Round(r.Width)), h = Math.Max(1, (int)Math.Round(r.Height));
            using var bmp = new SD.Bitmap(w, h, SDI.PixelFormat.Format32bppArgb);
            using (var g = SD.Graphics.FromImage(bmp))
                g.CopyFromScreen(x, y, 0, 0, new SD.Size(w, h), SD.CopyPixelOperation.SourceCopy);
            return ToSource(bmp);
        }

        public static BitmapSource? Grab(int cx, int cy, int size, out string hex)
        {
            hex = "#000000";
            int half = size / 2;
            try
            {
                using var bmp = new SD.Bitmap(size, size, SDI.PixelFormat.Format32bppArgb);
                using (var g = SD.Graphics.FromImage(bmp))
                    g.CopyFromScreen(cx - half, cy - half, 0, 0, new SD.Size(size, size), SD.CopyPixelOperation.SourceCopy);
                var c = bmp.GetPixel(half, half);
                hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                return ToSource(bmp);
            }
            catch { return null; }
        }

        public static string SavePng(BitmapSource image)
        {
            Directory.CreateDirectory(Folder);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(Folder, $"Screenshot_{stamp}.png");
            int i = 1;
            while (File.Exists(path)) path = Path.Combine(Folder, $"Screenshot_{stamp}_{i++}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            using (var fs = File.Create(path)) enc.Save(fs);
            return path;
        }

        private static BitmapSource ToSource(SD.Bitmap bmp)
        {
            IntPtr h = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DeleteObject(h); }
        }
    }
}