using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SetupHub180Hz.Services
{
    public static class IconCacheService
    {
        private static readonly ConcurrentDictionary<string, ImageSource> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "180HzSetupHub", "IconCache");

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        static IconCacheService()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            }
            catch { }

            try { Directory.CreateDirectory(CacheDir); } catch { }
        }

        public static string? DeriveFaviconUrl(string? webUrl)
        {
            if (string.IsNullOrWhiteSpace(webUrl)) return null;
            try
            {
                var url = webUrl.Trim();
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "https://" + url;
                }
                var uri = new Uri(url);
                var host = uri.Host;
                if (string.IsNullOrWhiteSpace(host) || host.Length < 3) return null;
                return $"https://www.google.com/s2/favicons?domain={host}&sz=128";
            }
            catch
            {
                return null;
            }
        }

        public static ImageSource? GetLocalFileIcon(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return null;

            var cacheKey = "local:" + filePath;
            if (_memoryCache.TryGetValue(cacheKey, out var cached)) return cached;

            try
            {
                var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
                if (ext is ".ico" or ".png" or ".jpg" or ".jpeg")
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                    bitmap.DecodePixelWidth = 64;
                    bitmap.DecodePixelHeight = 64;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    _memoryCache[cacheKey] = bitmap;
                    return bitmap;
                }
            }
            catch { }

            try
            {
                var shinfo = new SHFILEINFO();
                var res = SHGetFileInfo(filePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);
                if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        _memoryCache[cacheKey] = source;
                        return source;
                    }
                    finally
                    {
                        DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }

            return null;
        }

        public static async Task<ImageSource?> GetImageAsync(string? url, string? localFilePath = null)
        {
            // 1. If local file exists, extract genuine Windows icon
            if (!string.IsNullOrWhiteSpace(localFilePath) && File.Exists(localFilePath))
            {
                var localIcon = GetLocalFileIcon(localFilePath);
                if (localIcon != null) return localIcon;
            }

            if (string.IsNullOrWhiteSpace(url)) return null;

            // 2. Memory cache check
            if (_memoryCache.TryGetValue(url, out var memoryImg)) return memoryImg;

            // 3. Disk cache check
            var diskPath = GetDiskCachePath(url);
            if (File.Exists(diskPath))
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(diskPath);
                    var img = LoadFrozenBitmap(bytes);
                    if (img != null)
                    {
                        _memoryCache[url] = img;
                        return img;
                    }
                }
                catch { }
            }

            // 4. Download asynchronously in background
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                if (bytes.Length > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try { await File.WriteAllBytesAsync(diskPath, bytes); } catch { }
                    });

                    var img = LoadFrozenBitmap(bytes);
                    if (img != null)
                    {
                        _memoryCache[url] = img;
                        return img;
                    }
                }
            }
            catch { }

            return null;
        }

        private static ImageSource? LoadFrozenBitmap(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.DecodePixelWidth = 64;
                bitmap.DecodePixelHeight = 64;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDiskCachePath(string url)
        {
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(url)));
            return Path.Combine(CacheDir, hash + ".bin");
        }
    }
}
