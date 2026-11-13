using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using DynamicIslandPC.Wpf.Helpers;

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
                if (File.Exists(file) && !Files.Contains(file))
                {
                    Files.Add(file);
                    added = true;
                }
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
                ClipboardHelper.RetryAction(() =>
                {
                    var collection = new StringCollection { filePath };
                    Clipboard.SetFileDropList(collection);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shelf] Copy failed: {ex.Message}");
            }
        }

        public static void Open(string filePath)
        {
            try { if (File.Exists(filePath)) Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true }); }
            catch (Exception ex) { Console.WriteLine($"[Shelf] Open failed: {ex.Message}"); }
        }

        public static void OpenFolder(string filePath)
        {
            try { if (File.Exists(filePath)) Process.Start("explorer.exe", $"/select,\"{filePath}\""); }
            catch (Exception ex) { Console.WriteLine($"[Shelf] OpenFolder failed: {ex.Message}"); }
        }

        public static string GetFileIcon(string extension)
        {
            return extension.ToUpperInvariant().TrimStart('.') switch
            {
                "PDF" => "📄",
                "DOC" or "DOCX" => "📝",
                "XLS" or "XLSX" => "📊",
                "JPG" or "JPEG" or "PNG" or "GIF" or "WEBP" or "BMP" => "🖼️",
                "MP3" or "WAV" or "FLAC" or "OGG" or "M4A" => "🎵",
                "MP4" or "AVI" or "MKV" or "MOV" or "WEBM" => "🎬",
                "ZIP" or "RAR" or "7Z" or "TAR" or "GZ" => "📦",
                "TXT" or "MD" or "LOG" or "CSV" => "📃",
                "EXE" or "MSI" or "BAT" or "PS1" => "⚙️",
                "CS" or "CPP" or "PY" or "JS" or "TS" or "JSON" or "XML" or "HTML" or "CSS" => "💻",
                _ => "📎"
            };
        }
    }
}