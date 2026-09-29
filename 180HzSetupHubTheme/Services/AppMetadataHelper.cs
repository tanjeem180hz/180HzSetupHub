using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SetupHub180Hz.Models;

namespace SetupHub180Hz.Services
{
    public static class AppMetadataHelper
    {
        public class RegistryAppInfo
        {
            public string DisplayName { get; set; } = "";
            public string DisplayVersion { get; set; } = "";
            public string Size { get; set; } = "";
            public string? InstallLocation { get; set; }
            public string? DisplayIcon { get; set; }
            public string? UninstallString { get; set; }
            public string? WebUrl { get; set; }
        }

        private static Dictionary<string, RegistryAppInfo>? _registryCache;

        public static void EnrichAppItem(AppItem app, IEnumerable<AppItem>? catalog = null)
        {
            // 1. Smart match against catalog
            if (catalog != null)
            {
                var match = FindCatalogMatch(app, catalog);
                if (match != null)
                {
                    if (string.IsNullOrWhiteSpace(app.IconUrl)) app.IconUrl = match.IconUrl;
                    if (string.IsNullOrWhiteSpace(app.Category) || app.Category == "General") app.Category = match.Category;
                    if (string.IsNullOrWhiteSpace(app.WebUrl)) app.WebUrl = match.WebUrl;
                    if (string.IsNullOrWhiteSpace(app.Size)) app.Size = match.Size;
                    if (string.IsNullOrWhiteSpace(app.Description)) app.Description = match.Description;
                    app.AccentColor = match.AccentColor;
                }
            }

            // 2. Query Windows Registry for exact real installed version, size, and real icon path
            var regInfo = GetRegistryInfo(app.Name, app.Id);
            if (regInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(regInfo.DisplayVersion) && (string.IsNullOrWhiteSpace(app.Version) || app.Version == "Latest"))
                {
                    app.Version = regInfo.DisplayVersion;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation))
                {
                    app.InstallLocation = regInfo.InstallLocation;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.Size))
                {
                    app.Size = regInfo.Size;
                }
                else if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    var dirSize = CalculateDirectorySize(regInfo.InstallLocation);
                    if (!string.IsNullOrWhiteSpace(dirSize))
                    {
                        app.Size = dirSize;
                    }
                }

                if (!string.IsNullOrWhiteSpace(regInfo.DisplayIcon))
                {
                    var cleanIcon = CleanIconPath(regInfo.DisplayIcon);
                    if (File.Exists(cleanIcon))
                    {
                        app.LocalIconPath = cleanIcon;
                    }
                }

                // If no DisplayIcon found, search InstallLocation for primary executable
                if (string.IsNullOrWhiteSpace(app.LocalIconPath) && !string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    try
                    {
                        var exes = Directory.GetFiles(regInfo.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                        var matchExe = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(app.Name, StringComparison.OrdinalIgnoreCase))
                                       ?? exes.FirstOrDefault(e => !Path.GetFileName(e).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
                        if (matchExe != null && File.Exists(matchExe))
                        {
                            app.LocalIconPath = matchExe;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrWhiteSpace(regInfo.UninstallString))
                {
                    app.UninstallString = regInfo.UninstallString;
                }

                if (string.IsNullOrWhiteSpace(app.WebUrl) && !string.IsNullOrWhiteSpace(regInfo.WebUrl))
                {
                    app.WebUrl = regInfo.WebUrl;
                }
            }

            // 3. If local icon still missing, try Windows App Paths registry
            if (string.IsNullOrWhiteSpace(app.LocalIconPath))
            {
                var appPathExe = ResolveFromAppPaths(app.Name) ?? ResolveFromAppPaths(CleanPackageId(app.Id));
                if (!string.IsNullOrWhiteSpace(appPathExe) && File.Exists(appPathExe))
                {
                    app.LocalIconPath = appPathExe;
                }
            }

            // 4. Resolve authentic official website via catalog, registry, or comprehensive domain mapping
            if (string.IsNullOrWhiteSpace(app.WebUrl))
            {
                var resolved = ResolveOfficialUrl(app, catalog);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    app.WebUrl = resolved;
                }
                else if (app.Name.StartsWith("Windows ", StringComparison.OrdinalIgnoreCase) ||
                         app.Name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
                {
                    app.WebUrl = "https://www.microsoft.com";
                }
            }

            // 5. Fallback size if truly unmeasured: "-"
            if (string.IsNullOrWhiteSpace(app.Size))
            {
                app.Size = "-";
            }

            // 6. Derive authentic IconUrl from WebUrl if IconUrl is missing
            if (string.IsNullOrWhiteSpace(app.IconUrl) && !string.IsNullOrWhiteSpace(app.WebUrl))
            {
                var favicon = IconCacheService.DeriveFaviconUrl(app.WebUrl);
                if (!string.IsNullOrWhiteSpace(favicon))
                {
                    app.IconUrl = favicon;
                }
            }

            // 7. Try load local icon immediately if available
            if (!string.IsNullOrWhiteSpace(app.LocalIconPath) && File.Exists(app.LocalIconPath))
            {
                var localImg = IconCacheService.GetLocalFileIcon(app.LocalIconPath);
                if (localImg != null)
                {
                    app.IconImageSource = localImg;
                }
            }
        }

        public static AppItem? FindCatalogMatch(AppItem app, IEnumerable<AppItem> catalog)
        {
            if (!string.IsNullOrWhiteSpace(app.Id))
            {
                var match = catalog.FirstOrDefault(c => string.Equals(c.Id, app.Id, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;

                var cleanId = CleanPackageId(app.Id);
                if (!string.IsNullOrWhiteSpace(cleanId))
                {
                    match = catalog.FirstOrDefault(c =>
                        string.Equals(c.Id, cleanId, StringComparison.OrdinalIgnoreCase) ||
                        cleanId.StartsWith(c.Id, StringComparison.OrdinalIgnoreCase) ||
                        c.Id.EndsWith(cleanId, StringComparison.OrdinalIgnoreCase));
                    if (match != null) return match;
                }
            }

            return FindCatalogMatchForName(app.Name, catalog);
        }

        public static AppItem? FindCatalogMatchForName(string name, IEnumerable<AppItem> catalog)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            // 1. Exact Name match
            var match = catalog.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;

            // 2. DetectionNames match
            match = catalog.FirstOrDefault(c => c.DetectionNames != null && c.DetectionNames.Any(d =>
                string.Equals(d, name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(d, StringComparison.OrdinalIgnoreCase)));
            if (match != null) return match;

            // 3. Normalized Name match (strips (x64), versions, bitness, etc.)
            var normApp = NormalizeAppName(name);
            if (!string.IsNullOrWhiteSpace(normApp) && normApp.Length >= 3)
            {
                match = catalog.FirstOrDefault(c =>
                {
                    var normCat = NormalizeAppName(c.Name);
                    return string.Equals(normCat, normApp, StringComparison.OrdinalIgnoreCase) ||
                           normApp.StartsWith(normCat, StringComparison.OrdinalIgnoreCase) ||
                           normCat.StartsWith(normApp, StringComparison.OrdinalIgnoreCase) ||
                           normApp.Contains(normCat, StringComparison.OrdinalIgnoreCase);
                });
                if (match != null) return match;
            }

            return null;
        }

        private static readonly Dictionary<string, string> KnownDomainMap = new(StringComparer.OrdinalIgnoreCase)
        {
            // Browsers & Web
            ["Google.Chrome"] = "https://www.google.com/chrome/",
            ["Mozilla.Firefox"] = "https://www.mozilla.org/firefox/",
            ["Brave.Brave"] = "https://brave.com/",
            ["Microsoft.Edge"] = "https://www.microsoft.com/edge/",
            ["Opera.Opera"] = "https://www.opera.com/",
            ["Opera.OperaGX"] = "https://www.opera.com/gx",
            ["Vivaldi.Vivaldi"] = "https://vivaldi.com/",
            ["TorProject.TorBrowser"] = "https://www.torproject.org/",
            ["Floorp.Floorp"] = "https://floorp.app/",
            ["Waterfox.Waterfox"] = "https://www.waterfox.net/",
            ["Arc.Arc"] = "https://arc.net/",

            // Communication & Chat
            ["Discord.Discord"] = "https://discord.com/",
            ["Telegram.TelegramDesktop"] = "https://desktop.telegram.org/",
            ["WhatsApp.WhatsApp"] = "https://www.whatsapp.com/",
            ["Signal.Signal"] = "https://signal.org/",
            ["SlackTechnologies.Slack"] = "https://slack.com/",
            ["Microsoft.Teams"] = "https://www.microsoft.com/microsoft-teams/",
            ["Zoom.Zoom"] = "https://zoom.us/",
            ["Skype.Skype"] = "https://www.skype.com/",
            ["Element.Element"] = "https://element.io/",
            ["Viber.Viber"] = "https://www.viber.com/",

            // Media & Audio / Video
            ["VideoLAN.VLC"] = "https://www.videolan.org/vlc/",
            ["Spotify.Spotify"] = "https://www.spotify.com/",
            ["OBSProject.OBSStudio"] = "https://obsproject.com/",
            ["Audacity.Audacity"] = "https://www.audacityteam.org/",
            ["HandBrake.HandBrake"] = "https://handbrake.fr/",
            ["mpv.mpv"] = "https://mpv.io/",
            ["K-Lite.CodecPack"] = "https://codecguide.com/",
            ["Plex.Plex"] = "https://www.plex.tv/",
            ["Kodi.Kodi"] = "https://kodi.tv/",
            ["Tidal.Tidal"] = "https://tidal.com/",
            ["Deezer.Deezer"] = "https://www.deezer.com/",
            ["Foobar2000.Foobar2000"] = "https://www.foobar2000.org/",
            ["AIMP.AIMP"] = "https://www.aimp.ru/",
            ["MusicBee.MusicBee"] = "https://getmusicbee.com/",
            ["DaVinciResolve.DaVinciResolve"] = "https://www.blackmagicdesign.com/products/davinciresolve",

            // Gaming & Launchers
            ["Valve.Steam"] = "https://store.steampowered.com/",
            ["EpicGames.EpicGamesLauncher"] = "https://store.epicgames.com/",
            ["ElectronicArts.EADesktop"] = "https://www.ea.com/ea-app",
            ["Ubisoft.Connect"] = "https://ubisoftconnect.com/",
            ["GOG.Galaxy"] = "https://www.gog.com/galaxy",
            ["Battle.net"] = "https://battle.net/",
            ["PrismLauncher.PrismLauncher"] = "https://prismlauncher.org/",
            ["MoonlightGameStreamingProject.Moonlight"] = "https://moonlight-stream.org/",
            ["Parsec.Parsec"] = "https://parsec.app/",
            ["Playnite.Playnite"] = "https://playnite.link/",
            ["Razer.Synapse"] = "https://www.razer.com/synapse-3",
            ["Logitech.GHUB"] = "https://www.logitechg.com/innovation/g-hub.html",
            ["Corsair.iCUE"] = "https://www.corsair.com/icue",

            // Utilities & Tools
            ["7zip.7zip"] = "https://www.7-zip.org/",
            ["RARLab.WinRAR"] = "https://www.rarlab.com/",
            ["voidtools.Everything"] = "https://www.voidtools.com/",
            ["Notepad++.Notepad++"] = "https://notepad-plus-plus.org/",
            ["Microsoft.PowerToys"] = "https://github.com/microsoft/PowerToys",
            ["ShareX.ShareX"] = "https://getsharex.com/",
            ["Greenshot.Greenshot"] = "https://getgreenshot.org/",
            ["Lightshot.Lightshot"] = "https://app.prntscr.com/",
            ["Rufus.Rufus"] = "https://rufus.ie/",
            ["Balena.Etcher"] = "https://etcher.balena.io/",
            ["BleachBit.BleachBit"] = "https://www.bleachbit.org/",
            ["Piriform.CCleaner"] = "https://www.ccleaner.com/",
            ["RevoUninstaller.RevoUninstaller"] = "https://www.revouninstaller.com/",
            ["IObit.Uninstaller"] = "https://www.iobit.com/advanceduninstaller.php",
            ["CrystalDewWorld.CrystalDiskInfo"] = "https://crystalmark.info/",
            ["CrystalDewWorld.CrystalDiskMark"] = "https://crystalmark.info/",
            ["CPUID.CPU-Z"] = "https://www.cpuid.com/softwares/cpu-z.html",
            ["TechPowerUp.GPU-Z"] = "https://www.techpowerup.com/gpuz/",
            ["REALiX.HWiNFO"] = "https://www.hwinfo.com/",
            ["Guru3D.RTSS"] = "https://www.guru3d.com/",
            ["MSI.Afterburner"] = "https://www.msi.com/Landing/afterburner/graphics-cards",
            ["AutoHotkey.AutoHotkey"] = "https://www.autohotkey.com/",
            ["JAMSoftware.TreeSize.Free"] = "https://www.jam-software.com/treesize_free",
            ["qBittorrent.qBittorrent"] = "https://www.qbittorrent.org/",
            ["Transmission.Transmission"] = "https://transmissionbt.com/",
            ["BitTorrent.uTorrent"] = "https://www.utorrent.com/",
            ["FileZilla.FileZilla"] = "https://filezilla-project.org/",
            ["WinSCP.WinSCP"] = "https://winscp.net/",
            ["PuTTY.PuTTY"] = "https://www.putty.org/",
            ["AnyDeskSoftwareGmbH.AnyDesk"] = "https://anydesk.com/",
            ["TeamViewer.TeamViewer"] = "https://www.teamviewer.com/",
            ["RustDesk.RustDesk"] = "https://rustdesk.com/",

            // Security & Privacy
            ["Bitwarden.Bitwarden"] = "https://bitwarden.com/",
            ["1Password.1Password"] = "https://1password.com/",
            ["KeePassXCTeam.KeePassXC"] = "https://keepassxc.org/",
            ["ProtonTechnologies.ProtonVPN"] = "https://protonvpn.com/",
            ["NordVPN.NordVPN"] = "https://nordvpn.com/",
            ["Surfshark.Surfshark"] = "https://surfshark.com/",
            ["Tailscale.Tailscale"] = "https://tailscale.com/",
            ["WireGuard.WireGuard"] = "https://www.wireguard.com/",
            ["Malwarebytes.Malwarebytes"] = "https://www.malwarebytes.com/",
            ["Kaspersky.Kaspersky"] = "https://www.kaspersky.com/",
            ["ESET.NOD32"] = "https://www.eset.com/",

            // Developer & Design
            ["Microsoft.VisualStudioCode"] = "https://code.visualstudio.com/",
            ["Microsoft.VisualStudio.2022.Community"] = "https://visualstudio.microsoft.com/",
            ["Git.Git"] = "https://git-scm.com/",
            ["GitHub.GitHubDesktop"] = "https://desktop.github.com/",
            ["GitHub.cli"] = "https://cli.github.com/",
            ["Docker.DockerDesktop"] = "https://www.docker.com/products/docker-desktop/",
            ["Postman.Postman"] = "https://www.postman.com/",
            ["Insomnia.Insomnia"] = "https://insomnia.rest/",
            ["DBeaver.DBeaver.Community"] = "https://dbeaver.io/",
            ["DBeaver.DBeaver.Enterprise"] = "https://dbeaver.com/",
            ["Alacritty.Alacritty"] = "https://alacritty.org/",
            ["wez.wezterm"] = "https://wezfurlong.org/wezterm/",
            ["Neovim.Neovim"] = "https://neovim.io/",
            ["GodotEngine.GodotEngine"] = "https://godotengine.org/",
            ["Unity.UnityHub"] = "https://unity.com/",
            ["Google.AndroidStudio"] = "https://developer.android.com/studio",
            ["Figma.Figma"] = "https://www.figma.com/",
            ["BlenderFoundation.Blender"] = "https://www.blender.org/",
            ["GIMP.GIMP"] = "https://www.gimp.org/",
            ["Inkscape.Inkscape"] = "https://inkscape.org/",
            ["Krita.Krita"] = "https://krita.org/",
            ["WiresharkFoundation.Wireshark"] = "https://www.wireshark.org/",
            ["Python.Python.3.12"] = "https://www.python.org/",
            ["OpenJS.NodeJS"] = "https://nodejs.org/",
            ["Rustlang.Rustup"] = "https://www.rust-lang.org/",
            ["Golang.Go"] = "https://go.dev/",
            ["Oracle.JDK.21"] = "https://www.oracle.com/java/",
            ["JetBrains.IntelliJIDEA.Community"] = "https://www.jetbrains.com/idea/",
            ["JetBrains.PyCharm.Community"] = "https://www.jetbrains.com/pycharm/",
            ["SublimeHQ.SublimeText.4"] = "https://www.sublimetext.com/",
            ["Termius.Termius"] = "https://termius.com/",
            ["Oracle.VirtualBox"] = "https://www.virtualbox.org/",
            ["JanDeDobbeleer.OhMyPosh"] = "https://ohmyposh.dev/",
            ["Starship.Starship"] = "https://starship.rs/",
            ["Eugeny.Tabby"] = "https://tabby.sh/",
            ["Anysphere.Cursor"] = "https://www.cursor.com/",
            ["DenoLand.Deno"] = "https://deno.com/",
            ["BurntSushi.ripgrep.MSVC"] = "https://github.com/BurntSushi/ripgrep",
            ["NickeManarin.ScreenToGif"] = "https://www.screentogif.com/",
            ["Upscayl.Upscayl"] = "https://upscayl.org/",
            ["Toinane.Colorpicker"] = "https://github.com/toinane/colorpicker",
            ["HeroicGamesLauncher.HeroicGamesLauncher"] = "https://heroicgameslauncher.com/",
            ["shinchiro.mpv"] = "https://mpv.io/",
            ["Cockos.REAPER"] = "https://www.reaper.fm/",
            ["Safing.Portmaster"] = "https://safing.io/",
            ["OO-Software.ShutUp10"] = "https://www.oo-software.com/en/shutup10",
            ["zhongyang219.TrafficMonitor.Full"] = "https://github.com/zhongyang219/TrafficMonitor",
            ["WinsiderSS.SystemInformer"] = "https://systeminformer.sourceforge.io/",
            ["Open-Shell.Open-Shell-Menu"] = "https://open-shell.github.io/Open-Shell-Menu/",
            ["GNU.Octave"] = "https://octave.org/",

            // Documents & Office
            ["TheDocumentFoundation.LibreOffice"] = "https://www.libreoffice.org/",
            ["Adobe.Acrobat.Reader.64-bit"] = "https://get.adobe.com/reader/",
            ["Foxit.FoxitReader"] = "https://www.foxit.com/pdf-reader/",
            ["Calibre.Calibre"] = "https://calibre-ebook.com/",
            ["Obsidian.Obsidian"] = "https://obsidian.md/",
            ["Notion.Notion"] = "https://www.notion.so/",
            ["SumatraPDF.SumatraPDF"] = "https://www.sumatrapdfreader.org/",
            ["Anki.Anki"] = "https://apps.ankiweb.net/",
            ["Zotero.Zotero"] = "https://www.zotero.org/",
            ["DigitalScholar.Zotero"] = "https://www.zotero.org/",
            ["Miro.Miro"] = "https://miro.com/",
            ["Logseq.Logseq"] = "https://logseq.com/",
            ["Freeplane.Freeplane"] = "https://www.freeplane.org/",
            ["geeksoftwareGmbH.PDF24Creator"] = "https://tools.pdf24.org/"
        };

        public static string? ResolveOfficialUrl(AppItem app, IEnumerable<AppItem>? catalog = null)
        {
            if (!string.IsNullOrWhiteSpace(app.WebUrl) && app.WebUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return app.WebUrl;
            }

            // 1. Direct ID match in KnownDomainMap
            if (!string.IsNullOrWhiteSpace(app.Id) && KnownDomainMap.TryGetValue(app.Id, out var directUrl))
            {
                return directUrl;
            }

            // 2. Prefix / Contains match in KnownDomainMap
            if (!string.IsNullOrWhiteSpace(app.Id))
            {
                var clean = CleanPackageId(app.Id);
                foreach (var (key, val) in KnownDomainMap)
                {
                    if (key.Equals(clean, StringComparison.OrdinalIgnoreCase) ||
                        clean.StartsWith(key, StringComparison.OrdinalIgnoreCase) ||
                        key.StartsWith(clean, StringComparison.OrdinalIgnoreCase))
                    {
                        return val;
                    }
                }
            }

            // 3. By App Name in KnownDomainMap
            if (!string.IsNullOrWhiteSpace(app.Name))
            {
                var norm = NormalizeAppName(app.Name);
                foreach (var (key, val) in KnownDomainMap)
                {
                    var lastPart = key.Contains('.') ? key.Split('.').Last() : key;
                    if (norm.Contains(lastPart, StringComparison.OrdinalIgnoreCase) ||
                        lastPart.Contains(norm, StringComparison.OrdinalIgnoreCase))
                    {
                        return val;
                    }
                }
            }

            // 4. Catalog match
            if (catalog != null)
            {
                var match = FindCatalogMatch(app, catalog);
                if (!string.IsNullOrWhiteSpace(match?.WebUrl))
                {
                    return match.WebUrl;
                }
            }

            // 5. Registry URLInfoAbout
            var reg = GetRegistryInfo(app.Name, app.Id);
            if (!string.IsNullOrWhiteSpace(reg?.WebUrl) && reg.WebUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return reg.WebUrl;
            }

            // 6. Derive smart domain from Id or Name
            return DeriveDomainFromId(app.Id, app.Name);
        }

        public static string? DeriveDomainFromId(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name)) return null;

            var cleanId = CleanPackageId(id);
            var parts = cleanId.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                var pub = parts[0].ToLowerInvariant();
                var prod = parts[1].ToLowerInvariant();

                // Specific publisher shortcuts
                if (pub.Contains("github")) return "https://github.com";
                if (pub.Contains("microsoft")) return "https://www.microsoft.com";
                if (pub.Contains("google")) return "https://www.google.com";

                // Heuristic domain
                if (prod.Length > 2 && !prod.Contains("installer") && !prod.Contains("portable"))
                {
                    return $"https://{prod}.org";
                }

                if (pub.Length > 2)
                {
                    return $"https://{pub}.com";
                }
            }

            return null;
        }

        public static string NormalizeAppName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim();
            s = Regex.Replace(s, @"\s*\([^)]*\)", "");
            s = Regex.Replace(s, @"\s+(v|version\s+)?\d+(\.\d+)*.*$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*-\s*[a-z]{2}-[a-z]{2}$", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        public static string CleanPackageId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "";
            var s = id.Trim();
            if (s.StartsWith("MSIX\\", StringComparison.OrdinalIgnoreCase))
            {
                s = s.Substring(5);
                var underscore = s.IndexOf('_');
                if (underscore > 0) s = s.Substring(0, underscore);
            }
            else if (s.StartsWith("ARP\\", StringComparison.OrdinalIgnoreCase))
            {
                var lastSlash = s.LastIndexOf('\\');
                if (lastSlash >= 0 && lastSlash + 1 < s.Length)
                {
                    s = s.Substring(lastSlash + 1);
                }
            }
            return s.Trim();
        }

        public static string? FindLocalIconPath(string appName)
        {
            var regInfo = GetRegistryInfo(appName, "");
            if (regInfo != null)
            {
                if (!string.IsNullOrWhiteSpace(regInfo.DisplayIcon))
                {
                    var cleaned = CleanIconPath(regInfo.DisplayIcon);
                    if (File.Exists(cleaned)) return cleaned;
                }

                if (!string.IsNullOrWhiteSpace(regInfo.InstallLocation) && Directory.Exists(regInfo.InstallLocation))
                {
                    try
                    {
                        var exes = Directory.GetFiles(regInfo.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
                        var matchExe = exes.FirstOrDefault(e => Path.GetFileNameWithoutExtension(e).Equals(appName, StringComparison.OrdinalIgnoreCase))
                                       ?? exes.FirstOrDefault(e => !Path.GetFileName(e).StartsWith("unins", StringComparison.OrdinalIgnoreCase));
                        if (matchExe != null && File.Exists(matchExe)) return matchExe;
                    }
                    catch { }
                }
            }

            var appPathExe = ResolveFromAppPaths(appName);
            if (!string.IsNullOrWhiteSpace(appPathExe) && File.Exists(appPathExe))
            {
                return appPathExe;
            }

            return null;
        }

        public static string? ResolveFromAppPaths(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            var clean = query.Trim().Trim('\"', ' ');
            var candidates = new List<string>();

            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(clean + ".exe");
            }
            candidates.Add(clean);

            var firstWord = clean.Split(' ', '-', '_').FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(firstWord))
            {
                if (!firstWord.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(firstWord + ".exe");
                }
                candidates.Add(firstWord);
            }

            string[] appPathRoots = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"
            };

            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (var basePath in appPathRoots)
                {
                    foreach (var cand in candidates)
                    {
                        try
                        {
                            using var key = root.OpenSubKey($@"{basePath}\{cand}");
                            if (key != null)
                            {
                                var path = (key.GetValue("") as string) ?? (key.GetValue("Path") as string);
                                if (!string.IsNullOrWhiteSpace(path))
                                {
                                    var cleaned = CleanIconPath(path);
                                    if (File.Exists(cleaned)) return cleaned;
                                }
                            }
                        }
                        catch { }
                    }
                }
            }

            return null;
        }

        public static string CleanIconPath(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var clean = raw.Trim();
            var commaIdx = clean.IndexOf(',');
            if (commaIdx > 0)
            {
                clean = clean.Substring(0, commaIdx).Trim();
            }
            return clean.Trim('\"', ' ');
        }

        private static string CalculateDirectorySize(string folderPath)
        {
            try
            {
                var di = new DirectoryInfo(folderPath);
                long totalBytes = 0;
                int fileCount = 0;
                foreach (var fi in di.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    totalBytes += fi.Length;
                    if (++fileCount > 1500) break; // Speed-capped at 1500 files
                }
                if (totalBytes > 0)
                {
                    double mb = totalBytes / (1024.0 * 1024.0);
                    return mb >= 1024 ? $"{mb / 1024.0:0.0} GB" : $"{mb:0.0} MB";
                }
            }
            catch { }
            return "";
        }

        public static RegistryAppInfo? GetRegistryInfo(string name, string id = "")
        {
            EnsureRegistryCache();
            if (_registryCache == null) return null;

            if (!string.IsNullOrWhiteSpace(id) && _registryCache.TryGetValue(id.ToLowerInvariant(), out var info))
                return info;

            var cleanId = CleanPackageId(id).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(cleanId) && _registryCache.TryGetValue(cleanId, out var infoCleanId))
                return infoCleanId;

            if (!string.IsNullOrWhiteSpace(name) && _registryCache.TryGetValue(name.ToLowerInvariant(), out var infoName))
                return infoName;

            var normName = NormalizeAppName(name).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normName) && _registryCache.TryGetValue(normName, out var infoNorm))
                return infoNorm;

            foreach (var kvp in _registryCache)
            {
                var k = kvp.Key;
                if (k.Length < 3) continue;

                if ((!string.IsNullOrWhiteSpace(name) && k.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(normName) && k.Equals(normName, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(cleanId) && k.Equals(cleanId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(normName) && (k.StartsWith(normName) || normName.StartsWith(k))))
                {
                    return kvp.Value;
                }
            }

            return null;
        }

        private static void EnsureRegistryCache()
        {
            if (_registryCache != null) return;
            _registryCache = new Dictionary<string, RegistryAppInfo>(StringComparer.OrdinalIgnoreCase);

            string[] subKeys = {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                foreach (var path in subKeys)
                {
                    try
                    {
                        using var key = root.OpenSubKey(path);
                        if (key == null) continue;

                        foreach (var subName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = key.OpenSubKey(subName);
                                if (appKey == null) continue;

                                var dispName = appKey.GetValue("DisplayName") as string;
                                var dispVer = appKey.GetValue("DisplayVersion") as string;
                                var sizeObj = appKey.GetValue("EstimatedSize");
                                var installLoc = appKey.GetValue("InstallLocation") as string;
                                var dispIcon = appKey.GetValue("DisplayIcon") as string;
                                var uninstStr = appKey.GetValue("UninstallString") as string;
                                var webUrl = (appKey.GetValue("URLInfoAbout") as string)
                                             ?? (appKey.GetValue("HelpLink") as string)
                                             ?? (appKey.GetValue("URLUpdateInfo") as string);

                                string sizeStr = "";
                                if (sizeObj is int sizeKb && sizeKb > 0)
                                {
                                    double mb = sizeKb / 1024.0;
                                    sizeStr = mb >= 1024 ? $"{mb / 1024:0.0} GB" : $"{mb:0.0} MB";
                                }

                                var entry = new RegistryAppInfo
                                {
                                    DisplayName = dispName ?? subName,
                                    DisplayVersion = dispVer ?? "",
                                    Size = sizeStr,
                                    InstallLocation = installLoc,
                                    DisplayIcon = dispIcon,
                                    UninstallString = uninstStr,
                                    WebUrl = webUrl
                                };

                                if (!string.IsNullOrWhiteSpace(dispName))
                                {
                                    _registryCache[dispName.Trim().ToLowerInvariant()] = entry;
                                    var norm = NormalizeAppName(dispName).ToLowerInvariant();
                                    if (!string.IsNullOrWhiteSpace(norm))
                                    {
                                        _registryCache[norm] = entry;
                                    }
                                }
                                _registryCache[subName.Trim().ToLowerInvariant()] = entry;
                                var cleanSub = CleanPackageId(subName).ToLowerInvariant();
                                if (!string.IsNullOrWhiteSpace(cleanSub))
                                {
                                    _registryCache[cleanSub] = entry;
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }
    }
}
