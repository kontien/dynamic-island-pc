using System;
using System.Collections.Generic;

namespace DynamicIslandPC.Wpf.Models
{
    public class PersistedState
    {
        public List<string> ShelfFiles { get; set; } = new();
        public List<PersistedClipboardItem> Clipboard { get; set; } = new();
        public bool Pinned { get; set; }
    }

    public class PersistedClipboardItem
    {
        public string Type { get; set; } = "Text";
        public string? Text { get; set; }
        public string? ColorHex { get; set; }
        public string? ImageFile { get; set; }
        public string DisplayText { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }
}