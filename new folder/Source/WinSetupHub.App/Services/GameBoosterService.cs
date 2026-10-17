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

        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            int ProcessInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE ProcessInformation,
            uint ProcessInformationSize);

        private const int ProcessPowerThrottling = 4;
        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;
        private const int ProcessIoPriority = 21;

        private bool _isTimerLocked;
        private uint _currentTimerRes = 5000;

        public bool IsTimerResolutionLocked => _isTimerLocked;
        public double CurrentTimerResolutionMs => _currentTimerRes > 0 ? _currentTimerRes / 10000.0 : 0.5;

        private static Timer? _daemonTimer;
        private static readonly object _daemonLock = new();
        public static bool IsContinuousEngineRunning { get; private set; }

        public static void StartContinuousGameBoosterEngine()
        {
            lock (_daemonLock)
            {
                if (_daemonTimer != null) return;
                IsContinuousEngineRunning = true;
                _daemonTimer = new Timer(async _ =>
                {
                    try
                    {
                        var running = GetRunningGameProcessNames();
                        if (running.Count > 0)
                        {
                            Instance.Enable05msTimerResolution();
                            await BoostRunningGameProcessesAsync();
                        }
                    }
                    catch { }
                }, null, 1000, 2500);
            }
        }

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

            // 3. Enforce Ultimate Power Scheme, Hardware CPPC/SpeedShift, Dynamic Turbo Boost GHz & Disable Memory Compression
            await Task.Run(() =>
            {
                RunCmd("powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                RunCmd("powershell -NoProfile -ExecutionPolicy Bypass -Command \"Disable-MMAgent -MemoryCompression -PageCombining\"");
            });
            await CpuOptimizerService.ApplyProcessorWiseMaxTurboAsync();

            // 4. Enforce Game Mode, MMCSS 100% CPU Gaming Reservation, GPU Priority 8, Universal NVIDIA/AMD Overdrive
            ApplyInstantGamingRegistry();

            // 5. Scan & boost active emulator and game processes (High Priority, High I/O, No Power Throttling, Full Cores)
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

                // Priority Control - Win32 Priority Separation (0x26 = 38: 3:1 Foreground Quanta Boost)
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", true))
                {
                    key?.SetValue("Win32PrioritySeparation", 38, RegistryValueKind.DWord);
                }

                // Global CPU Power Throttling Off (Prevents EcoQoS throttling on emulator/game background threads)
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", true))
                {
                    key?.SetValue("PowerThrottlingOff", 1, RegistryValueKind.DWord);
                }

                // Memory Management - Keep Kernel & Drivers in Physical RAM (0 Page Fault Stutter)
                using (var key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true))
                {
                    key?.SetValue("DisablePagingExecutive", 1, RegistryValueKind.DWord);
                    key?.SetValue("LargeSystemCache", 0, RegistryValueKind.DWord);
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

                // Windowed VRR (G-Sync & FreeSync in Borderless/Emulator Windows)
                using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Graphics\WindowedVRR", true))
                {
                    key?.SetValue("Enabled", 1, RegistryValueKind.DWord);
                }

                // GraphicsDrivers HwSchMode = 2 (Hardware-Accelerated GPU Scheduling)
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

                // Dual-Vendor GPU Overdrive (NVIDIA PowerMizer + AMD ULPS) across all display adapters
                for (int i = 0; i <= 4; i++)
                {
                    string subkey = $@"SYSTEM\CurrentControlSet\Control\Class\{{4d36e968-e325-11ce-bfc1-08002be10318}}\{i:D4}";
                    try
                    {
                        using var key = Registry.LocalMachine.OpenSubKey(subkey, true);
                        if (key != null)
                        {
                            // AMD Radeon Optimizations
                            key.SetValue("EnableUlps", 0, RegistryValueKind.DWord);
                            key.SetValue("EnableUlps_NA", 0, RegistryValueKind.DWord);
                            key.SetValue("DalDisableDisplayGating", 1, RegistryValueKind.DWord);
                            key.SetValue("DisableDrmdmaPowerGating", 1, RegistryValueKind.DWord);
                            key.SetValue("DisableStutterMode", 1, RegistryValueKind.DWord);
                            key.SetValue("KMD_EnableComputePreemption", 1, RegistryValueKind.DWord);
                            key.SetValue("PP_ThermalAutoThrottlingEnable", 0, RegistryValueKind.DWord);
                            key.SetValue("ShaderCache", 1, RegistryValueKind.DWord);

                            // NVIDIA GeForce Optimizations
                            key.SetValue("PowerMizerEnable", 0, RegistryValueKind.DWord);
                            key.SetValue("PowerMizerLevel", 1, RegistryValueKind.DWord);
                            key.SetValue("PowerMizerLevelAC", 1, RegistryValueKind.DWord);
                            key.SetValue("PerfLevelSrc", 0x2222, RegistryValueKind.DWord);
                            key.SetValue("RmGpuPreemption", 1, RegistryValueKind.DWord);
                            key.SetValue("DisablePowerGovernors", 1, RegistryValueKind.DWord);
                        }
                    }
                    catch { }
                }

                // Pre-configure IFEO PerfOptions for all major Android emulators and competitive games
                string[] masterEmulatorExes = new[]
                {
                    "HD-Player.exe", "HD-Agent.exe", "BlueStacks.exe", "dnplayer.exe", "LdBoxHeadless.exe",
                    "ldvboxheadless.exe", "dnmultiplayer.exe", "AppMarket.exe", "AndroidProcess.exe", "aow_exe.exe",
                    "TXPlatform.exe", "TBSWebRenderer.exe", "Nox.exe", "MEmu.exe", "MEmuHeadless.exe", "MuMuPlayer.exe",
                    "MuMuNxPlayer.exe", "NemuPlayer.exe", "NemuHeadless.exe", "SmartGaGa.exe", "TitanEngine.exe",
                    "crosvm.exe", "vboxheadless.exe", "qemu-system-x86_64.exe", "cs2.exe", "valorant.exe"
                };

                foreach (var exe in masterEmulatorExes)
                {
                    try
                    {
                        using var ifeo = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exe}\PerfOptions", true);
                        ifeo?.SetValue("CpuPriorityClass", 3, RegistryValueKind.DWord);
                        ifeo?.SetValue("IoPriority", 3, RegistryValueKind.DWord);
                        ifeo?.SetValue("PagePriority", 5, RegistryValueKind.DWord);
                    }
                    catch { }
                }

                // Route all major emulators and running games directly to High-Performance GPU in DirectX UserGpuPreferences
                try
                {
                    using var gpuPrefKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", true);
                    if (gpuPrefKey != null)
                    {
                        foreach (var exe in masterEmulatorExes)
                        {
                            gpuPrefKey.SetValue(exe, "GpuPreference=2;", RegistryValueKind.String);
                        }

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
            // Android Emulators (BlueStacks, MSI App Player, LDPlayer, GameLoop, Nox, MEmu, MuMu, SmartGaGa, Google Play Games)
            "hd-player", "hd-agent", "hd-apphandler", "hd-plus-service", "hd-adb", "bstksvc", "bluestacks", "hd-multimanager",
            "msi", "msiappplayer",
            "dnplayer", "ldboxheadless", "ldvboxheadless", "ldnews", "ldboxsvc", "dnmultiplayer",
            "appmarket", "androidprocess", "aow_exe", "txplatform", "tbswebrenderer", "qqexternal", "tsettingcenter",
            "nox", "noxvmsvc", "nox_adb", "noxmultiplayer",
            "memu", "memuconsole", "memuheadless", "memupower",
            "mumuplayer", "mumunxplayer", "nemuplayer", "nemuheadless", "mumuvmmdl", "nemuvmmdl",
            "smartgaga", "titanengine", "crosvm", "vboxheadless", "qemu-system-x86_64",

            // Esports & PC Games
            "cs2", "csgo", "valorant", "valorant-win64-shipping", "riotclientservices",
            "fortniteclient-win64-shipping", "fortnite",
            "tslgame", "pubgmobile", "pubg",
            "r5apex", "apex",
            "gta5", "fivem_b", "fivem_gta", "fivem",
            "robloxplayerbeta", "javaw", "minecraft", "bedrock_server",
            "overwatch", "rainbowsix", "league of legends", "leagueclient", "dota2",
            "cod", "modernwarfare", "warzone", "rocketleague",
            "genshinimpact", "starrail", "wutheringwaves", "honkaistarrail",
            "fifa", "fc24", "fc25", "ea",
            "cyberpunk2077", "witcher3", "rdr2",
            "forzahorizon5", "forzahorizon4",
            "destiny2", "helldivers2", "rustclient", "rust",
            "deadbydaylight", "fallguys_client", "naraka", "warframe",
            "battlefield", "bf2042"
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
                                // 1. Elevate Process Priority to High
                                if (p.PriorityClass != ProcessPriorityClass.High && p.PriorityClass != ProcessPriorityClass.RealTime)
                                {
                                    p.PriorityClass = ProcessPriorityClass.High;
                                }

                                // 2. Disable Windows Power Throttling / EcoQoS on process
                                try
                                {
                                    var state = new PROCESS_POWER_THROTTLING_STATE
                                    {
                                        Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                                        ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                                        StateMask = 0
                                    };
                                    SetProcessInformation(p.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf(state));
                                }
                                catch { }

                                // 3. Elevate I/O Priority to High (Level 3)
                                try
                                {
                                    int ioPriority = 3;
                                    NtSetInformationProcess(p.Handle, ProcessIoPriority, ref ioPriority, sizeof(int));
                                }
                                catch { }

                                // 4. Set CPU Core Affinity across all logical processors
                                try
                                {
                                    long mask = (1L << Math.Min(64, Environment.ProcessorCount)) - 1;
                                    if (mask > 0)
                                    {
                                        p.ProcessorAffinity = (IntPtr)mask;
                                    }
                                }
                                catch { }

                                // 5. Register in IFEO for future automatic High Priority execution
                                try
                                {
                                    string exeName = p.ProcessName + ".exe";
                                    using var ifeoKey = Registry.LocalMachine.CreateSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{exeName}\PerfOptions", true);
                                    if (ifeoKey != null)
                                    {
                                        ifeoKey.SetValue("CpuPriorityClass", 3, RegistryValueKind.DWord);
                                        ifeoKey.SetValue("IoPriority", 3, RegistryValueKind.DWord);
                                        ifeoKey.SetValue("PagePriority", 5, RegistryValueKind.DWord);
                                    }
                                }
                                catch { }

                                count++;
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
