using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace WinSetupHub.Setup;

internal static class Program
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconInformation = 0x00000040;
    private const uint MbIconError = 0x00000010;

    public static string GetCurrentProcessPath()
    {
        try
        {
            var procPath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
            {
                return procPath!;
            }
        }
        catch
        {
            // Fallback
        }

        return Assembly.GetExecutingAssembly().Location ?? string.Empty;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        var isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("-silent", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("/S", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("-s", StringComparison.OrdinalIgnoreCase));

        var exeName = Path.GetFileName(GetCurrentProcessPath());
        var isUninstall = exeName.Equals(InstallerService.UninstallerExeName, StringComparison.OrdinalIgnoreCase) ||
                          args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                        a.Equals("-uninstall", StringComparison.OrdinalIgnoreCase));

        var workerIdx = Array.FindIndex(args, a => a.Equals("--uninstall-worker", StringComparison.OrdinalIgnoreCase));
        if (workerIdx >= 0 && workerIdx + 1 < args.Length)
        {
            var installRoot = args[workerIdx + 1];
            return RunUninstallWorker(installRoot, isSilent);
        }

        if (isUninstall)
        {
            if (isSilent)
            {
                return RunSilentUninstall();
            }

            var app = new Application();
            return app.Run(new UninstallerWindow());
        }

        if (isSilent)
        {
            return RunSilentInstall();
        }

        // Standard interactive Windows App Installer UI
        var wpfApp = new Application();
        return wpfApp.Run(new InstallerWindow());
    }

    private static int RunSilentInstall()
    {
        try
        {
            var installer = new InstallerService();
            var installRoot = InstallerService.GetDefaultInstallRoot();
            installer.InstallAsync(installRoot, createDesktopShortcut: true).GetAwaiter().GetResult();

            var installedExe = Path.Combine(installRoot, InstallerService.InstalledExeName);
            if (File.Exists(installedExe))
            {
                Process.Start(new ProcessStartInfo(installedExe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = installRoot
                });
            }

            return 0;
        }
        catch (Exception ex)
        {
            ShowMessage($"Installation failed: {ex.Message}", "180Hz Setup Hub Setup", MbIconError);
            return 1;
        }
    }

    private static int RunSilentUninstall()
    {
        try
        {
            var installRoot = InstallerService.GetDefaultInstallRoot();
            var currentExe = GetCurrentProcessPath();

            if (!string.IsNullOrEmpty(currentExe) && currentExe.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase))
            {
                var tempWorker = Path.Combine(Path.GetTempPath(), $"180HzSetupHub_Uninstall_{Guid.NewGuid():N}.exe");
                File.Copy(currentExe, tempWorker, overwrite: true);

                Process.Start(new ProcessStartInfo(tempWorker)
                {
                    Arguments = $"--uninstall-worker \"{installRoot}\" --silent",
                    UseShellExecute = true
                });

                return 0;
            }

            InstallerService.StopExistingApp(Path.Combine(installRoot, InstallerService.InstalledExeName));
            InstallerService.RemoveShortcuts();
            InstallerService.UnregisterUninstall();

            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }

            return 0;
        }
        catch
        {
            return 1;
        }
    }

    private static int RunUninstallWorker(string installRoot, bool isSilent)
    {
        try
        {
            // Give parent process time to terminate
            Thread.Sleep(1500);

            InstallerService.StopExistingApp(Path.Combine(installRoot, InstallerService.InstalledExeName));
            InstallerService.RemoveShortcuts();
            InstallerService.UnregisterUninstall();

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
                catch
                {
                    Thread.Sleep(1000);
                }
            }

            if (!isSilent)
            {
                ShowMessage("180Hz Setup Hub was successfully removed from your computer.", "180Hz Setup Hub", MbIconInformation);
            }

            // Clean up temporary worker executable via delayed cmd
            var currentPath = GetCurrentProcessPath();
            if (!string.IsNullOrEmpty(currentPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c timeout /t 2 & del /f /q \"{currentPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (!isSilent)
            {
                ShowMessage($"Uninstall error: {ex.Message}", "180Hz Setup Hub", MbIconError);
            }
            return 1;
        }
    }

    private static void ShowMessage(string text, string caption, uint iconType)
    {
        MessageBox(IntPtr.Zero, text, caption, MbOk | iconType);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
