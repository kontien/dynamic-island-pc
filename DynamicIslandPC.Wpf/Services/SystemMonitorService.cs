using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DynamicIslandPC.Wpf.Infrastructure;

namespace DynamicIslandPC.Wpf.Services
{
    public class SystemMonitorService : IDisposable
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength; public uint dwMemoryLoad;
            public ulong ullTotalPhys; public ulong ullAvailPhys;
            public ulong ullTotalPageFile; public ulong ullAvailPageFile;
            public ulong ullTotalVirtual; public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQueryW(IntPtr src, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern int PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint fmt, ref uint size, out uint count, IntPtr buffer);
        [DllImport("pdh.dll")] private static extern int PdhCloseQuery(IntPtr query);

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

        [StructLayout(LayoutKind.Sequential)] private struct PDH_VALUE { public uint CStatus; public double Value; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct PDH_ITEM { public IntPtr Name; public PDH_VALUE Fmt; }

        private IntPtr _gpuQuery = IntPtr.Zero, _gpuCounter = IntPtr.Zero;
        private bool _gpuAvailable, _pollErrorLogged;
        private PerformanceCounter? _cpuCounter;

        public event EventHandler<(double CpuPercent, double RamPercent, double GpuPercent, ulong UsedRamMb, ulong TotalRamMb)>? Updated;

        public SystemMonitorService()
        {
            try { _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total"); _cpuCounter.NextValue(); }
            catch (Exception ex) { Log.Error("[SysMon] CPU", ex); }
            InitGpu();
        }

        private void InitGpu()
        {
            try
            {
                if (PdhOpenQueryW(IntPtr.Zero, IntPtr.Zero, out _gpuQuery) != 0) { _gpuQuery = IntPtr.Zero; return; }
                int st = PdhAddEnglishCounterW(_gpuQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpuCounter);
                if (st != 0)
                {
                    Log.Warn($"[SysMon] GPU-счётчик недоступен (0x{st:X8})");
                    PdhCloseQuery(_gpuQuery); _gpuQuery = IntPtr.Zero; _gpuCounter = IntPtr.Zero; return;
                }
                PdhCollectQueryData(_gpuQuery);
                _gpuAvailable = true;
                Log.Info("[SysMon] GPU-счётчики подключены");
            }
            catch (Exception ex) { Log.Error("[SysMon] GPU init", ex); _gpuAvailable = false; }
        }

        private double ReadGpu()
        {
            if (!_gpuAvailable) return 0;
            try
            {
                if (PdhCollectQueryData(_gpuQuery) != 0) return 0;
                uint size = 0, count = 0;
                if (PdhGetFormattedCounterArrayW(_gpuCounter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero) != PDH_MORE_DATA || size == 0) return 0;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArrayW(_gpuCounter, PDH_FMT_DOUBLE, ref size, out count, buf) != 0) return 0;
                    var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    int itemSize = Marshal.SizeOf<PDH_ITEM>();
                    for (int i = 0; i < count; i++)
                    {
                        var item = Marshal.PtrToStructure<PDH_ITEM>(IntPtr.Add(buf, i * itemSize));
                        if (item.Fmt.CStatus != 0) continue;
                        string name = Marshal.PtrToStringUni(item.Name) ?? "";
                        int idx = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                        string engine = idx >= 0 ? name.Substring(idx + 8) : "other";
                        perEngine.TryGetValue(engine, out double cur); perEngine[engine] = cur + item.Fmt.Value;
                    }
                    double max = 0;
                    foreach (var v in perEngine.Values) if (v > max) max = v;
                    return Math.Clamp(max, 0, 100);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { return 0; }
        }

        public void Poll()
        {
            try
            {
                double cpu = Math.Round(_cpuCounter?.NextValue() ?? 0, 1);
                var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                GlobalMemoryStatusEx(ref mem);
                ulong totalMb = mem.ullTotalPhys / 1024 / 1024, availMb = mem.ullAvailPhys / 1024 / 1024;
                ulong usedMb = totalMb - availMb;
                double ram = totalMb > 0 ? (double)usedMb / totalMb * 100.0 : 0;
                double gpu = Math.Round(ReadGpu(), 1);
                Updated?.Invoke(this, (cpu, Math.Round(ram, 1), gpu, usedMb, totalMb));
                _pollErrorLogged = false;
            }
            catch (Exception ex)
            {
                if (_pollErrorLogged) return;
                _pollErrorLogged = true;
                Log.Error("[SysMon] Poll", ex);
            }
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose(); _cpuCounter = null;
            if (_gpuQuery != IntPtr.Zero) { PdhCloseQuery(_gpuQuery); _gpuQuery = IntPtr.Zero; }
            _gpuCounter = IntPtr.Zero; _gpuAvailable = false;
        }
    }
}