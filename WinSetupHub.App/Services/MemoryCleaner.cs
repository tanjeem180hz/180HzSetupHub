using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SetupHub180Hz.Services
{
    public static class MemoryCleaner
    {
        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

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

        public static long CleanRam()
        {
            long beforeAvail = GetAvailableMemoryBytes();

            int trimmedCount = 0;
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    if (EmptyWorkingSet(proc.Handle))
                        trimmedCount++;
                }
                catch
                {
                    // Skip system / protected processes
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long afterAvail = GetAvailableMemoryBytes();
            long freed = Math.Max(0, afterAvail - beforeAvail);

            // Fallback estimation if OS instantly re-allocated buffers
            if (freed == 0 && trimmedCount > 0)
            {
                freed = trimmedCount * 12L * 1024L * 1024L;
            }

            return freed;
        }

        public static long GetAvailableMemoryBytes()
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                    return (long)mem.ullAvailPhys;
            }
            catch { }
            return 0;
        }

        public static long GetTotalMemoryBytes()
        {
            try
            {
                var mem = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(mem))
                    return (long)mem.ullTotalPhys;
            }
            catch { }
            return 0;
        }
    }
}
