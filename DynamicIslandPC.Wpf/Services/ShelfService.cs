using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf.Services
{
    public class ShelfService
    {
        public List<string> Files { get; } = new();
        public event EventHandler? Changed;

        public void AddFiles(string[] files)
        {
            bool added = false;
            foreach (string file in files)
            {
                if (!File.Exists(file) || Files.Contains(file)) continue;
                Files.Add(file);
                added = true;
                Log.Info($"[Shelf] добавлен: {file}");
            }
            if (added) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Remove(string file)
        {
            if (Files.Remove(file)) Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            Files.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public static void CopyToClipboard(string filePath)
        {
            try
            {
                var collection = new StringCollection { filePath };
                Clipboard.SetFileDropList(collection);
            }
            catch (Exception ex) { Log.Error("[Shelf] copy", ex); }
        }

        public static void Open(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
            }
            catch (Exception ex) { Log.Error("[Shelf] open", ex); }
        }

        public static void OpenFolder(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex) { Log.Error("[Shelf] openFolder", ex); }
        }

        public static string GetFileIcon(string extension) => extension.ToUpperInvariant().TrimStart('.') switch
        {
            "PDF" or "DOC" or "DOCX" or "TXT" or "MD" or "RTF" => "📄",
            "XLS" or "XLSX" or "CSV" => "📊",
            "JPG" or "JPEG" or "PNG" or "GIF" or "WEBP" or "BMP" or "SVG" => "🖼️",
            "MP3" or "WAV" or "FLAC" or "OGG" or "M4A" => "🎵",
            "MP4" or "AVI" or "MKV" or "MOV" or "WEBM" => "🎬",
            "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" => "📦",
            "EXE" or "MSI" or "BAT" or "PS1" => "⚙️",
            "CS" or "CPP" or "PY" or "JS" or "TS" or "JSON" or "XML" or "HTML" or "CSS" => "💻",
            _ => "📎"
        };
    }
}