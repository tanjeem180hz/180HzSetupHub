using System.Reflection;

namespace WinSetupHub.App.Services;

public sealed class StoragePaths
{
    private StoragePaths(string root)
    {
        Root = root;
        Config = Path.Combine(root, "config");
        Logs = Path.Combine(root, "logs");
        Downloads = Path.Combine(root, "downloads");
        PackagesConfigPath = Path.Combine(Config, "packages.json");
        AppSettingsPath = Path.Combine(Config, "appsettings.json");
    }

    public string Root { get; }
    public string Config { get; }
    public string Logs { get; }
    public string Downloads { get; }
    public string PackagesConfigPath { get; }
    public string AppSettingsPath { get; }

    public static StoragePaths CreateDefault()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("WINSETUPHUB_DATA_ROOT");
        var legacyRoot = @"E:\WinSetupHub\Data";
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new[]
        {
            configuredRoot,
            Directory.Exists(legacyRoot) ? legacyRoot : null,
            Path.Combine(AppContext.BaseDirectory, "Data"),
            string.IsNullOrWhiteSpace(localAppData)
                ? null
                : Path.Combine(localAppData, "180HzSetupHub", "Data")
        };

        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            try
            {
                var paths = new StoragePaths(candidate!);
                paths.EnsureDirectories();
                paths.EnsureWritable();
                return paths;
            }
            catch
            {
                // Try the next storage location.
            }
        }

        throw new InvalidOperationException("180Hz Setup Hub could not create a writable data folder.");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Downloads);
    }

    public void EnsureSeedFile(string seedFileName, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            return;
        }

        var seedPath = Path.Combine(AppContext.BaseDirectory, "Configuration", seedFileName);
        if (!File.Exists(seedPath))
        {
            CopyEmbeddedSeedFile(seedFileName, destinationPath);
            return;
        }

        File.Copy(seedPath, destinationPath, overwrite: false);
    }

    private void EnsureWritable()
    {
        var testPath = Path.Combine(Root, $".write-test-{Guid.NewGuid():N}");
        File.WriteAllText(testPath, "ok");
        File.Delete(testPath);
    }

    private static void CopyEmbeddedSeedFile(string seedFileName, string destinationPath)
    {
        var resourceName = $"WinSetupHub.App.Configuration.{seedFileName}";
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var output = File.Create(destinationPath);
        stream.CopyTo(output);
    }
}
