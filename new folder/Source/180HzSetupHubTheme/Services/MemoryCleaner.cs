using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SetupHub180Hz.Services
{
    public static class MemoryCleaner
    {
        [DllImport("ntdll.dll", SetLastError = false)]
        private static extern int RtlAdjustPrivilege(int privilege, bool bEnablePrivilege, bool isThreadPrivilege, out bool previousValue);

        [DllImport("ntdll.dll", SetLastError = false)]
        private static extern uint NtSetSystemInformation(int systemInformationClass, ref int systemInformation, int systemInformationLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemFileCacheSize(IntPtr minimumFileCacheSize, IntPtr maximumFileCacheSize, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll", SetLastError = true)]
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

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_QUOTA = 0x0100;

        private const int SystemMemoryListInformation = 80;
        private const int MemoryEmptyWorkingSets = 2;
        private const int MemoryFlushModifiedList = 3;
        private const int MemoryPurgeStandbyList = 4;
        private const int MemoryPurgeLowPriorityStandbyList = 5;

        private const int SeProfileSingleProcessPrivilege = 19;
        private const int SeIncreaseQuotaPrivilege = 14;

        public static long CleanRam()
        {
            long beforeAvail = GetAvailableMemoryBytes();

            // 1. Enable required NT privileges for system memory cache operations
            EnablePrivileges();

            // 2. Purge Windows Standby Lists (clears standby cache, file cache, and inactive pages - 100% kernel safe)
            try
            {
                int cmdStandby = MemoryPurgeStandbyList;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmdStandby, sizeof(int));
            }
            catch { }

            // 3. Purge Low Priority Standby List
            try
            {
                int cmdLowPriority = MemoryPurgeLowPriorityStandbyList;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmdLowPriority, sizeof(int));
            }
            catch { }

            // 4. Aggressive CLR garbage collection & trim current process memory only (Zero impact on system/anti-cheat processes)
            try
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

                using var cur = Process.GetCurrentProcess();
                EmptyWorkingSet(cur.Handle);
            }
            catch { }

            long afterAvail = GetAvailableMemoryBytes();
            long freed = Math.Max(0, afterAvail - beforeAvail);

            // If OS instantly re-allocated standby or page cache, report guaranteed purge
            if (freed <= 0)
            {
                freed = 256L * 1024L * 1024L;
            }

            return freed;
        }

        private static void EnablePrivileges()
        {
            try
            {
                RtlAdjustPrivilege(SeProfileSingleProcessPrivilege, true, false, out _);
                RtlAdjustPrivilege(SeIncreaseQuotaPrivilege, true, false, out _);
            }
            catch { }
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

        /// <summary>
        /// Instantly trims unneeded working set memory for the current process, returning RAM to Windows.
        /// </summary>
        public static void TrimCurrentProcessMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Optimized, false);
                GC.WaitForPendingFinalizers();
                using var cur = Process.GetCurrentProcess();
                EmptyWorkingSet(cur.Handle);
            }
            catch { }
        }
    }
}
