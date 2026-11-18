using System;
using System.Diagnostics;
using System.IO;

namespace DynamicIslandPC.Wpf.Infrastructure
{
    public static class Log
    {
        private static readonly object _gate = new();
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DynamicIslandPC", "logs");

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARN", msg);
        public static void Error(string msg, Exception? ex = null) =>
            Write("ERROR", ex == null ? msg : $"{msg} | {ex}");

        private static void Write(string level, string msg)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
            Debug.WriteLine(line);
            try
            {
                lock (_gate)
                {
                    Directory.CreateDirectory(LogDir);
                    File.AppendAllText(Path.Combine(LogDir, $"{DateTime.Now:yyyy-MM-dd}.log"), line + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}