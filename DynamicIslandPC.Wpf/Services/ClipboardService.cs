using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIslandPC.Wpf.Helpers;
using DynamicIslandPC.Wpf.Models;

namespace DynamicIslandPC.Wpf.Services
{
    public class ClipboardService
    {
        private const int MaxHistory = 50;
        private bool _isRestoring = false;
        private DateTime _lastCaptureTime = DateTime.MinValue;

        public List<ClipboardItem> History { get; } = new();
        public event EventHandler? HistoryChanged;

        public void OnClipboardChanged()
        {
            if (_isRestoring) return;
            var now = DateTime.UtcNow;
            if ((now - _lastCaptureTime).TotalMilliseconds < 300) return;
            _lastCaptureTime = now;

            try
            {
                var item = CaptureCurrent();
                if (item != null && !IsDuplicate(item))
                {
                    History.Insert(0, item);
                    if (History.Count > MaxHistory)
                        History.RemoveAt(History.Count - 1);
                    HistoryChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Clipboard] Capture error: {ex.Message}");
            }
        }

        private ClipboardItem? CaptureCurrent()
        {
            if (Clipboard.ContainsImage())
            {
                try
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        // ОПТИМИЗАЦИЯ ПАМЯТИ: сжимаем до 300px и замораживаем
                        var resized = ResizeImage(img, 300);
                        return new ClipboardItem
                        {
                            Type = ClipboardItemType.Image,
                            Image = resized,
                            DisplayText = $"Изображение {resized.PixelWidth}×{resized.PixelHeight}",
                            Timestamp = DateTime.Now
                        };
                    }
                }
                catch { }
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return null;

                var item = new ClipboardItem
                {
                    Text = text,
                    DisplayText = TruncateText(text, 50),
                    Timestamp = DateTime.Now
                };

                if (Regex.IsMatch(text.Trim(), @"^#[0-9A-Fa-f]{6}$"))
                {
                    item.Type = ClipboardItemType.Color;
                    try { item.Color = (Color)ColorConverter.ConvertFromString(text.Trim()); }
                    catch { item.Type = ClipboardItemType.Text; }
                }
                else item.Type = ClipboardItemType.Text;

                return item;
            }

            return null;
        }

        private BitmapSource ResizeImage(BitmapSource source, int maxWidth)
        {
            if (source.PixelWidth <= maxWidth)
            {
                source.Freeze();
                return source;
            }

            double scale = (double)maxWidth / source.PixelWidth;
            var transform = new ScaleTransform(scale, scale);
            var resized = new TransformedBitmap(source, transform);
            resized.Freeze();
            return resized;
        }

        private bool IsDuplicate(ClipboardItem item)
        {
            if (History.Count == 0) return false;
            var last = History[0];
            if (last.Type != item.Type) return false;
            return last.Type switch
            {
                ClipboardItemType.Image => false,
                _ => last.Text == item.Text
            };
        }

        private string TruncateText(string text, int maxLength)
        {
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Trim();
            return text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text;
        }

        public void RestoreItem(ClipboardItem item)
        {
            try
            {
                _isRestoring = true;
                ClipboardHelper.RetryAction(() =>
                {
                    if (item.Type == ClipboardItemType.Image && item.Image != null)
                        Clipboard.SetImage(item.Image);
                    else if (item.Type == ClipboardItemType.Color)
                        Clipboard.SetText(!string.IsNullOrEmpty(item.Text) ? item.Text : new ColorConverter().ConvertToString(item.Color!.Value));
                    else if (!string.IsNullOrEmpty(item.Text))
                        Clipboard.SetText(item.Text);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Clipboard] Restore failed: {ex.Message}");
            }
            finally
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (s, e) => { _isRestoring = false; timer.Stop(); };
                timer.Start();
            }
        }

        public void Clear()
        {
            History.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}