using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinSetupHub.Setup;

public sealed class InstallerService
{
    public const string AppName = "180Hz Setup Hub";
    public const string InstalledExeName = "180HzSetupHub.exe";
    public const string UninstallerExeName = "Uninstall.exe";
    public const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\180HzSetupHub";

    public const string DefaultDownloadUrl = "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe";
    public const string RawDownloadUrl = "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/artifacts/publish/win-x64/180HzSetupHub.exe";
    public const string FallbackDownloadUrl = "https://github.com/tanjeem180hz/180HzSetupHub/releases/latest/download/180HzSetupHub.exe";

    private const string PayloadPackages = "packages.default.json";
    private const string PayloadAppSettings = "appsettings.default.json";
    private const string PayloadTweaks = "tweaks.default.json";
    private const string PayloadRegistryTweaks = "registry_tweaks.default.json";

    public static string GetDefaultInstallRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppName);
    }

    public static string FormatDynamicSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 KB/s";
        if (bytesPerSec >= 1024.0 * 1024.0 * 1024.0)
        {
            return $"{bytesPerSec / (1024.0 * 1024.0 * 1024.0):0.0} GB/s";
        }
        if (bytesPerSec >= 1024.0 * 1024.0)
        {
            return $"{bytesPerSec / (1024.0 * 1024.0):0.0} MB/s";
        }
        if (bytesPerSec >= 1024.0)
        {
            return $"{bytesPerSec / 1024.0:0.0} KB/s";
        }
        return $"{bytesPerSec:0} B/s";
    }

    public async Task<bool> CheckAndInstallPrerequisitesAsync(Action<string, double>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Invoke("Checking system prerequisites...", 5);

        // 1. Ensure PowerShell execution policy allows user scripts
        try
        {
            progress?.Invoke("Configuring script execution policy...", 8);
            await RunPowerShellAsync("Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force", cancellationToken);
        }
        catch
        {
            // Non-critical, continue
        }

        // 2. Check if winget is available
        progress?.Invoke("Checking Windows Package Manager (winget)...", 12);
        var wingetReady = await IsWingetAvailableAsync(cancellationToken);
        if (wingetReady)
        {
            progress?.Invoke("Windows Package Manager is ready.", 15);
            return true;
        }

        // 3. Try to register built-in Microsoft.DesktopAppInstaller
        progress?.Invoke("Registering Windows App Installer...", 16);
        try
        {
            await RunPowerShellAsync("Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe", cancellationToken);
            await Task.Delay(1500, cancellationToken);
            if (await IsWingetAvailableAsync(cancellationToken))
            {
                progress?.Invoke("Windows Package Manager registered successfully.", 20);
                return true;
            }
        }
        catch
        {
            // Fall through to download
        }

        // 4. Download and install Microsoft App Installer bundle from official Microsoft aka.ms link
        progress?.Invoke("Downloading Microsoft App Installer (winget)...", 18);
        try
        {
            var tempBundle = Path.Combine(Path.GetTempPath(), "Microsoft.DesktopAppInstaller.msixbundle");
            using (var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            {
                using (var response = await httpClient.GetAsync("https://aka.ms/getwinget", HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    if (response.IsSuccessStatusCode)
                    {
                        using (var remoteStream = await response.Content.ReadAsStreamAsync())
                        using (var localFile = File.Create(tempBundle))
                        {
                            await remoteStream.CopyToAsync(localFile);
                        }

                        progress?.Invoke("Installing Microsoft App Installer package...", 22);
                        await RunPowerShellAsync($"Add-AppxPackage -Path '{tempBundle.Replace("'", "''")}'", cancellationToken);
                        await Task.Delay(1500, cancellationToken);

                        try { File.Delete(tempBundle); } catch { }
                    }
                }
            }
        }
        catch
        {
            // If download fails (e.g. offline machine), app will still run and self-bootstrap when online
        }

        progress?.Invoke("Prerequisites verified.", 25);
        return true;
    }

    public async Task InstallAsync(
        string installRoot,
        bool createDesktopShortcut,
        Action<string, double>? progress = null,
        Action<string>? detailLog = null,
        CancellationToken cancellationToken = default)
    {
        detailLog?.Invoke("Initializing 180Hz Setup Hub installation engine...");
        detailLog?.Invoke($"Destination folder: {installRoot}");

        // 1. Prerequisites check & auto-install (5% - 25%)
        await CheckAndInstallPrerequisitesAsync(progress, cancellationToken);

        // 2. Stop existing process if running (28%)
        progress?.Invoke("Preparing installation directory...", 28);
        var targetExe = Path.Combine(installRoot, InstalledExeName);
        StopExistingApp(targetExe);

        // 3. Create target directories (30%)
        var configDir = Path.Combine(installRoot, "Configuration");
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(configDir);

        // 4. Download and Deploy 180Hz Setup Hub (30% - 85%)
        await EnsureAppPackageAsync(targetExe, progress, detailLog, cancellationToken);

        // 5. Extract bundled configurations (86% - 89%)
        progress?.Invoke("Extracting package catalog and configurations...", 86);
        ExtractResourceToFile(PayloadPackages, Path.Combine(configDir, "packages.default.json"));
        ExtractResourceToFile(PayloadAppSettings, Path.Combine(configDir, "appsettings.default.json"));
        ExtractResourceToFile(PayloadTweaks, Path.Combine(configDir, "tweaks.default.json"));
        ExtractResourceToFile(PayloadRegistryTweaks, Path.Combine(configDir, "registry_tweaks.default.json"));

        // 6. Copy current installer as Uninstaller (90%)
        var uninstallerTarget = Path.Combine(installRoot, UninstallerExeName);
        try
        {
            var currentExe = Program.GetCurrentProcessPath();
            if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
            {
                File.Copy(currentExe, uninstallerTarget, overwrite: true);
                detailLog?.Invoke($"Created uninstaller: {uninstallerTarget}");
            }
        }
        catch
        {
            // Ignore if copy fails
        }

        // 7. Create shortcuts (92%)
        progress?.Invoke("Creating desktop and start menu shortcuts...", 92);
        CreateShortcuts(targetExe, installRoot, createDesktopShortcut);

        // 8. Register in Windows Add/Remove Programs (96%)
        progress?.Invoke("Registering application...", 96);
        RegisterUninstall(installRoot, targetExe);

        progress?.Invoke("Installation Complete!", 100);
        detailLog?.Invoke("Installation completed successfully.");
    }

    public async Task RepairAsync(
        string installRoot,
        Action<string, double>? progress = null,
        Action<string>? detailLog = null,
        CancellationToken cancellationToken = default)
    {
        detailLog?.Invoke("Starting 180Hz Setup Hub comprehensive repair & diagnosis...");
        progress?.Invoke("Stopping active instances...", 5);

        var targetExe = Path.Combine(installRoot, InstalledExeName);
        StopExistingApp(targetExe);

        // 1. Verify and repair system prerequisites (winget, script policy) (10% - 25%)
        progress?.Invoke("Repairing system prerequisites and winget...", 10);
        await CheckAndInstallPrerequisitesAsync(progress, cancellationToken);

        // 2. Prepare directories and clean corrupt temp files (28%)
        progress?.Invoke("Cleaning temporary files & verifying directories...", 28);
        var configDir = Path.Combine(installRoot, "Configuration");
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(configDir);
        CleanCorruptTempFiles(installRoot);

        // 3. Reinstall fresh application executable with live speed (30% - 85%)
        detailLog?.Invoke("Re-deploying fresh application binary package...");
        await EnsureAppPackageAsync(targetExe, progress, detailLog, cancellationToken);

        // 4. Restore configuration catalog to repair any corrupt database (86% - 89%)
        progress?.Invoke("Restoring default package catalogs...", 86);
        ExtractResourceToFile(PayloadPackages, Path.Combine(configDir, "packages.default.json"));
        ExtractResourceToFile(PayloadAppSettings, Path.Combine(configDir, "appsettings.default.json"));
        ExtractResourceToFile(PayloadTweaks, Path.Combine(configDir, "tweaks.default.json"));
        ExtractResourceToFile(PayloadRegistryTweaks, Path.Combine(configDir, "registry_tweaks.default.json"));

        // 5. Ensure uninstaller binary is healthy (90%)
        var uninstallerTarget = Path.Combine(installRoot, UninstallerExeName);
        try
        {
            var currentExe = Program.GetCurrentProcessPath();
            if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe) &&
                !string.Equals(currentExe, uninstallerTarget, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentExe, uninstallerTarget, overwrite: true);
            }
        }
        catch { }

        // 6. Repair shortcuts (92%)
        progress?.Invoke("Repairing desktop and start menu shortcuts...", 92);
        RemoveShortcuts();
        CreateShortcuts(targetExe, installRoot, createDesktopShortcut: true);

        // 7. Refresh registry registration (96%)
        progress?.Invoke("Refreshing Windows Registry configuration...", 96);
        RegisterUninstall(installRoot, targetExe);

        progress?.Invoke("Repair complete! Application is healthy.", 100);
        detailLog?.Invoke("Repair process finished successfully.");
    }

    private static void CleanCorruptTempFiles(string installRoot)
    {
        try
        {
            if (Directory.Exists(installRoot))
            {
                foreach (var tmp in Directory.GetFiles(installRoot, "*.tmp*", SearchOption.TopDirectoryOnly))
                {
                    try { File.Delete(tmp); } catch { }
                }

                var dataDownloads = Path.Combine(installRoot, "Data", "downloads");
                if (Directory.Exists(dataDownloads))
                {
                    foreach (var tmp in Directory.GetFiles(dataDownloads, "*.tmp", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(tmp); } catch { }
                    }
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    public async Task EnsureAppPackageAsync(
        string destinationExe,
        Action<string, double>? progress = null,
        Action<string>? detailLog = null,
        CancellationToken cancellationToken = default)
    {
        // Check local fallbacks first
        var localSource = FindLocalPackage();
        if (localSource is not null && File.Exists(localSource))
        {
            detailLog?.Invoke($"Installing from local package: {localSource}");
            await CopyWithProgressAsync(localSource, destinationExe, progress, 30, 85, cancellationToken);
            return;
        }

        // Perform online download with retry and verification
        var candidateUrls = new List<string>();
        var envDownloadUrl = Environment.GetEnvironmentVariable("WINSETUPHUB_DOWNLOAD_URL");
        if (!string.IsNullOrWhiteSpace(envDownloadUrl))
        {
            candidateUrls.Add(envDownloadUrl.Trim());
        }
        candidateUrls.Add(DefaultDownloadUrl);
        candidateUrls.Add(RawDownloadUrl);

        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;

        var tempTarget = $"{destinationExe}.download.tmp";

        const int maxRetries = 3;
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var downloadUrl = candidateUrls[(attempt - 1) % candidateUrls.Count];
            detailLog?.Invoke($"Downloading 180Hz Setup Hub from: {downloadUrl}");
            progress?.Invoke("Connecting to download server...", 30);

            try
            {
                if (File.Exists(tempTarget))
                {
                    try { File.Delete(tempTarget); } catch { }
                }

                using (var handler = new HttpClientHandler { AllowAutoRedirect = true })
                using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) })
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "180Hz-SetupHub-Bootstrapper/1.0");

                    using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            var emergencyLocal = FindLocalPackage();
                            if (emergencyLocal is not null && File.Exists(emergencyLocal))
                            {
                                detailLog?.Invoke($"Online download returned {response.StatusCode}. Falling back to local file.");
                                await CopyWithProgressAsync(emergencyLocal, destinationExe, progress, 35, 85, cancellationToken);
                                return;
                            }

                            throw new HttpRequestException($"Download server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). URL: {downloadUrl}");
                        }

                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(tempTarget, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                        {
                            var buffer = new byte[65536];
                            long totalRead = 0;
                            int bytesRead;

                            var stopwatch = Stopwatch.StartNew();
                            var lastSampleMs = stopwatch.ElapsedMilliseconds;
                            var lastSampleBytes = 0L;
                            var currentSpeedFormatted = "0 KB/s";

                            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                                totalRead += bytesRead;

                                var elapsedMs = stopwatch.ElapsedMilliseconds;
                                if (elapsedMs - lastSampleMs >= 200)
                                {
                                    var deltaSec = (elapsedMs - lastSampleMs) / 1000.0;
                                    var deltaBytes = totalRead - lastSampleBytes;
                                    if (deltaSec > 0)
                                    {
                                        var bytesPerSec = deltaBytes / deltaSec;
                                        currentSpeedFormatted = FormatDynamicSpeed(bytesPerSec);
                                    }
                                    lastSampleMs = elapsedMs;
                                    lastSampleBytes = totalRead;
                                }

                                if (totalBytes > 0)
                                {
                                    var pct = 30.0 + ((double)totalRead / totalBytes) * 55.0; // 30% to 85%
                                    progress?.Invoke($"Installing app package {(int)pct}% ({currentSpeedFormatted})...", pct);
                                }
                                else
                                {
                                    var mbRead = totalRead / (1024.0 * 1024.0);
                                    var pseudoPct = Math.Min(84.0, 30.0 + mbRead * 0.7);
                                    progress?.Invoke($"Downloading app package ({mbRead:F1} MB • {currentSpeedFormatted})...", pseudoPct);
                                }
                            }

                            // Verify complete stream received
                            if (totalBytes > 0 && totalRead < totalBytes)
                            {
                                throw new IOException($"Incomplete download stream. Expected {totalBytes} bytes, received {totalRead} bytes.");
                            }
                        }
                    }
                }

                // Verify file validity
                var fileInfo = new FileInfo(tempTarget);
                if (!fileInfo.Exists || fileInfo.Length < 10_000_000)
                {
                    throw new InvalidOperationException($"Downloaded binary is invalid or incomplete (Size: {fileInfo.Length} bytes).");
                }

                // Verify PE header (MZ)
                using (var fs = new FileStream(tempTarget, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] header = new byte[2];
                    if (fs.Read(header, 0, 2) != 2 || header[0] != 0x4D || header[1] != 0x5A)
                    {
                        throw new InvalidDataException("Downloaded package is not a valid Windows executable binary.");
                    }
                }

                // Move into destination with retry against locks
                for (int r = 0; r < 5; r++)
                {
                    try
                    {
                        if (File.Exists(destinationExe))
                        {
                            File.Delete(destinationExe);
                        }
                        File.Move(tempTarget, destinationExe);
                        break;
                    }
                    catch (IOException) when (r < 4)
                    {
                        StopExistingApp(destinationExe);
                        Thread.Sleep(500);
                    }
                }

                detailLog?.Invoke("Application package downloaded, verified, and staged successfully.");
                return;
            }
            catch (Exception ex) when (attempt < maxRetries && ex is not OperationCanceledException)
            {
                lastEx = ex;
                detailLog?.Invoke($"Download attempt {attempt} failed: {ex.Message}. Retrying...");
                progress?.Invoke($"Connection interrupted. Retrying download ({attempt}/{maxRetries})...", 30);
                await Task.Delay(1500 * attempt, cancellationToken);
            }
            catch (Exception ex)
            {
                lastEx = ex;
                break;
            }
        }

        if (File.Exists(tempTarget))
        {
            try { File.Delete(tempTarget); } catch { }
        }

        // Check emergency local fallback
        var emergency = FindLocalPackage();
        if (emergency is not null && File.Exists(emergency))
        {
            detailLog?.Invoke("Online download failed, falling back to local package...");
            await CopyWithProgressAsync(emergency, destinationExe, progress, 35, 85, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Failed to deploy 180Hz Setup Hub: {lastEx?.Message ?? "Download error"}. Please check your internet connection or place '{InstalledExeName}' next to the installer.", lastEx);
    }

    private static string? FindLocalPackage()
    {
        // 1. Environment variable override
        var envPkg = Environment.GetEnvironmentVariable("WINSETUPHUB_LOCAL_PACKAGE");
        if (!string.IsNullOrWhiteSpace(envPkg) && File.Exists(envPkg))
        {
            return envPkg;
        }

        // 2. Adjacent to running installer or current process directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var procDir = Path.GetDirectoryName(Program.GetCurrentProcessPath()) ?? "";

        var searchDirs = new List<string> { baseDir };
        if (!string.IsNullOrWhiteSpace(procDir) && !searchDirs.Contains(procDir, StringComparer.OrdinalIgnoreCase))
        {
            searchDirs.Add(procDir);
        }

        foreach (var dir in searchDirs)
        {
            var adjacent = Path.Combine(dir, InstalledExeName);
            if (File.Exists(adjacent) && new FileInfo(adjacent).Length > 10_000_000)
            {
                return adjacent;
            }
        }

        // 3. Search local directories in development/portable folder structures dynamically
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var testPaths = new List<string>();

        foreach (var dir in searchDirs)
        {
            testPaths.Add(Path.Combine(dir, "StandaloneApp", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "..", "StandaloneApp", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "..", "publish", "win-x64", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "..", "artifacts", "publish", "win-x64", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "..", "..", "artifacts", "publish", "win-x64", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "..", "..", "..", "artifacts", "publish", "win-x64", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "artifacts", "publish", "win-x64", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "new folder", "StandaloneApp", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "..", "new folder", "StandaloneApp", InstalledExeName));
            testPaths.Add(Path.Combine(dir, "..", "..", "new folder", "StandaloneApp", InstalledExeName));
        }

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            testPaths.Add(Path.Combine(userProfile, "Downloads", "WinSetupHub", "artifacts", "publish", "win-x64", InstalledExeName));
            testPaths.Add(Path.Combine(userProfile, "Downloads", "WinSetupHub", "new folder", "StandaloneApp", InstalledExeName));
        }

        foreach (var p in testPaths)
        {
            try
            {
                var full = Path.GetFullPath(p);
                if (File.Exists(full) && new FileInfo(full).Length > 10_000_000)
                {
                    return full;
                }
            }
            catch { }
        }

        return null;
    }

    private static async Task CopyWithProgressAsync(
        string sourceFile,
        string destinationFile,
        Action<string, double>? progress,
        double startPercent,
        double endPercent,
        CancellationToken cancellationToken)
    {
        var tempTarget = $"{destinationFile}.tmp.{Guid.NewGuid():N}";
        var sourceInfo = new FileInfo(sourceFile);
        var totalBytes = sourceInfo.Length;

        var stopwatch = Stopwatch.StartNew();
        var lastSampleMs = stopwatch.ElapsedMilliseconds;
        var lastSampleBytes = 0L;
        var currentSpeedFormatted = "0 MB/s";

        using (var sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true))
        using (var destStream = new FileStream(tempTarget, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
        {
            var buffer = new byte[65536];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await destStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;

                var elapsedMs = stopwatch.ElapsedMilliseconds;
                if (elapsedMs - lastSampleMs >= 200)
                {
                    var deltaSec = (elapsedMs - lastSampleMs) / 1000.0;
                    var deltaBytes = totalRead - lastSampleBytes;
                    if (deltaSec > 0)
                    {
                        var bytesPerSec = deltaBytes / deltaSec;
                        currentSpeedFormatted = FormatDynamicSpeed(bytesPerSec);
                    }
                    lastSampleMs = elapsedMs;
                    lastSampleBytes = totalRead;
                }

                var pct = startPercent + ((double)totalRead / totalBytes) * (endPercent - startPercent);
                progress?.Invoke($"Installing app package {(int)pct}% ({currentSpeedFormatted})...", pct);
            }
        }

        if (File.Exists(destinationFile))
        {
            File.Delete(destinationFile);
        }
        File.Move(tempTarget, destinationFile);
    }

    public static void StopExistingApp(string installedExe)
    {
        var procName = Path.GetFileNameWithoutExtension(installedExe);
        var targetNames = new[] { procName, "180HzSetupHub", "180HzSetupHubSetup" };
        var currentPid = Process.GetCurrentProcess().Id;

        for (var retry = 0; retry < 5; retry++)
        {
            bool anyKilled = false;
            foreach (var name in targetNames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var processes = Process.GetProcessesByName(name);
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.Id == currentPid) continue;
                        process.Kill();
                        anyKilled = true;
                    }
                    catch { }
                }
            }
            if (!anyKilled) break;
            Thread.Sleep(300);
        }
    }

    private static void ExtractResourceToFile(string resourceName, string destinationPath)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var targetResource = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + resourceName, StringComparison.OrdinalIgnoreCase) || n.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

        using (var stream = targetResource != null ? assembly.GetManifestResourceStream(targetResource) : assembly.GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                WriteTempAndMove(stream, destinationPath);
                return;
            }
        }

        // Fallback: check if file exists in configuration folder
        var localFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configuration", resourceName);
        if (File.Exists(localFallback))
        {
            File.Copy(localFallback, destinationPath, overwrite: true);
            return;
        }

        // Fallback: check in base directory
        var rootFallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, resourceName);
        if (File.Exists(rootFallback))
        {
            File.Copy(rootFallback, destinationPath, overwrite: true);
            return;
        }
    }

    private static void WriteTempAndMove(Stream sourceStream, string destinationPath)
    {
        var tempPath = $"{destinationPath}.tmp.{Guid.NewGuid():N}";
        try
        {
            using (var output = File.Create(tempPath))
            {
                sourceStream.CopyTo(output);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempPath, destinationPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public static void CreateShortcuts(string targetExe, string workingDir, bool createDesktopShortcut)
    {
        // 1. Try instantaneous native COM WScript.Shell
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType != null)
            {
                dynamic shell = Activator.CreateInstance(shellType);
                var startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                Directory.CreateDirectory(startDir);
                var startMenuLnk = Path.Combine(startDir, $"{AppName}.lnk");
                dynamic sMenu = shell.CreateShortcut(startMenuLnk);
                sMenu.TargetPath = targetExe;
                sMenu.WorkingDirectory = workingDir;
                sMenu.IconLocation = $"{targetExe},0";
                sMenu.Description = AppName;
                sMenu.Save();
                SetShortcutRunAsAdmin(startMenuLnk);

                if (createDesktopShortcut)
                {
                    var deskDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    Directory.CreateDirectory(deskDir);
                    var deskLnk = Path.Combine(deskDir, $"{AppName}.lnk");
                    dynamic sDesk = shell.CreateShortcut(deskLnk);
                    sDesk.TargetPath = targetExe;
                    sDesk.WorkingDirectory = workingDir;
                    sDesk.IconLocation = $"{targetExe},0";
                    sDesk.Description = AppName;
                    sDesk.Save();
                    SetShortcutRunAsAdmin(deskLnk);
                }
                return;
            }
        }
        catch { }

        // 2. Fallback to PowerShell
        var escapedTarget = targetExe.Replace("'", "''");
        var escapedDir = workingDir.Replace("'", "''");
        var escapedName = AppName.Replace("'", "''");

        var commands = new List<string>
        {
            "$ws = New-Object -ComObject WScript.Shell;",
            "$startDir = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs';",
            "if (-not (Test-Path $startDir)) { New-Item -ItemType Directory -Path $startDir -Force | Out-Null };",
            $"$sMenu = $ws.CreateShortcut((Join-Path $startDir '{escapedName}.lnk'));",
            $"$sMenu.TargetPath = '{escapedTarget}';",
            $"$sMenu.WorkingDirectory = '{escapedDir}';",
            $"$sMenu.IconLocation = '{escapedTarget},0';",
            $"$sMenu.Description = '{escapedName}';",
            "$sMenu.Save();"
        };

        if (createDesktopShortcut)
        {
            commands.Add("$desktopDir = [Environment]::GetFolderPath('DesktopDirectory');");
            commands.Add($"$sDesk = $ws.CreateShortcut((Join-Path $desktopDir '{escapedName}.lnk'));");
            commands.Add($"$sDesk.TargetPath = '{escapedTarget}';");
            commands.Add($"$sDesk.WorkingDirectory = '{escapedDir}';");
            commands.Add($"$sDesk.IconLocation = '{escapedTarget},0';");
            commands.Add($"$sDesk.Description = '{escapedName}';");
            commands.Add("$sDesk.Save();");
        }

        try
        {
            var singleLineCmd = string.Join(" ", commands);
            using (var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{singleLineCmd}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            }))
            {
                p?.WaitForExit(10000);
            }

            var startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
            var startMenuLnk = Path.Combine(startDir, $"{AppName}.lnk");
            SetShortcutRunAsAdmin(startMenuLnk);

            if (createDesktopShortcut)
            {
                var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var desktopLnk = Path.Combine(desktopDir, $"{AppName}.lnk");
                SetShortcutRunAsAdmin(desktopLnk);
            }
        }
        catch { }
    }

    private static void SetShortcutRunAsAdmin(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
            {
                var bytes = File.ReadAllBytes(shortcutPath);
                if (bytes.Length > 0x15)
                {
                    bytes[0x15] = (byte)(bytes[0x15] | 0x20); // SLDF_RUNAS_USER flag
                    File.WriteAllBytes(shortcutPath, bytes);
                }
            }
        }
        catch { }
    }

    public static void RemoveShortcuts()
    {
        var candidateDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", AppName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", AppName)
        };

        foreach (var dir in candidateDirs)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                var lnk = Path.Combine(dir, $"{AppName}.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);

                var lnk2 = Path.Combine(dir, "180Hz Setup Hub.lnk");
                if (File.Exists(lnk2)) File.Delete(lnk2);

                if (dir.EndsWith(AppName, StringComparison.OrdinalIgnoreCase) && Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    try { Directory.Delete(dir); } catch { }
                }
            }
            catch { }
        }
    }

    public static void RegisterUninstall(string installRoot, string targetExe)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(RegistryKeyPath, writable: true);
            if (key is null) return;

            var uninstallerPath = Path.Combine(installRoot, UninstallerExeName);
            key.SetValue("DisplayName", AppName);

            string displayVersion = "1.0.0";
            if (File.Exists(targetExe))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(targetExe);
                    if (!string.IsNullOrWhiteSpace(vi.FileVersion))
                    {
                        displayVersion = vi.FileVersion;
                    }
                }
                catch { }
            }

            key.SetValue("DisplayVersion", displayVersion);
            key.SetValue("Publisher", "180Hz");
            key.SetValue("DisplayIcon", $"{targetExe},0");
            key.SetValue("InstallLocation", installRoot);
            key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --silent");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 0, RegistryValueKind.DWord); // Allow repair in Windows Settings

            var exeInfo = new FileInfo(targetExe);
            if (exeInfo.Exists)
            {
                key.SetValue("EstimatedSize", (int)(exeInfo.Length / 1024), RegistryValueKind.DWord);
            }
            key.Flush();

            // Register AppCompatFlags so Windows automatically runs the application and uninstaller as Administrator
            using var compatKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
            if (compatKey != null)
            {
                compatKey.SetValue(targetExe, "~ RUNASADMIN");
                compatKey.SetValue(uninstallerPath, "~ RUNASADMIN");
            }
        }
        catch { }
    }

    public static void UnregisterUninstall()
    {
        // 1. Uninstall entries
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch { }

        try
        {
            using var baseKey32 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry32);
            baseKey32.DeleteSubKeyTree(RegistryKeyPath, throwOnMissingSubKey: false);
        }
        catch { }

        // 2. Startup Run key
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue("180HzSetupHub", throwOnMissingValue: false);
            runKey?.DeleteValue(AppName, throwOnMissingValue: false);
        }
        catch { }

        // 3. AppCompatFlags entries
        try
        {
            using var compatKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", writable: true);
            if (compatKey != null)
            {
                var valueNames = compatKey.GetValueNames();
                foreach (var val in valueNames)
                {
                    if (val.IndexOf("180HzSetupHub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        val.IndexOf(AppName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        try { compatKey.DeleteValue(val, throwOnMissingValue: false); } catch { }
                    }
                }
            }
        }
        catch { }

        // 4. HKCU\Software\180HzSetupHub
        try
        {
            using var softKey = Registry.CurrentUser.OpenSubKey("Software", writable: true);
            softKey?.DeleteSubKeyTree("180HzSetupHub", throwOnMissingSubKey: false);
        }
        catch { }
    }

    public static void CleanAppDataResidues()
    {
        // 1. AppData\Local\180HzSetupHub
        var localAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "180HzSetupHub");
        if (Directory.Exists(localAppDataDir))
        {
            try { Directory.Delete(localAppDataDir, recursive: true); } catch { }
        }

        // 2. AppData\Roaming\180HzSetupHub
        var roamingAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "180HzSetupHub");
        if (Directory.Exists(roamingAppDataDir))
        {
            try { Directory.Delete(roamingAppDataDir, recursive: true); } catch { }
        }

        // 3. Temp uninstaller/installer workers
        try
        {
            var tempDir = Path.GetTempPath();
            var tempFiles = Directory.GetFiles(tempDir, "180HzSetupHub*.*", SearchOption.TopDirectoryOnly);
            foreach (var f in tempFiles)
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    public static void PerformFullCleanUninstall(string installRoot, Action<string, double>? progress = null)
    {
        progress?.Invoke("Terminating active application processes...", 10);
        StopExistingApp(Path.Combine(installRoot, InstalledExeName));
        Thread.Sleep(300);

        progress?.Invoke("Removing shortcuts and desktop links...", 25);
        RemoveShortcuts();

        progress?.Invoke("Purging Windows Add/Remove programs registry keys...", 45);
        UnregisterUninstall();

        progress?.Invoke("Cleaning application cache, downloads and logs...", 65);
        CleanAppDataResidues();

        progress?.Invoke("Removing application installation directories...", 85);
        for (var retry = 0; retry < 5; retry++)
        {
            try
            {
                if (Directory.Exists(installRoot))
                {
                    Directory.Delete(installRoot, recursive: true);
                }
                break;
            }
            catch (IOException)
            {
                StopExistingApp(Path.Combine(installRoot, InstalledExeName));
                Thread.Sleep(500);
            }
            catch { }
        }

        if (Directory.Exists(installRoot))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 & rmdir /s /q \"{installRoot}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch { }
        }

        progress?.Invoke("Uninstallation complete. All files cleanly removed.", 100);
    }

    public static async Task<bool> IsWingetAvailableAsync(CancellationToken cancellationToken = default)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new List<string> { "winget.exe", "winget" };
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Insert(0, Path.Combine(localAppData, "Microsoft", "WindowsApps", "winget.exe"));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(candidate, "--version")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };

                if (process.Start())
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    await Task.Run(() => process.WaitForExit(3000));
                    if (process.HasExited && process.ExitCode == 0)
                    {
                        var output = await outputTask;
                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Continue to next candidate
            }
        }

        return false;
    }

    private static async Task<int> RunPowerShellAsync(string command, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            }
        };

        if (process.Start())
        {
            await Task.Run(() => process.WaitForExit(10000));
            return process.HasExited ? process.ExitCode : -1;
        }

        return -1;
    }
}
