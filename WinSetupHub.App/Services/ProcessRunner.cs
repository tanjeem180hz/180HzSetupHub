using System.Diagnostics;
using System.Text;
using WinSetupHub.App.Services.Progress;

namespace WinSetupHub.App.Services;

public sealed class ProcessRunner
{
    private readonly object _logLock = new();
    private readonly string _sessionLogPath;

    public ProcessRunner(StoragePaths paths)
    {
        _sessionLogPath = Path.Combine(paths.Logs, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Action<string>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();

        AppendLog($"> {fileName} {string.Join(" ", arguments.Select(FormatArgumentForLog))}");

        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(-1, output.ToString(), "Process could not be started.");
            }

            var outputTask = ReadStreamAsync(process.StandardOutput, output, onOutput);
            var errorTask = ReadStreamAsync(process.StandardError, error, onOutput);
            await process.WaitForExitAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask);
            return new ProcessRunResult(process.ExitCode, output.ToString(), error.ToString());
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch (Exception ex)
        {
            AppendLog(ex.ToString());
            return new ProcessRunResult(-1, output.ToString(), ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Nothing useful to do if Windows already closed the process.
        }
    }

    private void AppendLog(string message)
    {
        lock (_logLock)
        {
            File.AppendAllText(_sessionLogPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
    }

    private async Task ReadStreamAsync(TextReader reader, StringBuilder capture, Action<string>? onOutput)
    {
        var buffer = new char[512];
        var segment = new StringBuilder();
        var lastPartialProgress = string.Empty;
        var lastPartialProgressAt = DateTime.MinValue;

        while (true)
        {
            var read = await reader.ReadAsync(buffer, 0, buffer.Length);
            if (read <= 0)
            {
                break;
            }

            var chunk = new string(buffer, 0, read);
            capture.Append(chunk);

            foreach (var character in chunk)
            {
                if (character is '\r' or '\n')
                {
                    EmitSegment(segment, onOutput, writeToSessionLog: true);
                    segment.Clear();
                    continue;
                }

                if (character == '\b')
                {
                    if (segment.Length > 0)
                    {
                        segment.Length--;
                    }

                    continue;
                }

                if (!char.IsControl(character) || character == '\t')
                {
                    segment.Append(character);
                }
            }

            var partial = ProgressOutputParser.Normalize(segment.ToString());
            if (!IsProgressOutput(partial) || partial == lastPartialProgress)
            {
                continue;
            }

            var now = DateTime.Now;
            if ((now - lastPartialProgressAt).TotalMilliseconds < 250)
            {
                continue;
            }

            lastPartialProgress = partial;
            lastPartialProgressAt = now;
            onOutput?.Invoke(partial);
        }

        EmitSegment(segment, onOutput, writeToSessionLog: true);
    }

    private void EmitSegment(StringBuilder segment, Action<string>? onOutput, bool writeToSessionLog)
    {
        var message = ProgressOutputParser.Normalize(segment.ToString());
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (writeToSessionLog && !IsProgressOutput(message))
        {
            AppendLog(message);
        }

        onOutput?.Invoke(message);
    }

    private static bool IsProgressOutput(string message)
    {
        return ProgressOutputParser.TryReadTransferPercent(message, out _)
            || ProgressOutputParser.TryReadPercent(message, out _);
    }

    private static string FormatArgumentForLog(string argument)
    {
        return argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }
}
