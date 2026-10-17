using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        public double BaseGhz { get; set; } = 3.55;
        public double MaxTurboGhz { get; set; } = 5.0;
        public double RealtimeGhz { get; set; } = 3.55;
        public double PeakCoreGhz { get; set; } = 5.0;
        public double AverageAllCoreGhz { get; set; } = 3.93;
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
        private static List<PerformanceCounter>? _coreCounters;
        private static readonly object _syncLock = new();

        static CpuOptimizerService()
        {
            try
            {
                if (PerformanceCounterCategory.Exists("Processor Information"))
                {
                    _cpuPerfCounter = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                    _cpuPerfCounter.NextValue();

                    var cat = new PerformanceCounterCategory("Processor Information");
                    var instances = cat.GetInstanceNames().Where(n => !n.Contains("Total", StringComparison.OrdinalIgnoreCase)).ToList();
                    _coreCounters = new List<PerformanceCounter>();
                    foreach (var inst in instances)
                    {
                        try
                        {
                            var pc = new PerformanceCounter("Processor Information", "% Processor Performance", inst);
                            pc.NextValue();
                            _coreCounters.Add(pc);
                        }
                        catch { }
                    }
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

                // Update real-time GHz reading (both peak core boost and all-core average)
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
                    int mhz = Convert.ToInt32(key.GetValue("~MHz") ?? 3550);
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

            // Detect hybrid architecture (e.g. Ryzen 8500G with Zen 4 + Zen 4c, or Intel 12th/13th/14th Gen P+E)
            info.IsHybrid = info.FullName.Contains("8500G", StringComparison.OrdinalIgnoreCase) ||
                            info.FullName.Contains("8300G", StringComparison.OrdinalIgnoreCase) ||
                            info.FullName.Contains("7545U", StringComparison.OrdinalIgnoreCase) ||
                            (info.IsIntel && (info.FullName.Contains("12", StringComparison.OrdinalIgnoreCase) ||
                                              info.FullName.Contains("13", StringComparison.OrdinalIgnoreCase) ||
                                              info.FullName.Contains("14", StringComparison.OrdinalIgnoreCase)));

            // Clean up short name
            info.ShortName = CleanCpuName(info.FullName);

            // Calculate Max Turbo GHz based on processor model or base clock
            info.MaxTurboGhz = CalculateMaxTurboGhz(info.FullName, info.BaseGhz);
            info.PeakCoreGhz = info.MaxTurboGhz;
            info.RealtimeGhz = info.BaseGhz;
            info.AverageAllCoreGhz = info.BaseGhz;

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
            double maxCorePerf = 0;
            double avgPerf = 0;

            if (_coreCounters != null && _coreCounters.Count > 0)
            {
                double sum = 0;
                int count = 0;
                foreach (var counter in _coreCounters)
                {
                    try
                    {
                        double val = counter.NextValue();
                        if (val > maxCorePerf) maxCorePerf = val;
                        sum += val;
                        count++;
                    }
                    catch { }
                }
                if (count > 0) avgPerf = sum / count;
            }

            if (avgPerf <= 0 && _cpuPerfCounter != null)
            {
                try
                {
                    avgPerf = _cpuPerfCounter.NextValue();
                }
                catch { }
            }

            if (avgPerf > 0)
            {
                info.PerformancePercent = Math.Round(avgPerf, 1);
                double calculatedAvgGhz = Math.Round((info.BaseGhz * (avgPerf / 100.0)), 2);
                info.AverageAllCoreGhz = Math.Max(info.BaseGhz * 0.5, calculatedAvgGhz);
                info.RealtimeGhz = info.AverageAllCoreGhz;

                if (maxCorePerf > 0)
                {
                    double calculatedPeak = Math.Round((info.BaseGhz * (maxCorePerf / 100.0)), 2);
                    info.PeakCoreGhz = Math.Max(info.RealtimeGhz, Math.Min(calculatedPeak, info.MaxTurboGhz));
                }
                else
                {
                    info.PeakCoreGhz = Math.Min(info.RealtimeGhz * 1.25, info.MaxTurboGhz);
                }
            }
            else
            {
                info.RealtimeGhz = info.BaseGhz;
                info.AverageAllCoreGhz = info.BaseGhz;
                info.PeakCoreGhz = info.MaxTurboGhz;
            }
        }

        public static async Task ApplyProcessorWiseMaxTurboAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    string sub = "54533251-82be-4824-96c1-47b60b740d00";

                    // 1. Force Aggressive At Guaranteed Boost Mode (5 = Aggressive At Guaranteed Turbo)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} be337238-0d82-4146-a960-4f3749d470c7 5");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} be337238-0d82-4146-a960-4f3749d470c7 5");

                    // 2. Processor Performance Boost Policy = 100% (Permits 100% maximum boost frequency headroom)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 45bcc044-d885-43e2-8605-ee0ec6e96b59 100");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} 45bcc044-d885-43e2-8605-ee0ec6e96b59 100");

                    // 3. Energy Performance Preference = 0 (100% Max Performance, 0% Energy Bias)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 0");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 0");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 36687f9e-e3a5-4dbf-b1dc-15eb381c6864 0");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} 36687f9e-e3a5-4dbf-b1dc-15eb381c6864 0");

                    // 4. Autonomous Mode = 1 (Hardware P-States / AMD CPPC / Intel Speed Shift Active)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 8baa4a8a-14c6-4451-8e8b-14bdbd197537 1");

                    // 5. Heterogeneous Thread Scheduling: 1 = Performant Processors Only (Zen 4 Big Cores)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 93b8b6dc-0698-4d1c-9ee4-0644e900c85d 1");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} 93b8b6dc-0698-4d1c-9ee4-0644e900c85d 1");

                    // 6. Heterogeneous Short Running Thread Policy: 1 = Performant Processors
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} bae08b81-2d5e-4688-ad6a-13243356654b 1");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} bae08b81-2d5e-4688-ad6a-13243356654b 1");

                    // 7. Max Processor Frequency = 0 (Unlimited / Full Turbo Boost)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 75b0ae3f-bce0-45a7-8c89-c9611c25e100 0");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 75b0ae3f-bce0-45a7-8c89-c9611c25e101 0");

                    // 8. Min / Max Processor State = 100%
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 893dee8e-2bef-41e0-89c6-b55d0929964c 100");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} 893dee8e-2bef-41e0-89c6-b55d0929964c 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} bc5038f7-23e0-4960-96da-33abaf5935ec 100");
                    RunCmd($"powercfg /setdcvalueindex scheme_current {sub} bc5038f7-23e0-4960-96da-33abaf5935ec 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 893dee8e-2bef-41e0-89c6-b55d0929964d 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} bc5038f7-23e0-4960-96da-33abaf5935ed 100");

                    // 9. Core Parking Min / Max Cores = 100% (Unpark all performance & compact cores)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 0cc5b647-c1df-4637-891a-dec35c318583 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 0cc5b647-c1df-4637-891a-dec35c318584 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} ea062031-0e34-4ff1-9b6d-eb1059334028 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} ea062031-0e34-4ff1-9b6d-eb1059334029 100");

                    // 10. Latency Sensitivity Hint = 100% Unpark & 100% Performance
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 616cdaa5-695e-4545-97ad-97dc2d1bdd88 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 619b7505-003b-4e82-b7a6-4dd29c300971 100");

                    // 11. Instant Frequency Increase (1 interval delay, 10% threshold) & Sustained Decrease (100 intervals)
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 06cadf0e-64ed-448a-8927-ce7bf90eb35d 10");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 12a0ab44-fe28-4fa9-b3bd-4b64f44960a6 5");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} 984cf492-3bed-4488-a8f9-4286c97bf5aa 1");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} d8edeb9b-95cf-4f95-a73c-b061973693c8 100");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} b000397d-9b0b-483d-98c9-692a6060cfbf 10");
                    RunCmd($"powercfg /setacvalueindex scheme_current {sub} f8861c27-95e7-475c-865b-13c0cb3f9d6b 5");

                    // Activate scheme
                    RunCmd("powercfg /setactive scheme_current");

                    // 12. Global Power Throttling & EcoQoS Registry Elimination
                    using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", true))
                    {
                        key?.SetValue("PowerThrottlingOff", 1, RegistryValueKind.DWord);
                    }

                    // 13. BCD Invariant TSC Clock Synchronization
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
