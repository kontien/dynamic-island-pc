using DynamicIslandPC.Wpf.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace DynamicIslandPC.Wpf
{
    public partial class MainWindow
    {
        private bool _nightLightOn;
        private bool _gammaModified;
        private int _brightnessIndex;
        private static readonly int[] BrightnessLevels = { 100, 75, 50, 30 };
        private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _cpuPrev = new();

        [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr hDC, IntPtr lpRamp);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);

        private void UpdateSystemUi((double CpuPercent, double RamPercent, double GpuPercent, ulong UsedRamMb, ulong TotalRamMb) data)
        {
            BigCpuValue.Text = $"{(int)data.CpuPercent}%";
            BigRamValue.Text = $"{(int)data.RamPercent}%";
            BigGpuValue.Text = $"{(int)data.GpuPercent}%";
            SetRingValue(CpuRing, data.CpuPercent);
            SetRingValue(RamRing, data.RamPercent);
            SetRingValue(GpuRing, data.GpuPercent);
            SysCpuText.Text = $"{(int)data.CpuPercent}%";
            SysRamText.Text = $"{(int)data.RamPercent}%";
        }

        private void SetRingValue(Ellipse ring, double percent)
        {
            const double circumference = 2 * Math.PI * 24 / 4;
            double clamped = Math.Max(0, Math.Min(100, percent));
            ring.StrokeDashOffset = (1 - clamped / 100.0) * circumference;
        }

        private void BuildProcessList()
        {
            if (CpuProcessStack == null || RamProcessStack == null) return;
            CpuProcessStack.Children.Clear();
            RamProcessStack.Children.Clear();

            try
            {
                var now = DateTime.UtcNow;
                int cores = Math.Max(1, Environment.ProcessorCount);
                var rows = new List<(string Name, double Cpu, long Ram)>();
                var seen = new HashSet<int>();

                foreach (var p in Process.GetProcesses())
                {
                    int pid;
                    try { pid = p.Id; } catch { p.Dispose(); continue; }
                    if (pid == 0) { p.Dispose(); continue; }

                    string name; TimeSpan cpu; long ram;
                    try { name = p.ProcessName; cpu = p.TotalProcessorTime; ram = p.WorkingSet64; }
                    catch { p.Dispose(); continue; }
                    p.Dispose();

                    if (string.IsNullOrEmpty(name)) continue;

                    double percent = 0;
                    if (_cpuPrev.TryGetValue(pid, out var prev))
                    {
                        double wall = (now - prev.At).TotalMilliseconds;
                        if (wall > 120)
                            percent = Math.Max(0, Math.Min(100, (cpu - prev.Cpu).TotalMilliseconds / wall / cores * 100.0));
                    }
                    _cpuPrev[pid] = (cpu, now);

                    if (!seen.Add(pid)) continue;
                    rows.Add((name, percent, ram));
                }

                foreach (int dead in _cpuPrev.Keys.Except(seen).ToList()) _cpuPrev.Remove(dead);

                var byCpu = rows.OrderByDescending(r => r.Cpu).ThenByDescending(r => r.Ram).Take(6).ToList();
                var byRam = rows.OrderByDescending(r => r.Ram).Take(6).ToList();

                foreach (var r in byCpu) CpuProcessStack.Children.Add(ProcessRow(r.Name, $"{r.Cpu:0.0}%", r.Cpu, "Azure"));
                foreach (var r in byRam) RamProcessStack.Children.Add(ProcessRow(r.Name, $"{r.Ram / 1024 / 1024} МБ", r.Ram / 16_000_000.0, "Amber"));

                if (byCpu.Count == 0) CpuProcessStack.Children.Add(EmptyHint("Нет данных"));
                if (byRam.Count == 0) RamProcessStack.Children.Add(EmptyHint("Нет данных"));
            }
            catch (Exception ex) { Log.Error("[ProcessList]", ex); }
        }

        private Grid ProcessRow(string name, string value, double percent, string brushKey)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4), Height = 22 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = Truncate(name, 16),
                FontSize = 10.5,
                Foreground = (SolidColorBrush)FindResource("TextSecondary"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var track = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = (SolidColorBrush)FindResource("Track"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
                ClipToBounds = true
            };
            var fill = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(2, Math.Min(1, percent / 100.0) * 42),
                Background = (SolidColorBrush)FindResource(brushKey)
            };
            track.Child = fill;
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var val = new TextBlock
            {
                Text = value,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = (SolidColorBrush)FindResource(brushKey),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 48,
                TextAlignment = TextAlignment.Right
            };
            Grid.SetColumn(val, 2);
            row.Children.Add(val);
            return row;
        }

        private void FreeRam_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long before;
                using (var self = Process.GetCurrentProcess()) { self.Refresh(); before = self.WorkingSet64; }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                try { using var self = Process.GetCurrentProcess(); EmptyWorkingSet(self.Handle); } catch { }

                long after;
                using (var self = Process.GetCurrentProcess()) { self.Refresh(); after = self.WorkingSet64; }

                BuildProcessList();
                ShowScreenshotConfirmation($"RAM: −{Math.Max(0, before - after) / 1024 / 1024} МБ");
            }
            catch (Exception ex) { Log.Error("[FreeRAM]", ex); }
        }

        private void BrightnessBtn_Click(object sender, RoutedEventArgs e)
        {
            _brightnessIndex = (_brightnessIndex + 1) % BrightnessLevels.Length;
            int level = BrightnessLevels[_brightnessIndex];

            bool wmiOk = false;
            try
            {
                using var mc = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null);
                using var moc = mc.GetInstances();
                foreach (ManagementObject mo in moc)
                {
                    using (mo) { mo.InvokeMethod("WmiSetBrightness", new object[] { 1, (byte)level }); }
                    wmiOk = true;
                    break;
                }
            }
            catch (Exception ex) { Log.Warn("[Brightness] WMI: " + ex.Message); }

            if (!wmiOk)
            {
                double k = 0.45 + level / 100.0 * 0.55;
                ApplyGamma(k, k * (_nightLightOn ? 0.86 : 1.0), k * (_nightLightOn ? 0.72 : 1.0));
            }
            ShowScreenshotConfirmation($"Яркость: {level}%");
        }

        private void NightLightBtn_Click(object sender, RoutedEventArgs e)
        {
            _nightLightOn = !_nightLightOn;
            double k = 0.45 + BrightnessLevels[_brightnessIndex] / 100.0 * 0.55;
            if (_nightLightOn) ApplyGamma(k, k * 0.86, k * 0.72);
            else if (_brightnessIndex == 0) RestoreGamma();
            else ApplyGamma(k, k, k);
            ShowScreenshotConfirmation(_nightLightOn ? "Ночной свет включён" : "Ночной свет выключен");
        }

        private void ApplyGamma(double r = 1.0, double g = 1.0, double b = 1.0)
        {
            IntPtr dc = IntPtr.Zero;
            GCHandle handle = default;
            try
            {
                var ramp = new ushort[3 * 256];
                for (int i = 0; i < 256; i++)
                {
                    ramp[i] = (ushort)Math.Min(65535, Math.Max(0, (int)(i * 256 * r)));
                    ramp[256 + i] = (ushort)Math.Min(65535, Math.Max(0, (int)(i * 256 * g)));
                    ramp[512 + i] = (ushort)Math.Min(65535, Math.Max(0, (int)(i * 256 * b)));
                }
                handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
                dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero)
                {
                    SetDeviceGammaRamp(dc, handle.AddrOfPinnedObject());
                    _gammaModified = true;
                }
            }
            catch (Exception ex) { Log.Error("[Gamma]", ex); }
            finally
            {
                if (handle.IsAllocated) handle.Free();
                if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private void RestoreGamma()
        {
            if (!_gammaModified) return;
            _gammaModified = false;
            _nightLightOn = false;
            _brightnessIndex = 0;

            IntPtr dc = IntPtr.Zero;
            GCHandle handle = default;
            try
            {
                var ramp = new ushort[3 * 256];
                for (int i = 0; i < 256; i++)
                {
                    ushort v = (ushort)Math.Min(65535, i * 257);
                    ramp[i] = v; ramp[256 + i] = v; ramp[512 + i] = v;
                }
                handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
                dc = GetDC(IntPtr.Zero);
                if (dc != IntPtr.Zero) SetDeviceGammaRamp(dc, handle.AddrOfPinnedObject());
            }
            catch (Exception ex) { Log.Error("[Gamma restore]", ex); }
            finally
            {
                if (handle.IsAllocated) handle.Free();
                if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            }
        }
    }
}