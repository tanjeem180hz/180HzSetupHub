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
        private static readonly System.Threading.SemaphoreSlim _metadataSemaphore = new(2, 2);

        public async Task<AppMetadata?> GetMetadataAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (_metadataCache.TryGetValue(id, out var cached)) return cached;

            await _metadataSemaphore.WaitAsync();
            try
            {
                if (_metadataCache.TryGetValue(id, out cached)) return cached;

                var output = await RunWingetAsync($"show --id \"{id}\" --exact --accept-source-agreements");
                if (string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                string? homepage = null;
                string? publisherUrl = null;
                string? supportUrl = null;
                string? licenseUrl = null;
                string? installerUrl = null;

                using var reader = new System.IO.StringReader(output);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("Homepage:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Homepage:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) homepage = val;
                    }
                    else if (trimmed.StartsWith("Publisher Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) publisherUrl = val;
                    }
                    else if (trimmed.StartsWith("Publisher Support Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher Support Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) supportUrl = val;
                    }
                    else if (trimmed.StartsWith("License Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("License Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) licenseUrl = val;
                    }
                    else if (trimmed.StartsWith("Release Notes Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Release Notes Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val) && supportUrl == null) supportUrl = val;
                    }
                    else if (trimmed.StartsWith("Installer Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Installer Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) installerUrl = val;
                    }
                }

                // If no direct site found, extract root domain from Installer Url
                string? derivedInstallerDomain = null;
                if (homepage == null && publisherUrl == null && supportUrl == null && !string.IsNullOrWhiteSpace(installerUrl))
                {
                    try
                    {
                        var uri = new Uri(installerUrl);
                        if (uri.Host.Contains('.'))
                        {
                            derivedInstallerDomain = $"{uri.Scheme}://{uri.Host}/";
                        }
                    }
                    catch { }
                }

                var chosen = homepage ?? publisherUrl ?? supportUrl ?? licenseUrl ?? derivedInstallerDomain;
                var result = chosen != null
                    ? new AppMetadata(homepage ?? chosen, publisherUrl, supportUrl)
                    : null;

                if (result != null)
                {
                    _metadataCache[id] = result;
                }
                return result;
            }
            catch
            {
                return null;
            }
            finally
            {
                _metadataSemaphore.Release();
            }
        }

        public record WingetInstallerInfo(
            string? Url,
            string? Type,
            string? Sha256,
            string? Homepage = null,
            string? PublisherUrl = null,
            string? Publisher = null);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, WingetInstallerInfo?> _installerInfoCache =
            new(StringComparer.OrdinalIgnoreCase);

        public async Task<WingetInstallerInfo?> GetInstallerInfoAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (_installerInfoCache.TryGetValue(id, out var cached)) return cached;

            await _metadataSemaphore.WaitAsync();
            try
            {
                if (_installerInfoCache.TryGetValue(id, out cached)) return cached;

                var output = await RunWingetAsync($"show --id \"{id}\" --exact --accept-source-agreements");
                if (string.IsNullOrWhiteSpace(output))
                {
                    return null;
                }

                string? installerUrl = null;
                string? installerType = null;
                string? sha256 = null;
                string? homepage = null;
                string? publisherUrl = null;
                string? publisher = null;

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
                    else if (trimmed.StartsWith("Homepage:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Homepage:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) homepage = val;
                    }
                    else if (trimmed.StartsWith("Publisher Url:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher Url:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) publisherUrl = val;
                    }
                    else if (trimmed.StartsWith("Publisher:", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("Publisher:".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(val)) publisher = val;
                    }
                }

                var result = !string.IsNullOrWhiteSpace(installerUrl)
                    ? new WingetInstallerInfo(installerUrl, installerType, sha256, homepage, publisherUrl, publisher)
                    : null;

                if (result != null)
                {
                    _installerInfoCache[id] = result;
                }
                return result;
            }
            catch
            {
                return null;
            }
            finally
            {
                _metadataSemaphore.Release();
            }
        }

        public async Task<bool> InstallAsync(string id, string? source = null, Action<string>? onOutputLine = null, System.Threading.CancellationToken ct = default)
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

            var success = await RunActionAsync($"install --id \"{id}\" {srcArg}-e --silent --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
            if (!success && !ct.IsCancellationRequested)
            {
                success = await RunActionAsync($"install \"{id}\" {srcArg}--silent --accept-package-agreements --accept-source-agreements", onOutputLine, ct);
            }
            return success;
        }

        public Task<bool> UpgradeAsync(string id, Action<string>? onOutputLine = null) =>
            RunActionAsync($"upgrade --id \"{id}\" -e --silent --accept-package-agreements --accept-source-agreements", onOutputLine);

        public Task<bool> UpgradeAllAsync(Action<string>? onOutputLine = null) =>
            RunActionAsync("upgrade --all --silent --accept-package-agreements --accept-source-agreements", onOutputLine);

        public async Task<bool> UninstallAsync(string id, string? name = null, Action<string>? onOutputLine = null)
        {
            // 1. Try exact match by ID with silent mode and force
            var success = await RunActionAsync($"uninstall --id \"{id}\" -e --silent --force --accept-source-agreements", onOutputLine);
            if (!success)
            {
                // 2. Try partial/case-insensitive match by ID with silent mode and force
                success = await RunActionAsync($"uninstall --id \"{id}\" --silent --force --accept-source-agreements", onOutputLine);
            }
            if (!success && !string.IsNullOrWhiteSpace(name))
            {
                // 3. Try exact match by Name with silent mode and force
                success = await RunActionAsync($"uninstall --name \"{name}\" -e --silent --force --accept-source-agreements", onOutputLine);
                if (!success)
                {
                    // 4. Try partial match by Name
                    success = await RunActionAsync($"uninstall --name \"{name}\" --silent --force --accept-source-agreements", onOutputLine);
                }
            }
            if (!success)
            {
                // 5. Fallback: interactive mode with force (in case uninstaller displays confirmation)
                success = await RunActionAsync($"uninstall --id \"{id}\" --force --accept-source-agreements", onOutputLine);
            }
            return success;
        }

        // ---- internals ----

        private async Task<bool> RunActionAsync(string arguments, Action<string>? onOutputLine, System.Threading.CancellationToken ct = default)
        {
            var (exitCode, output) = await RunWingetWithCodeAsync(arguments, onOutputLine, timeoutMs: 240000, ct: ct);

            // 0 = Success, 3010 = Reboot required / Success, 0x8A15002B = Already installed / up to date
            if (exitCode == 0 || exitCode == 3010 || unchecked((uint)exitCode) == 0x8A15002B)
            {
                return true;
            }

            if (output.Contains("Successfully installed", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Successfully upgraded", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("Successfully uninstalled", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("No installed package found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("No package found matching", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("0x8a150014", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("0x8a150056", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("No applicable update found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("No available upgrade found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("already installed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private Task<string> RunWingetAsync(string arguments, Action<string>? onOutputLine = null)
        {
            return Task.Run(async () =>
            {
                var (_, output) = await RunWingetWithCodeAsync(arguments, onOutputLine, timeoutMs: 45000);
                return output;
            });
        }

        private Task<(int ExitCode, string Output)> RunWingetWithCodeAsync(string arguments, Action<string>? onOutputLine = null, int timeoutMs = 60000, System.Threading.CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (!arguments.Contains("--disable-interactivity", StringComparison.OrdinalIgnoreCase))
                {
                    arguments += " --disable-interactivity";
                }
                if (!arguments.Contains("--accept-source-agreements", StringComparison.OrdinalIgnoreCase))
                {
                    arguments += " --accept-source-agreements";
                }

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

                using var reg = ct.Register(() =>
                {
                    try { process.Kill(true); } catch { }
                });

                try
                {
                    process.Start();

                    var readOutputTask = Task.Run(async () =>
                    {
                        try
                        {
                            var reader = process.StandardOutput;
                            var lineBuf = new StringBuilder();
                            char[] buf = new char[512];
                            int read;
                            while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
                            {
                                for (int i = 0; i < read; i++)
                                {
                                    char c = buf[i];
                                    if (c == '\r' || c == '\n')
                                    {
                                        if (lineBuf.Length > 0)
                                        {
                                            string line = lineBuf.ToString().Trim();
                                            lineBuf.Clear();
                                            if (!string.IsNullOrWhiteSpace(line))
                                            {
                                                lock (sb) sb.AppendLine(line);
                                                onOutputLine?.Invoke(line);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lineBuf.Append(c);
                                    }
                                }
                            }
                            if (lineBuf.Length > 0)
                            {
                                string line = lineBuf.ToString().Trim();
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    lock (sb) sb.AppendLine(line);
                                    onOutputLine?.Invoke(line);
                                }
                            }
                        }
                        catch { }
                    });

                    var readErrorTask = Task.Run(async () =>
                    {
                        try
                        {
                            var reader = process.StandardError;
                            var lineBuf = new StringBuilder();
                            char[] buf = new char[512];
                            int read;
                            while ((read = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
                            {
                                for (int i = 0; i < read; i++)
                                {
                                    char c = buf[i];
                                    if (c == '\r' || c == '\n')
                                    {
                                        if (lineBuf.Length > 0)
                                        {
                                            string line = lineBuf.ToString().Trim();
                                            lineBuf.Clear();
                                            if (!string.IsNullOrWhiteSpace(line))
                                            {
                                                lock (sb) sb.AppendLine(line);
                                                onOutputLine?.Invoke(line);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        lineBuf.Append(c);
                                    }
                                }
                            }
                        }
                        catch { }
                    });

                    bool exited = process.WaitForExit(timeoutMs);
                    try { Task.WaitAll(new[] { readOutputTask, readErrorTask }, 1500); } catch { }

                    if (!exited || ct.IsCancellationRequested)
                    {
                        try { process.Kill(true); } catch { }
                        lock (sb) return (-1, sb.ToString());
                    }

                    lock (sb) return (process.ExitCode, sb.ToString());
                }
                catch (Exception ex)
                {
                    return (-1, $"Error running winget: {ex.Message}");
                }
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
            int matchCol = header.IndexOf("Match", StringComparison.Ordinal);
            int availCol = header.IndexOf("Available", StringComparison.Ordinal);
            int sourceCol = header.IndexOf("Source", StringComparison.Ordinal);

            string Slice(string line, int start, int end)
            {
                if (start < 0 || start >= line.Length) return "";
                int safeEnd = end < 0 ? line.Length : Math.Min(end, line.Length);
                return safeEnd <= start ? "" : line[start..safeEnd].Trim();
            }

            int versionEnd = sourceCol > 0 ? sourceCol : -1;
            if (availCol > 0 && (versionEnd <= 0 || availCol < versionEnd)) versionEnd = availCol;
            if (matchCol > 0 && (versionEnd <= 0 || matchCol < versionEnd)) versionEnd = matchCol;

            // headerIndex + 1 is the "----" separator line, so data starts at + 2.
            for (int i = headerIndex + 2; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("-")) continue;

                var rawVer = Slice(line, versionCol, versionEnd);
                if (rawVer.Contains("Tag:", StringComparison.OrdinalIgnoreCase))
                {
                    rawVer = rawVer.Split("Tag:")[0].Trim();
                }

                var item = new AppItem
                {
                    Name = Slice(line, nameCol, idCol),
                    Id = Slice(line, idCol, versionCol),
                    Version = rawVer,
                    AvailableVersion = availCol > 0 ? Slice(line, availCol, sourceCol) : "",
                    Source = sourceCol > 0 ? Slice(line, sourceCol, -1) : "winget",
                    Category = "Online"
                };

                if (string.IsNullOrWhiteSpace(item.Source)) item.Source = "winget";

                if (!string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.Id))
                    items.Add(item);
            }

            return items;
        }
    }
}
