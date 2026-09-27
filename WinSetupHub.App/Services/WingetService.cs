using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    /// <summary>
    /// Thin wrapper around the `winget` CLI. All calls are async and stream
    /// console output back via onOutputLine so the UI can show live progress
    /// instead of freezing on a single big call.
    /// </summary>
    public class WingetService
    {
        public async Task<bool> IsAvailableAsync()
        {
            try
            {
                var output = await RunWingetAsync("--version");
                return !string.IsNullOrWhiteSpace(output);
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<AppItem>> GetInstalledAppsAsync()
        {
            var output = await RunWingetAsync("list --accept-source-agreements");
            return ParseTable(output);
        }

        public async Task<List<AppItem>> GetUpgradableAppsAsync()
        {
            var output = await RunWingetAsync("upgrade --accept-source-agreements");
            return ParseTable(output);
        }

        public async Task<List<AppItem>> SearchAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<AppItem>();
            var output = await RunWingetAsync($"search \"{query}\" --accept-source-agreements");
            return ParseTable(output);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, AppMetadata?> _metadataCache =
            new(StringComparer.OrdinalIgnoreCase);

        public async Task<AppMetadata?> GetMetadataAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (_metadataCache.TryGetValue(id, out var cached)) return cached;

            try
            {
                var output = await RunWingetAsync($"show --id \"{id}\" --exact --accept-source-agreements");
                if (string.IsNullOrWhiteSpace(output))
                {
                    _metadataCache[id] = null;
                    return null;
                }

                string? homepage = null;
                string? publisherUrl = null;
                string? supportUrl = null;

                using var reader = new System.IO.StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Homepage:", StringComparison.Ordinal))
                    {
                        var val = trimmed.Substring("Homepage:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) homepage = val;
                    }
                    else if (trimmed.StartsWith("Publisher Url:", StringComparison.Ordinal))
                    {
                        var val = trimmed.Substring("Publisher Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) publisherUrl = val;
                    }
                    else if (trimmed.StartsWith("Publisher Support Url:", StringComparison.Ordinal))
                    {
                        var val = trimmed.Substring("Publisher Support Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) supportUrl = val;
                    }
                }

                var result = (homepage != null || publisherUrl != null || supportUrl != null)
                    ? new AppMetadata(homepage, publisherUrl, supportUrl)
                    : null;

                _metadataCache[id] = result;
                return result;
            }
            catch
            {
                _metadataCache[id] = null;
                return null;
            }
        }

        public record WingetInstallerInfo(string? Url, string? Type, string? Sha256);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WingetInstallerInfo?> _installerInfoCache =
            new(StringComparer.OrdinalIgnoreCase);

        public async Task<WingetInstallerInfo?> GetInstallerInfoAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (_installerInfoCache.TryGetValue(id, out var cached)) return cached;

            try
            {
                var output = await RunWingetAsync($"show --id \"{id}\" --exact --accept-source-agreements");
                if (string.IsNullOrWhiteSpace(output))
                {
                    _installerInfoCache[id] = null;
                    return null;
                }

                string? installerUrl = null;
                string? installerType = null;
                string? sha256 = null;

                using var reader = new System.IO.StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Installer Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) installerUrl = val;
                    }
                    else if (trimmed.StartsWith("Installer Type:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer Type:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) installerType = val;
                    }
                    else if (trimmed.StartsWith("Installer SHA256:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer SHA256:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) sha256 = val;
                    }
                }

                var result = !string.IsNullOrWhiteSpace(installerUrl)
                    ? new WingetInstallerInfo(installerUrl, installerType, sha256)
                    : null;

                _installerInfoCache[id] = result;
                return result;
            }
            catch
            {
                _installerInfoCache[id] = null;
                return null;
            }
        }

        public async Task<bool> InstallAsync(string id, string? source = null, Action<string>? onOutputLine = null)
        {
            if (id.Equals("Microsoft.WindowsStore", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Process.Start(new ProcessStartInfo("ms-windows-store://") { UseShellExecute = true });
                    onOutputLine?.Invoke("Opened Microsoft Store.");
                    return true;
                }
                catch
                {
                    // Fallback to winget install
                }
            }

            var srcArg = !string.IsNullOrWhiteSpace(source) && !source.Equals("winget", StringComparison.OrdinalIgnoreCase)
                ? $"--source \"{source}\" "
                : "";

            var success = await RunActionAsync($"install --id \"{id}\" {srcArg}-e --silent --accept-package-agreements --accept-source-agreements", onOutputLine);
            if (!success)
            {
                success = await RunActionAsync($"install \"{id}\" {srcArg}--silent --accept-package-agreements --accept-source-agreements", onOutputLine);
            }
            return success;
        }

        public Task<bool> UpgradeAsync(string id, Action<string>? onOutputLine = null) =>
            RunActionAsync($"upgrade --id \"{id}\" -e --silent --accept-package-agreements --accept-source-agreements", onOutputLine);

        public Task<bool> UpgradeAllAsync(Action<string>? onOutputLine = null) =>
            RunActionAsync("upgrade --all --silent --accept-package-agreements --accept-source-agreements", onOutputLine);

        public async Task<bool> UninstallAsync(string id, Action<string>? onOutputLine = null)
        {
            var success = await RunActionAsync($"uninstall --id \"{id}\" -e --silent --accept-source-agreements", onOutputLine);
            if (!success)
            {
                success = await RunActionAsync($"uninstall --id \"{id}\" --silent --accept-source-agreements", onOutputLine);
            }
            return success;
        }

        // ---- internals ----

        private async Task<bool> RunActionAsync(string arguments, Action<string>? onOutputLine)
        {
            var output = await RunWingetAsync(arguments, onOutputLine);
            return !output.Contains("failed", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("No package found", StringComparison.OrdinalIgnoreCase);
        }

        private Task<string> RunWingetAsync(string arguments, Action<string>? onOutputLine = null)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                };

                using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var sb = new StringBuilder();

                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    sb.AppendLine(e.Data);
                    onOutputLine?.Invoke(e.Data);
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data == null) return;
                    sb.AppendLine(e.Data);
                    onOutputLine?.Invoke(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                return sb.ToString();
            });
        }

        /// <summary>
        /// winget prints a fixed-width, column-aligned table. We find the header row,
        /// use its column start offsets to slice every following line, and skip the
        /// "----" separator row beneath it.
        /// </summary>
        private static List<AppItem> ParseTable(string raw)
        {
            var items = new List<AppItem>();
            if (string.IsNullOrWhiteSpace(raw)) return items;

            var lines = raw.Replace("\r", "").Split('\n');

            int headerIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Name") && lines[i].Contains("Id"))
                {
                    headerIndex = i;
                    break;
                }
            }
            if (headerIndex == -1 || headerIndex + 1 >= lines.Length) return items;

            var header = lines[headerIndex];
            int nameCol = header.IndexOf("Name", StringComparison.Ordinal);
            int idCol = header.IndexOf("Id", StringComparison.Ordinal);
            int versionCol = header.IndexOf("Version", StringComparison.Ordinal);
            int availCol = header.IndexOf("Available", StringComparison.Ordinal);
            int sourceCol = header.IndexOf("Source", StringComparison.Ordinal);

            string Slice(string line, int start, int end)
            {
                if (start < 0 || start >= line.Length) return "";
                int safeEnd = end < 0 ? line.Length : Math.Min(end, line.Length);
                return safeEnd <= start ? "" : line[start..safeEnd].Trim();
            }

            // headerIndex + 1 is the "----" separator line, so data starts at + 2.
            for (int i = headerIndex + 2; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("-")) continue;

                int versionEnd = availCol > 0 ? availCol : sourceCol;

                var item = new AppItem
                {
                    Name = Slice(line, nameCol, idCol),
                    Id = Slice(line, idCol, versionCol),
                    Version = Slice(line, versionCol, versionEnd),
                    AvailableVersion = availCol > 0 ? Slice(line, availCol, sourceCol) : "",
                    Source = Slice(line, sourceCol, -1),
                };

                if (!string.IsNullOrWhiteSpace(item.Name))
                    items.Add(item);
            }

            return items;
        }
    }
}
