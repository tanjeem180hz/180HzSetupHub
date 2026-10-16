using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SetupHub180Hz.Services
{
    public class GameBoosterService
    {
        private static readonly Lazy<GameBoosterService> _lazy = new(() => new GameBoosterService());
        public static GameBoosterService Instance => _lazy.Value;

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetTimerResolution(uint DesiredResolution, bool SetResolution, out uint CurrentResolution);

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtQueryTimerResolution(out uint MinimumResolution, out uint MaximumResolution, out uint CurrentResolution);

        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod", SetLastError = true)]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod", SetLastError = true)]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        private bool _isTimerLocked;
        private uint _currentTimerRes = 5000;

        public bool IsTimerResolutionLocked => _isTimerLocked;
        public double CurrentTimerResolutionMs => _currentTimerRes > 0 ? _currentTimerRes / 10000.0 : 0.5;

        public void Enable05msTimerResolution()
        {
            try
            {
                // Request 0.5ms (5000 in 100ns units)
                int status = NtSetTimerResolution(5000, true, out _currentTimerRes);
                TimeBeginPeriod(1);
                _isTimerLocked = true;
            }
            catch { }
        }

        public void DisableTimerResolution()
        {
            try
            {
                NtSetTimerResolution(5000, false, out _currentTimerRes);
                TimeEndPeriod(1);
                _isTimerLocked = false;
            }
            catch { }
        }

        public async Task<GameBoostResult> Activate2xFpsOverdriveAsync()
        {
            // 1. Lock 0.500ms Timer Resolution
            Enable05msTimerResolution();

            // 2. Memory Purge (Standby list, Working Sets, Modified list)
            long freedRam = await Task.Run(() => MemoryCleaner.CleanRam());

            // 3. Enforce Ultimate Power Scheme, 100% Core Unparking & Unlock Full Dynamic Turbo Boost GHz
            await Task.Run(() =>
            {
                RunCmd("powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor 0cc5b647-c1df-4637-891a-dec35c318583 100");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor ea062031-0e34-4ff1-9b6d-eb1059334028 100");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor 5d76a2ca-e8c0-402f-a133-2158492d58ad 0");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor be337238-0d82-4146-a960-4f3749d470c7 2");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 0");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor 75b0ae3f-bce0-45a7-8c89-c9611c25e100 0");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor 893dee8e-2bef-41e0-89c6-b55d0929964c 100");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor bc5038f7-23e0-4960-96da-33abaf5935ec 100");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor 06cadf0e-64ed-448a-8927-ce7bf90eb35d 10");
                RunCmd("powercfg -setacvalueindex scheme_current sub_processor 984cf492-3bed-4488-a8f9-4286c97bf5aa 1");
                RunCmd("powercfg -setactive scheme_current");
            });

            // 4. Enforce Game Mode, MMCSS 100% CPU Gaming Reservation, GPU Priority 8
            ApplyInstantGamingRegistry();

            // 5. Scan & boost active game processes (High Priority, High I/O, No Power Throttling)
            int boostedProcesses = await BoostRunningGameProcessesAsync();

            return new GameBoostResult
            {
                FreedRamBytes = freedRam,
                BoostedProcessesCount = boostedProcesses,
                TimerResolutionMs = 0.5,
                Success = true
            };
        }

        public static void ApplyInstantGamingRegistry()
        {
            try
            {
                // SystemResponsiveness = 0 (100% to games), NetworkThrottlingIndex = 0xFFFFFFFF
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", true))
                {
                    key?.SetValue("SystemResponsiveness", 0, RegistryValueKind.DWord);
                    key?.SetValue("NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord);
                    key?.SetValue("NoLazyMode", 1, RegistryValueKind.DWord);
                    key?.SetValue("AlwaysOn", 1, RegistryValueKind.DWord);
                }

                // Tasks\Games: GPU Priority = 8, Priority = 6
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", true))
                {
                    key?.SetValue("GPU Priority", 8, RegistryValueKind.DWord);
                    key?.SetValue("Priority", 6, RegistryValueKind.DWord);
                    key?.SetValue("Scheduling Category", "High", RegistryValueKind.String);
                    key?.SetValue("SFIO Priority", "High", RegistryValueKind.String);
                    key?.SetValue("Affinity", 0, RegistryValueKind.DWord);
                }

                // GameBar Game Mode
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\GameBar", true))
                {
                    key?.SetValue("AutoGameModeEnabled", 1, RegistryValueKind.DWord);
                    key?.SetValue("AllowAutoGameMode", 1, RegistryValueKind.DWord);
                }

                // GameConfigStore FSE & GameDVR Disable
                using (var key = Registry.CurrentUser.CreateSubKey(@"System\GameConfigStore", true))
                {
                    key?.SetValue("GameDVR_Enabled", 0, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_FSEBehaviorMode", 2, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_HonorUserFSEBehaviorMode", 1, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_DXGIHonorFSEWindowsCompatible", 1, RegistryValueKind.DWord);
                    key?.SetValue("GameDVR_EFSEFeatureFlags", 0, RegistryValueKind.DWord);
                }

                // Direct3D DirectFlip & 10GB Shader Cache
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Direct3D", true))
                {
                    key?.SetValue("EnableDirectFlip", 1, RegistryValueKind.DWord);
                    key?.SetValue("DirectFlipHonor", 1, RegistryValueKind.DWord);
                }

                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Direct3D\Shaders", true))
                {
                    key?.SetValue("ShaderCacheSize", 10240, RegistryValueKind.DWord);
                }

                // GraphicsDrivers HwSchMode = 2
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", true))
                {
                    key?.SetValue("HwSchMode", 2, RegistryValueKind.DWord);
                    key?.SetValue("PowerSavingPolicy", 0, RegistryValueKind.DWord);
                }

                // Mouse Acceleration Linear Fix
                using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse", true))
                {
                    key?.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                    key?.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                    key?.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                }

                // CSRSS Realtime
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\csrss.exe\PerfOptions", true))
                {
                    key?.SetValue("CpuPriorityClass", 4, RegistryValueKind.DWord);
                }

                // DWM 1 Queued Frame
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM", true))
                {
                    key?.SetValue("MaxQueuedBuffers", 1, RegistryValueKind.DWord);
                }

                // Disable GPU Ultra-Low Power State (ULPS) & PowerMizer across all display adapters for 100% full GPU utilization
                for (int i = 0; i <= 3; i++)
                {
                    string subkey = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{i:D4}";
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(subkey, true);
                        if (key != null)
                        {
                            key.SetValue("EnableUlps", 0, RegistryValueKind.DWord);
                            key.SetValue("EnableUlps_NA", 0, RegistryValueKind.DWord);
                            key.SetValue("KMD_EnableComputePreemption", 1, RegistryValueKind.DWord);
                            key.SetValue("DalDisableDisplayGating", 1, RegistryValueKind.DWord);
                            key.SetValue("DisableDrmdmaPowerGating", 1, RegistryValueKind.DWord);
                            key.SetValue("DisableStutterMode", 1, RegistryValueKind.DWord);
                            key.SetValue("PowerMizerEnable", 0, RegistryValueKind.DWord);
                            key.SetValue("PowerMizerLevel", 1, RegistryValueKind.DWord);
                            key.SetValue("PowerMizerLevelAC", 1, RegistryValueKind.DWord);
                        }
                    }
                    catch { }
                }

                // Route running games directly to High-Performance GPU in DirectX UserGpuPreferences
                try
                {
                    using var gpuPrefKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true);
                    if (gpuPrefKey != null)
                    {
                        foreach (var p in Process.GetProcesses())
                        {
                            try
                            {
                                string pName = p.ProcessName.ToLowerInvariant();
                                foreach (var g in KnownGameExes)
                                {
                                    if (pName.Contains(g))
                                    {
                                        string? mainModulePath = p.MainModule?.FileName;
                                        if (!string.IsNullOrEmpty(mainModulePath))
                                        {
                                            gpuPrefKey.SetValue(mainModulePath, "GpuPreference=2;", RegistryValueKind.String);
                                        }
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        private static readonly string[] KnownGameExes = new[]
        {
            "cs2", "csgo", "valorant", "valorant-win64-shipping", "fortniteclient-win64-shipping",
            "tslgame", "r5apex", "gta5", "robloxplayerbeta", "javaw", "minecraft",
            "hd-player", "androidprocess", "aow_exe", "mumuplayer", "ldvboxheadless",
            "overwatch", "rainbowsix", "league of legends", "dota2", "pubgmobile",
            "cod", "modernwarfare", "warzone", "rocketleague", "apex", "genshinimpact"
        };

        public static Task<int> BoostRunningGameProcessesAsync()
        {
            return Task.Run(() =>
            {
                int count = 0;
                try
                {
                    var processes = Process.GetProcesses();
                    foreach (var p in processes)
                    {
                        try
                        {
                            string name = p.ProcessName.ToLowerInvariant();
                            bool isGame = false;
                            foreach (var g in KnownGameExes)
                            {
                                if (name.Contains(g))
                                {
                                    isGame = true;
                                    break;
                                }
                            }

                            if (isGame)
                            {
                                if (p.PriorityClass != ProcessPriorityClass.High && p.PriorityClass != ProcessPriorityClass.RealTime)
                                {
                                    p.PriorityClass = ProcessPriorityClass.High;
                                    count++;
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                return count;
            });
        }

        public static List<string> GetRunningGameProcessNames()
        {
            var list = new List<string>();
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        string name = p.ProcessName.ToLowerInvariant();
                        foreach (var g in KnownGameExes)
                        {
                            if (name.Contains(g))
                            {
                                list.Add(p.ProcessName);
                                break;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void RunCmd(string cmd)
        {
            try
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c {cmd}")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { }
        }
    }

    public class GameBoostResult
    {
        public bool Success { get; set; }
        public long FreedRamBytes { get; set; }
        public int BoostedProcessesCount { get; set; }
        public double TimerResolutionMs { get; set; }
    }
}
