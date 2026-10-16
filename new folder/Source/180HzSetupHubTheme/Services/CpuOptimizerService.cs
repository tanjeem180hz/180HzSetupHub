using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SetupHub180Hz.Services
{
    public class CpuInfo
    {
        public string FullName { get; set; } = "Processor";
        public string ShortName { get; set; } = "Processor";
        public string Vendor { get; set; } = "Unknown";
        public double BaseGhz { get; set; } = 3.5;
        public double MaxTurboGhz { get; set; } = 5.0;
        public double RealtimeGhz { get; set; } = 3.5;
        public double PerformancePercent { get; set; } = 100.0;
        public int CoreCount { get; set; } = Environment.ProcessorCount / 2;
        public int ThreadCount { get; set; } = Environment.ProcessorCount;
        public bool IsHybrid { get; set; } = false;
        public bool IsAmd { get; set; } = false;
        public bool IsIntel { get; set; } = false;
    }

    public static class CpuOptimizerService
    {
        private static CpuInfo? _cachedInfo;
        private static PerformanceCounter? _cpuPerfCounter;
        private static readonly object _syncLock = new();

        static CpuOptimizerService()
        {
            try
            {
                if (PerformanceCounterCategory.Exists("Processor Information"))
                {
                    _cpuPerfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                    _cpuPerfCounter.NextValue();
                }
            }
            catch { }
        }

        public static CpuInfo GetCpuInfo()
        {
            lock (_syncLock)
            {
                if (_cachedInfo == null)
                {
                    _cachedInfo = DetectCpuHardware();
                }

                // Update real-time GHz reading
                UpdateRealtimeFrequency(_cachedInfo);
                return _cachedInfo;
            }
        }

        private static CpuInfo DetectCpuHardware()
        {
            var info = new CpuInfo();
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                if (key != null)
                {
                    info.FullName = (key.GetValue("ProcessorNameString") as string)?.Trim() ?? "Processor";
                    info.Vendor = (key.GetValue("VendorIdentifier") as string)?.Trim() ?? "";
                    int mhz = Convert.ToInt32(key.GetValue("~MHz") ?? 3500);
                    info.BaseGhz = Math.Round(mhz / 1000.0, 2);
                }
            }
            catch { }

            info.ThreadCount = Environment.ProcessorCount;
            info.IsAmd = info.Vendor.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                         info.FullName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                         info.FullName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
            info.IsIntel = info.Vendor.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
                           info.FullName.Contains("Intel", StringComparison.OrdinalIgnoreCase);

            // Clean up short name
            info.ShortName = CleanCpuName(info.FullName);

            // Calculate Max Turbo GHz based on processor model or base clock
            info.MaxTurboGhz = CalculateMaxTurboGhz(info.FullName, info.BaseGhz);
            info.RealtimeGhz = info.BaseGhz;

            return info;
        }

        private static string CleanCpuName(string fullName)
        {
            string name = fullName;
            name = Regex.Replace(name, @"\(TM\)|\(R\)|w/.*|Processor|CPU", "", RegexOptions.IgnoreCase).Trim();
            name = Regex.Replace(name, @"\s+", " ").Trim();
            return string.IsNullOrWhiteSpace(name) ? fullName : name;
        }

        private static double CalculateMaxTurboGhz(string fullName, double baseGhz)
        {
            // Specific lookup for known popular processors
            if (fullName.Contains("8500G", StringComparison.OrdinalIgnoreCase)) return 5.0;
            if (fullName.Contains("8600G", StringComparison.OrdinalIgnoreCase)) return 5.0;
            if (fullName.Contains("8700G", StringComparison.OrdinalIgnoreCase)) return 5.1;
            if (fullName.Contains("7800X3D", StringComparison.OrdinalIgnoreCase)) return 5.05;
            if (fullName.Contains("7950X3D", StringComparison.OrdinalIgnoreCase)) return 5.7;
            if (fullName.Contains("7950X", StringComparison.OrdinalIgnoreCase)) return 5.7;
            if (fullName.Contains("7900X", StringComparison.OrdinalIgnoreCase)) return 5.6;
            if (fullName.Contains("7700X", StringComparison.OrdinalIgnoreCase)) return 5.4;
            if (fullName.Contains("7600X", StringComparison.OrdinalIgnoreCase)) return 5.3;
            if (fullName.Contains("5800X3D", StringComparison.OrdinalIgnoreCase)) return 4.5;
            if (fullName.Contains("5800X", StringComparison.OrdinalIgnoreCase)) return 4.7;
            if (fullName.Contains("5600X", StringComparison.OrdinalIgnoreCase)) return 4.6;
            if (fullName.Contains("5600G", StringComparison.OrdinalIgnoreCase)) return 4.4;
            if (fullName.Contains("14900K", StringComparison.OrdinalIgnoreCase)) return 6.0;
            if (fullName.Contains("14700K", StringComparison.OrdinalIgnoreCase)) return 5.6;
            if (fullName.Contains("13900K", StringComparison.OrdinalIgnoreCase)) return 5.8;
            if (fullName.Contains("13700K", StringComparison.OrdinalIgnoreCase)) return 5.4;
            if (fullName.Contains("13600K", StringComparison.OrdinalIgnoreCase)) return 5.1;
            if (fullName.Contains("12900K", StringComparison.OrdinalIgnoreCase)) return 5.2;
            if (fullName.Contains("12700K", StringComparison.OrdinalIgnoreCase)) return 5.0;
            if (fullName.Contains("12600K", StringComparison.OrdinalIgnoreCase)) return 4.9;
            if (fullName.Contains("12400", StringComparison.OrdinalIgnoreCase)) return 4.4;

            // Generic turbo multiplier
            if (baseGhz > 0)
            {
                return Math.Round(Math.Max(baseGhz * 1.35, 4.8), 1);
            }
            return 5.0;
        }

        private static void UpdateRealtimeFrequency(CpuInfo info)
        {
            if (_cpuPerfCounter != null)
            {
                try
                {
                    double perfPercent = _cpuPerfCounter.NextValue();
                    if (perfPercent > 0)
                    {
                        info.PerformancePercent = Math.Round(perfPercent, 1);
                        double calculatedGhz = Math.Round((info.BaseGhz * (perfPercent / 100.0)), 2);
                        info.RealtimeGhz = Math.Max(info.BaseGhz * 0.5, Math.Min(calculatedGhz, info.MaxTurboGhz + 0.2));
                        return;
                    }
                }
                catch { }
            }

            info.RealtimeGhz = Math.Min(info.BaseGhz * 1.25, info.MaxTurboGhz);
        }

        public static async Task ApplyProcessorWiseMaxTurboAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    // 1. Force Aggressive Boost Mode (2 = Aggressive Turbo)
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor be337238-0d82-4146-a960-4f3749d470c7 2");
                    RunCmd("powercfg /setdcvalueindex scheme_current sub_processor be337238-0d82-4146-a960-4f3749d470c7 2");

                    // 2. Energy Performance Preference = 0 (100% Max Performance, 0% Energy Bias)
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 0");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 36687f9e-e3a5-4dbf-b1dc-15eb381c6864 0");

                    // 3. Autonomous Mode = 1 (Hardware P-States / CPPC / Speed Shift Active in Nanoseconds)
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 8baa4a8a-14c6-4451-8e8b-14bdbd197537 1");

                    // 4. Max Processor Frequency = 0 (Unlimited / Full Turbo)
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 75b0ae3f-bce0-45a7-8c89-c9611c25e100 0");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 75b0ae3f-bce0-45a7-8c89-c9611c25e101 0");

                    // 5. Min / Max Processor State = 100%
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 893dee8e-2bef-41e0-89c6-b55d0929964c 100");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor bc5038f7-23e0-4960-96da-33abaf5935ec 100");

                    // 6. Core Parking Min / Max Cores = 100% (Unpark all performance & efficiency cores)
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 0cc5b647-c1df-4637-891a-dec35c318583 100");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 0cc5b647-c1df-4637-891a-dec35c318584 100");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor ea062031-0e34-4ff1-9b6d-eb1059334028 100");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor ea062031-0e34-4ff1-9b6d-eb1059334029 100");

                    // 7. Latency Sensitivity Hint = 100% Unpark & 100% Performance
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 616cdaa5-695e-4545-97ad-97dc2d1bdd88 100");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 619b7505-003b-4e82-b7a6-4dd29c300971 100");

                    // 8. Heterogeneous Thread Scheduling: Prioritize Performant Cores (Zen 4 / P-Cores)
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 93b8b6dc-0698-4d1c-9ee4-0644e900c85d 2");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor bae08b81-2d5e-4688-ad6a-13243356654b 2");

                    // 9. Instant Frequency Increase (1 interval delay, 10% threshold) & Sustained Decrease (100 intervals)
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 984cf492-3bed-4488-a8f9-4286c97bf5aa 1");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor d8edeb9b-95cf-4f95-a73c-b061973693c8 100");
                    RunCmd("powercfg /setacvalueindex scheme_current sub_processor 5d76a2ca-e8c0-402f-a133-2158492d58ad 0");

                    // Activate scheme
                    RunCmd("powercfg /setactive scheme_current");

                    // 10. Unhide Processor Performance Boost Mode in Windows Power Options
                    try
                    {
                        using var boostKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerSettings\54533251-82be-4824-96c1-47b60b740d00\be337238-0d82-4146-a960-4f3749d470c7", true);
                        boostKey?.SetValue("Attributes", 2, RegistryValueKind.DWord);
                    }
                    catch { }

                    // 11. Global Power Throttling & EcoQoS Registry Elimination
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", true))
                    {
                        key?.SetValue("PowerThrottlingOff", 1, RegistryValueKind.DWord);
                    }

                    // 11. BCD Invariant TSC Clock Synchronization
                    RunCmd("bcdedit /set useplatformclock false");
                    RunCmd("bcdedit /set useplatformtick yes");
                    RunCmd("bcdedit /set disabledynamictick yes");
                    RunCmd("bcdedit /set tscsyncpolicy Enhanced");
                }
                catch { }
            });
        }

        private static void RunCmd(string cmd)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {cmd}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                p?.WaitForExit(3000);
            }
            catch { }
        }
    }
}
