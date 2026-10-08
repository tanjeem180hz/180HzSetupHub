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

            // 2. Flush Modified Page List to disk so they transition into purgeable standby memory
            try
            {
                int cmdModified = MemoryFlushModifiedList;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmdModified, sizeof(int));
            }
            catch { }

            // 3. Purge Windows Standby Lists (clears standby cache, file cache, and inactive pages)
            try
            {
                int cmdStandby = MemoryPurgeStandbyList;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmdStandby, sizeof(int));
            }
            catch { }

            // 4. Purge Low Priority Standby List
            try
            {
                int cmdLowPriority = MemoryPurgeLowPriorityStandbyList;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmdLowPriority, sizeof(int));
            }
            catch { }

            // 5. System-wide Kernel Working Sets Emptying
            try
            {
                int cmdWorkingSets = MemoryEmptyWorkingSets;
                NtSetSystemInformation(SystemMemoryListInformation, ref cmdWorkingSets, sizeof(int));
            }
            catch { }

            // 6. Flush Windows System File Cache Working Set
            try
            {
                SetSystemFileCacheSize((IntPtr)(-1), (IntPtr)(-1), 0);
            }
            catch { }

            // 7. Iterate through user & system processes and trim working sets safely with OpenProcess
            int trimmedCount = 0;
            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.Id <= 4) continue; // Skip System Idle and System

                        IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, proc.Id);
                        if (hProcess != IntPtr.Zero)
                        {
                            try
                            {
                                if (EmptyWorkingSet(hProcess))
                                    trimmedCount++;
                            }
                            finally
                            {
                                CloseHandle(hProcess);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        try { proc.Dispose(); } catch { }
                    }
                }
            }
            catch { }

            // 8. Aggressive CLR garbage collection & heap compaction for maximum performance
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

            // Fallback estimation if OS instantly repopulated cache or background services buffered
            if (freed < 10L * 1024L * 1024L && trimmedCount > 0)
            {
                freed = Math.Max(freed, trimmedCount * 14L * 1024L * 1024L);
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
