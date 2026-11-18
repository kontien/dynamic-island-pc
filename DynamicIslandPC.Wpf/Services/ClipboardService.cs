using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIslandPC.Wpf.Infrastructure;
using DynamicIslandPC.Wpf.Models;

namespace DynamicIslandPC.Wpf.Services
{
    public class ClipboardService
    {
        private const int MaxHistory = 50;
        private bool _isRestoring;
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
                if (item == null || IsDuplicate(item)) return;
                History.Insert(0, item);
                if (History.Count > MaxHistory) History.RemoveAt(History.Count - 1);
                HistoryChanged?.Invoke(this, EventArgs.Empty);
                Log.Info($"[Clipboard] добавлено: {item.Type}");
            }
            catch (Exception ex) { Log.Error("[Clipboard] capture", ex); }
        }

        private ClipboardItem? CaptureCurrent()
        {
            if (Clipboard.ContainsImage())
            {
                try
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                        return new ClipboardItem { Type = ClipboardItemType.Image, Image = img, DisplayText = $"Изображение {img.PixelWidth}×{img.PixelHeight}", Timestamp = DateTime.Now };
                }
                catch (Exception ex) { Log.Warn($"[Clipboard] image: {ex.Message}"); }
            }
            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text)) return null;
                var item = new ClipboardItem { Text = text, DisplayText = TruncateText(text, 50), Timestamp = DateTime.Now, Type = ClipboardItemType.Text };
                if (Regex.IsMatch(text.Trim(), @"^#[0-9A-Fa-f]{6}$"))
                {
                    try { item.Color = (Color)ColorConverter.ConvertFromString(text.Trim()); item.Type = ClipboardItemType.Color; }
                    catch { item.Type = ClipboardItemType.Text; }
                }
                return item;
            }
            return null;
        }

        private bool IsDuplicate(ClipboardItem item)
        {
            if (History.Count == 0) return false;
            var last = History[0];
            if (last.Type != item.Type) return false;
            return last.Type switch { ClipboardItemType.Image => false, _ => last.Text == item.Text };
        }

        private static string TruncateText(string text, int maxLength)
        {
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ").Trim();
            return text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text;
        }

        public void RestoreItem(ClipboardItem item)
        {
            try
            {
                _isRestoring = true;
                if (item.Type == ClipboardItemType.Image && item.Image != null)
                    Clipboard.SetImage(item.Image);
                else if (item.Type == ClipboardItemType.Color)
                    Clipboard.SetText(!string.IsNullOrEmpty(item.Text) ? item.Text : new ColorConverter().ConvertToString(item.Color!.Value));
                else if (!string.IsNullOrEmpty(item.Text))
                    Clipboard.SetText(item.Text);
                Log.Info($"[Clipboard] восстановлено: {item.Type}");
            }
            catch (Exception ex) { Log.Error("[Clipboard] restore", ex); }
            finally
            {
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                timer.Tick += (s, e) => { _isRestoring = false; timer.Stop(); };
                timer.Start();
            }
        }

        public void Clear() { History.Clear(); HistoryChanged?.Invoke(this, EventArgs.Empty); }

        public void Remove(ClipboardItem item)
        {
            if (item == null) return;
            if (History.Remove(item))
            {
                Log.Info($"[Clipboard] удалена запись: {item.Type}");
                HistoryChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}