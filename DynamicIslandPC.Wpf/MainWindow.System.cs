using DynamicIslandPC.Wpf.Infrastructure;
using System;
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

        [DllImport("gdi32.dll")]
        private static extern bool SetDeviceGammaRamp(IntPtr hDC, IntPtr lpRamp);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

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
            double dashArrayValue = 2 * Math.PI * 24 / 4;
            double clamped = Math.Max(0, Math.Min(100, percent));
            ring.StrokeDashOffset = (1 - clamped / 100.0) * dashArrayValue;
        }

        private void BuildProcessList()
        {
            CpuProcessStack.Children.Clear();
            RamProcessStack.Children.Clear();
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .ToList();

                // Кортеж с явными именами полей
                (string Name, double Cpu, long Ram) GetProcessInfo(Process p)
                {
                    try
                    {
                        return (p.ProcessName, p.TotalProcessorTime.TotalMilliseconds, p.WorkingSet64);
                    }
                    catch
                    {
                        return ("", 0d, 0L);
                    }
                }

                var byCpu = processes
                    .Select(GetProcessInfo)
                    .Where(x => !string.IsNullOrEmpty(x.Name))
                    .OrderByDescending(x => x.Cpu)
                    .Take(5)
                    .ToList();

                var byRam = processes
                    .Select(GetProcessInfo)
                    .Where(x => !string.IsNullOrEmpty(x.Name))
                    .OrderByDescending(x => x.Ram)
                    .Take(5)
                    .ToList();

                foreach (var p in byCpu)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock
                    {
                        Text = Truncate(p.Name, 18),
                        FontSize = 10,
                        Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var val = new TextBlock
                    {
                        Text = $"{(int)p.Cpu}ms",
                        FontSize = 9,
                        Foreground = (SolidColorBrush)FindResource("Azure"),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(val, 1);
                    row.Children.Add(val);
                    CpuProcessStack.Children.Add(row);
                }

                foreach (var p in byRam)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new TextBlock
                    {
                        Text = Truncate(p.Name, 18),
                        FontSize = 10,
                        Foreground = (SolidColorBrush)FindResource("TextPrimary"),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    var val = new TextBlock
                    {
                        Text = $"{p.Ram / 1024 / 1024}MB",
                        FontSize = 9,
                        Foreground = (SolidColorBrush)FindResource("Amber"),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(8, 0, 0, 0)
                    };
                    Grid.SetColumn(val, 1);
                    row.Children.Add(val);
                    RamProcessStack.Children.Add(row);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[ProcessList]", ex);
            }
        }

        private void FreeRam_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long before = GC.GetTotalMemory(false);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long after = GC.GetTotalMemory(false);
                DispatchToUi(() => ShowScreenshotConfirmation($"Освобождено: {(before - after) / 1024}KB"));
            }
            catch (Exception ex)
            {
                Log.Error("[FreeRAM]", ex);
            }
        }

        private void BrightnessBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var mc = new ManagementClass("root\\WMI", "WmiMonitorBrightnessMethods", null);
                using var moc = mc.GetInstances();
                foreach (ManagementObject mo in moc)
                {
                    mo.InvokeMethod("WmiSetBrightness", new object[] { 100, 70 });
                    break;
                }
                DispatchToUi(() => ShowScreenshotConfirmation("Яркость: 70%"));
            }
            catch (Exception ex)
            {
                Log.Warn("[Brightness] WMI failed: " + ex.Message);
                ApplyGamma(0.85);
                DispatchToUi(() => ShowScreenshotConfirmation("Яркость снижена (гамма)"));
            }
        }

        private void NightLightBtn_Click(object sender, RoutedEventArgs e)
        {
            _nightLightOn = !_nightLightOn;
            if (_nightLightOn)
                ApplyGamma(1.0, 0.85, 0.7);
            else
                ApplyGamma(1.0);
            DispatchToUi(() => ShowScreenshotConfirmation(_nightLightOn ? "Ночной свет включён" : "Ночной свет выключен"));
        }

        private void ApplyGamma(double r = 1.0, double g = 1.0, double b = 1.0)
        {
            try
            {
                var ramp = new short[3 * 256];
                for (int i = 0; i < 256; i++)
                {
                    ramp[i] = (short)Math.Min(65535, Math.Max(0, (int)(i * 256 * r)));
                    ramp[256 + i] = (short)Math.Min(65535, Math.Max(0, (int)(i * 256 * g)));
                    ramp[512 + i] = (short)Math.Min(65535, Math.Max(0, (int)(i * 256 * b)));
                }
                var handle = GCHandle.Alloc(ramp, GCHandleType.Pinned);
                try
                {
                    SetDeviceGammaRamp(GetDC(IntPtr.Zero), handle.AddrOfPinnedObject());
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                Log.Error("[Gamma]", ex);
            }
        }
    }
}