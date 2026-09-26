using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SetupHub180Hz.Services
{
    public record SystemSnapshot(
        double CpuPercent,
        double RamPercent,
        double RamUsedGb,
        double RamTotalGb,
        double DiskPercent,
        double DiskFreeGb,
        double DiskTotalGb);

    public class SystemStatsService : IDisposable
    {
        private PerformanceCounter? _cpuCounter;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
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

            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public SystemStatsService()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // Discard initial reading (always 0)
            }
            catch
            {
                _cpuCounter = null;
            }
        }

        public SystemSnapshot GetSnapshot()
        {
            // 1. CPU Load
            double cpu = 0.0;
            if (_cpuCounter != null)
            {
                try
                {
                    cpu = Math.Round(_cpuCounter.NextValue(), 1);
                }
                catch
                {
                    cpu = 0.0;
                }
            }

            // 2. RAM Memory
            double ramUsedGb = 0.0;
            double ramTotalGb = 0.0;
            double ramPercent = 0.0;
            try
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(memStatus))
                {
                    ramTotalGb = Math.Round((double)memStatus.ullTotalPhys / (1024 * 1024 * 1024), 1);
                    double ramAvailGb = (double)memStatus.ullAvailPhys / (1024 * 1024 * 1024);
                    ramUsedGb = Math.Round(ramTotalGb - ramAvailGb, 1);
                    ramPercent = ramTotalGb > 0 ? Math.Round((ramUsedGb / ramTotalGb) * 100.0, 1) : (double)memStatus.dwMemoryLoad;
                }
            }
            catch { }

            // 3. Disk Space (C:)
            double diskFreeGb = 0.0;
            double diskTotalGb = 0.0;
            double diskPercent = 0.0;
            try
            {
                var cDrive = DriveInfo.GetDrives().FirstOrDefault(d => d.IsReady && d.Name.StartsWith("C", StringComparison.OrdinalIgnoreCase));
                if (cDrive != null)
                {
                    diskTotalGb = Math.Round((double)cDrive.TotalSize / (1024 * 1024 * 1024), 1);
                    diskFreeGb = Math.Round((double)cDrive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
                    double diskUsedGb = diskTotalGb - diskFreeGb;
                    diskPercent = diskTotalGb > 0 ? Math.Round((diskUsedGb / diskTotalGb) * 100.0, 1) : 0.0;
                }
            }
            catch { }

            return new SystemSnapshot(cpu, ramPercent, ramUsedGb, ramTotalGb, diskPercent, diskFreeGb, diskTotalGb);
        }

        public void Dispose()
        {
            _cpuCounter?.Dispose();
            _cpuCounter = null;
        }
    }
}
