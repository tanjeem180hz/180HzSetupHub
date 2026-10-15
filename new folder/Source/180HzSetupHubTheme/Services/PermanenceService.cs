using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SetupHub180Hz.Services
{
    public class PermanenceService
    {
        private static readonly Lazy<PermanenceService> _lazy = new(() => new PermanenceService());
        public static PermanenceService Instance => _lazy.Value;

        private static readonly string GuardDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "180HzSetupHub");

        private static readonly string GuardScriptPath = Path.Combine(GuardDir, "PermanenceGuard.cmd");
        private static readonly string StatusFile = Path.Combine(GuardDir, "permanence_status.json");
        public const string TaskName = "180Hz_Permanence_Guard";

        public bool IsPermanenceLocked()
        {
            try
            {
                if (File.Exists(StatusFile))
                {
                    string content = File.ReadAllText(StatusFile);
                    return content.Contains("\"locked\": true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        public async Task<bool> DeployPermanentSafeguardAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(GuardDir))
                    {
                        Directory.CreateDirectory(GuardDir);
                    }

                    // 1. Write the Permanent Guard script that locks high performance and network on every Windows boot
                    string scriptContent =
@"@echo off
:: ===============================================================
:: 180Hz Setup Hub - Boot Persistence Safeguard Engine
:: Automatically reapplies core hardware, power, timer & network locks
:: ===============================================================

:: 1. Force Ultimate / High Performance Power Scheme & 100% Core Unparking
powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 >nul 2>&1
powercfg /setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor CPMINCORES 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor CPMAXCORES 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor PERFBOOSTMODE 2 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor PERFEPP 0 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor IDLEDIS 1 >nul 2>&1
powercfg /setactive scheme_current >nul 2>&1

:: 2. Network Zero-Ping & Fast Open Locking
netsh int tcp set global fastopen=enabled >nul 2>&1
netsh int tcp set global fastopenfallback=enabled >nul 2>&1
netsh int tcp set global rss=enabled >nul 2>&1
netsh int tcp set global autotuninglevel=normal >nul 2>&1
netsh int tcp set global timestamps=disabled >nul 2>&1
netsh int tcp set heuristics disabled >nul 2>&1
netsh int ip set global taskoffload=enabled >nul 2>&1

:: 3. Fast File System & NTFS Low Overhead
fsutil behavior set disablelastaccess 1 >nul 2>&1
fsutil 8dot3name set 1 >nul 2>&1

:: 4. Multimedia & GPU Scheduling Registry Enforcement
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NoLazyMode /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v AlwaysOn /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 8 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Priority"" /t REG_DWORD /d 6 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Scheduling Category"" /t REG_SZ /d ""High"" /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""SFIO Priority"" /t REG_SZ /d ""High"" /f >nul 2>&1

:: 5. Priority Control - Short Variable Quantum Foreground Boost (0x26 = 38)
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl"" /v Win32PrioritySeparation /t REG_DWORD /d 38 /f >nul 2>&1

:: 6. Disable GameDVR Background Recording & Overheads
reg add ""HKCU\System\GameConfigStore"" /v GameDVR_Enabled /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR"" /v AllowGameDVR /t REG_DWORD /d 0 /f >nul 2>&1

:: 7. BCD High Precision Scheduling Locks
bcdedit /set useplatformclock false >nul 2>&1
bcdedit /set useplatformtick yes >nul 2>&1
bcdedit /set disabledynamictick yes >nul 2>&1
";
                    File.WriteAllText(GuardScriptPath, scriptContent);

                    // 2. Register Scheduled Task to execute on every user logon with highest privileges
                    string createCmd = $"schtasks /create /tn \"{TaskName}\" /tr \"\\\"{GuardScriptPath}\\\"\" /sc onlogon /rl HIGHEST /f";
                    RunCmd(createCmd);

                    // 3. Immediately run the script once so it takes effect right now without needing a reboot
                    RunCmd($"cmd.exe /c \"{GuardScriptPath}\"");

                    // 4. Mark permanent status
                    string json = $"{{\n  \"locked\": true,\n  \"appliedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\"\n}}";
                    File.WriteAllText(StatusFile, json);

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public async Task<bool> RemovePermanentSafeguardAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    RunCmd($"schtasks /delete /tn \"{TaskName}\" /f");
                    if (File.Exists(StatusFile))
                        File.Delete(StatusFile);
                    if (File.Exists(GuardScriptPath))
                        File.Delete(GuardScriptPath);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
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
                p?.WaitForExit(10000);
            }
            catch { }
        }
    }
}
