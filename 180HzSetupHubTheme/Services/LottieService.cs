using System;
using System.IO;
using System.Reflection;
using System.Windows;
using LottieSharp.WPF;

namespace SetupHub180Hz.Services
{
    /// <summary>
    /// Manages offline, self-contained extraction of vector Lottie animation JSON files
    /// to guarantee 100% reliable rendering without Skia managedStream or WPF pack URI failures.
    /// </summary>
    public static class LottieService
    {
        private static readonly string LottieFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "180HzSetupHub", "Assets", "Lottie");

        public static void Initialize()
        {
            try
            {
                Directory.CreateDirectory(LottieFolder);
                var asm = typeof(LottieService).Assembly;
                foreach (var r in asm.GetManifestResourceNames())
                {
                    if (r.Contains("Assets.Lottie.", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var fileName = r.Substring(r.IndexOf("Assets.Lottie.", StringComparison.OrdinalIgnoreCase) + "Assets.Lottie.".Length);
                        var target = Path.Combine(LottieFolder, fileName);
                        using var src = asm.GetManifestResourceStream(r);
                        if (src != null)
                        {
                            using var dst = File.Create(target);
                            src.CopyTo(dst);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lottie pre-extraction error: {ex.Message}");
            }
        }

        public static string? GetLottieFilePath(string assetName)
        {
            try
            {
                Directory.CreateDirectory(LottieFolder);
                var target = Path.Combine(LottieFolder, assetName);
                if (File.Exists(target) && new FileInfo(target).Length > 0)
                {
                    return target;
                }

                // Fallback: extract directly from embedded resource
                var asm = typeof(LottieService).Assembly;
                var resourceName = $"SetupHub180Hz.Assets.Lottie.{assetName}";
                using var src = asm.GetManifestResourceStream(resourceName);
                if (src != null)
                {
                    using var dst = File.Create(target);
                    src.CopyTo(dst);
                    return target;
                }

                // Fallback: check adjacent or relative development path
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = new[]
                {
                    Path.Combine(baseDir, "Assets", "Lottie", assetName),
                    Path.Combine(baseDir, "..", "..", "..", "Assets", "Lottie", assetName)
                };

                foreach (var c in candidates)
                {
                    if (File.Exists(c))
                    {
                        return c;
                    }
                }
            }
            catch
            {
                // Graceful fallback
            }

            return null;
        }
    }

    /// <summary>
    /// Attached property helper allowing clean, crash-proof binding of Lottie animations in XAML.
    /// Usage: services:LottieHelper.AssetName="wave.json"
    /// </summary>
    public static class LottieHelper
    {
        public static readonly DependencyProperty AssetNameProperty =
            DependencyProperty.RegisterAttached(
                "AssetName",
                typeof(string),
                typeof(LottieHelper),
                new PropertyMetadata(null, OnAssetNameChanged));

        public static string? GetAssetName(DependencyObject obj) => (string?)obj.GetValue(AssetNameProperty);
        public static void SetAssetName(DependencyObject obj, string? value) => obj.SetValue(AssetNameProperty, value);

        private static void OnAssetNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is LottieAnimationView view && e.NewValue is string assetName && !string.IsNullOrWhiteSpace(assetName))
            {
                try
                {
                    var path = LottieService.GetLottieFilePath(assetName);
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        view.FileName = path;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to bind Lottie '{assetName}': {ex.Message}");
                }
            }
        }
    }
}
