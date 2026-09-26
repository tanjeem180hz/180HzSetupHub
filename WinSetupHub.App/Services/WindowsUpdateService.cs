using System.Diagnostics;

namespace WinSetupHub.App.Services;

public sealed class WindowsUpdateService
{
    private readonly ProcessRunner _runner;

    public WindowsUpdateService(ProcessRunner runner)
    {
        _runner = runner;
    }

    public async Task TriggerInteractiveScanAsync(Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        log?.Invoke("Opening Windows Update settings.");

        Process.Start(new ProcessStartInfo("ms-settings:windowsupdate")
        {
            UseShellExecute = true
        });

        log?.Invoke("Requesting an interactive Windows Update scan.");
        await _runner.RunAsync("UsoClient.exe", ["StartInteractiveScan"], log, cancellationToken);
    }
}
