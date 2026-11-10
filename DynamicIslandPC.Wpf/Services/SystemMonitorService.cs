using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

        private TimeSpan _lastCpuTime;
        private DateTime _lastCpuCheck = DateTime.UtcNow;

        public event EventHandler<(double CpuPercent, double RamPercent, double GpuPercent, ulong UsedRamMb, ulong TotalRamMb)>? Updated;

        public SystemMonitorService()
        {
            _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
            _lastCpuCheck = DateTime.UtcNow;
        }

        public void Poll()
        {
            try
            {
                // CPU (для текущего процесса, нормализуем к 100%)
                var currentProcess = Process.GetCurrentProcess();
                var currentCpuTime = currentProcess.TotalProcessorTime;
                var now = DateTime.UtcNow;

                var cpuTimeSpan = currentCpuTime - _lastCpuTime;
                var realTimeSpan = now - _lastCpuCheck;

                double cpuPercent = 0;
                if (realTimeSpan.TotalMilliseconds > 0)
                {
                    // Получаем количество логических ядер для нормализации
                    int cores = Environment.ProcessorCount;
                    cpuPercent = (cpuTimeSpan.TotalMilliseconds / realTimeSpan.TotalMilliseconds) * 100.0 / cores;
                }

                _lastCpuTime = currentCpuTime;
                _lastCpuCheck = now;

                // RAM
                var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                GlobalMemoryStatusEx(ref memStatus);

                ulong totalRamMb = memStatus.ullTotalPhys / 1024 / 1024;
                ulong availRamMb = memStatus.ullAvailPhys / 1024 / 1024;
                ulong usedRamMb = totalRamMb - availRamMb;
                double ramPercent = totalRamMb > 0 ? (double)usedRamMb / totalRamMb * 100.0 : 0;

                // GPU — заглушка 0% (PerformanceCounter нестабилен в WPF)
                double gpuPercent = 0;

                Updated?.Invoke(this, (
                    Math.Round(cpuPercent, 1),
                    Math.Round(ramPercent, 1),
                    gpuPercent,
                    usedRamMb,
                    totalRamMb
                ));
            }
            catch { }
        }

        public void Dispose() { }
    }
}