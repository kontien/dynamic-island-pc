using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIslandPC.Wpf.Models
{
    public enum ClipboardItemType
    {
        Text,
        Image,
        Color
    }

    public class ClipboardItem
    {
        public ClipboardItemType Type { get; set; }
        public string? Text { get; set; }
        public BitmapSource? Image { get; set; }
        public Color? Color { get; set; }
        public string DisplayText { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}