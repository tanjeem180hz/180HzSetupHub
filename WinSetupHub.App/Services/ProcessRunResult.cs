namespace WinSetupHub.App.Services;

public sealed record ProcessRunResult(int ExitCode, string Output, string Error);
