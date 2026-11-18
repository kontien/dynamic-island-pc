using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIslandPC.Wpf.Models;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf.Services
{
    public class SettingsStore
    {
        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandPC");
        private static readonly string StatePath = Path.Combine(Dir, "state.json");
        private static readonly string ImagesDir = Path.Combine(Dir, "clipboard-images");
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public PersistedState Load()
        {
            try
            {
                if (!File.Exists(StatePath)) return new PersistedState();
                return JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(StatePath)) ?? new PersistedState();
            }
            catch { return new PersistedState(); }
        }

        public void Save(IEnumerable<string> shelfFiles, IEnumerable<ClipboardItem> clipboard, bool pinned)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                Directory.CreateDirectory(ImagesDir);

                var state = new PersistedState { Pinned = pinned, ShelfFiles = shelfFiles.ToList() };
                var usedImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in clipboard)
                {
                    var p = new PersistedClipboardItem
                    {
                        Type = item.Type.ToString(),
                        Text = item.Text,
                        DisplayText = item.DisplayText,
                        Timestamp = item.Timestamp
                    };
                    if (item.Type == ClipboardItemType.Color && item.Color.HasValue)
                        p.ColorHex = item.Color.Value.ToString();
                    else if (item.Type == ClipboardItemType.Image && item.Image != null)
                    {
                        string file = SaveImage(item.Image);
                        p.ImageFile = file;
                        usedImages.Add(file);
                    }
                    state.Clipboard.Add(p);
                }

                File.WriteAllText(StatePath, JsonSerializer.Serialize(state, JsonOpts));
                CleanupImages(usedImages);
            }
            catch (Exception ex) { Log.Error("[Settings] Save", ex); }
        }

        public ClipboardItem? ToClipboardItem(PersistedClipboardItem p)
        {
            var item = new ClipboardItem
            {
                Text = p.Text,
                DisplayText = p.DisplayText,
                Timestamp = p.Timestamp
            };
            switch (p.Type)
            {
                case "Color":
                    try { item.Color = (Color)ColorConverter.ConvertFromString(p.ColorHex ?? ""); item.Type = ClipboardItemType.Color; }
                    catch { item.Type = ClipboardItemType.Text; }
                    break;
                case "Image":
                    var img = LoadImage(p.ImageFile);
                    if (img == null) return null;
                    item.Image = img;
                    item.Type = ClipboardItemType.Image;
                    break;
                default:
                    item.Type = ClipboardItemType.Text;
                    break;
            }
            return item;
        }

        private string SaveImage(BitmapSource image)
        {
            string name = $"{Guid.NewGuid():N}.png";
            string path = Path.Combine(ImagesDir, name);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var fs = File.Create(path);
            encoder.Save(fs);
            return name;
        }

        private BitmapImage? LoadImage(string? name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string path = Path.Combine(ImagesDir, name);
            if (!File.Exists(path)) return null;
            try
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.UriSource = new Uri(path);
                img.EndInit();
                img.Freeze();
                return img;
            }
            catch { return null; }
        }

        private void CleanupImages(HashSet<string> used)
        {
            try
            {
                foreach (var f in Directory.GetFiles(ImagesDir, "*.png"))
                    if (!used.Contains(Path.GetFileName(f))) File.Delete(f);
            }
            catch { }
        }
    }
}