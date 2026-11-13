using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DynamicIslandPC.Wpf.Services
{
    public class SystemMonitorService : IDisposable
    {
        // ---------- RAM ----------
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        // ---------- GPU через PDH (English-имена → работает на любой локали Windows) ----------
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhOpenQueryW(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhAddEnglishCounterW(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        [DllImport("pdh.dll")]
        private static extern int PdhCollectQueryData(IntPtr hQuery);

        [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
        private static extern int PdhGetFormattedCounterArrayW(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, out uint lpdwItemCount, IntPtr ItemBuffer);

        [DllImport("pdh.dll")]
        private static extern int PdhCloseQuery(IntPtr hQuery);

        private const uint PDH_FMT_DOUBLE = 0x00000200;
        private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE
        {
            public uint CStatus;
            public double doubleValue;   // union, выравнивание 8 байт
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PDH_FMT_COUNTERVALUE_ITEM_W
        {
            public IntPtr szName;
            public PDH_FMT_COUNTERVALUE FmtValue;
        }

        private IntPtr _gpuQuery = IntPtr.Zero;
        private IntPtr _gpuCounter = IntPtr.Zero;
        private bool _gpuAvailable;

        private PerformanceCounter? _cpuCounter;

        public event EventHandler<(double CpuPercent, double RamPercent, double GpuPercent, ulong UsedRamMb, ulong TotalRamMb)>? Updated;

        public SystemMonitorService()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // первое чтение всегда 0
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] CPU init failed: {ex.Message}");
            }

            InitGpu();
        }

        private void InitGpu()
        {
            try
            {
                if (PdhOpenQueryW(IntPtr.Zero, IntPtr.Zero, out _gpuQuery) != 0)
                {
                    _gpuQuery = IntPtr.Zero;
                    return;
                }

                int st = PdhAddEnglishCounterW(_gpuQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpuCounter);
                if (st != 0)
                {
                    Debug.WriteLine($"[SystemMonitor] GPU-счётчик недоступен (0x{st:X8}). Нужен драйвер WDDM 2.0+.");
                    PdhCloseQuery(_gpuQuery);
                    _gpuQuery = IntPtr.Zero;
                    _gpuCounter = IntPtr.Zero;
                    return;
                }

                PdhCollectQueryData(_gpuQuery); // прогрев — первый сэмпл пустой
                _gpuAvailable = true;
                Debug.WriteLine("[SystemMonitor] GPU-счётчики подключены");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] GPU init failed: {ex.Message}");
                _gpuAvailable = false;
            }
        }

        private double ReadGpu()
        {
            if (!_gpuAvailable) return 0;
            try
            {
                if (PdhCollectQueryData(_gpuQuery) != 0) return 0;

                uint size = 0, count = 0;
                int status = PdhGetFormattedCounterArrayW(_gpuCounter, PDH_FMT_DOUBLE, ref size, out count, IntPtr.Zero);
                if (status != PDH_MORE_DATA || size == 0) return 0;

                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    status = PdhGetFormattedCounterArrayW(_gpuCounter, PDH_FMT_DOUBLE, ref size, out count, buf);
                    if (status != 0) return 0;

                    // Инстансы вида: pid_1234_luid_0x..._phys_0_eng_0_engtype_3D
                    // Суммируем внутри каждого типа движка и берём максимум —
                    // так же считает общую загрузку Диспетчер задач.
                    var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM_W>();

                    for (int i = 0; i < count; i++)
                    {
                        var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM_W>(IntPtr.Add(buf, i * itemSize));
                        if (item.FmtValue.CStatus != 0) continue;

                        string name = Marshal.PtrToStringUni(item.szName) ?? "";
                        int idx = name.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
                        string engine = idx >= 0 ? name.Substring(idx + 8) : "other";

                        perEngine.TryGetValue(engine, out double cur);
                        perEngine[engine] = cur + item.FmtValue.doubleValue;
                    }

                    double max = 0;
                    foreach (var v in perEngine.Values)
                        if (v > max) max = v;

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
                double cpuPercent = Math.Round(_cpuCounter?.NextValue() ?? 0, 1);

                var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                GlobalMemoryStatusEx(ref memStatus);
                ulong totalRamMb = memStatus.ullTotalPhys / 1024 / 1024;
                ulong availRamMb = memStatus.ullAvailPhys / 1024 / 1024;
                ulong usedRamMb = totalRamMb - availRamMb;
                double ramPercent = totalRamMb > 0 ? (double)usedRamMb / totalRamMb * 100.0 : 0;

                double gpuPercent = Math.Round(ReadGpu(), 1);

                Updated?.Invoke(this, (cpuPercent, Math.Round(ramPercent, 1), gpuPercent, usedRamMb, totalRamMb));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemMonitor] Poll failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _cpuCounter = null;

            if (_gpuQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_gpuQuery);
                _gpuQuery = IntPtr.Zero;
            }
            _gpuCounter = IntPtr.Zero;
            _gpuAvailable = false;
        }
    }
}