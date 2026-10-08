# 180Hz Setup Hub — WPF (.NET 8)

Full working app matching your screenshot's layout (Dashboard, Setup Apps, Update Center,
Uninstaller, Cleanup, Activity, Storage), built on the "Slate Pro" theme: single accent
color, flat surfaces, 120–180ms GPU-cheap animations only (Opacity/RenderTransform).

## ⚡ 1-Click Installer Download

- **Direct In-Repo Installer:** [180HzSetupHubSetup.exe](https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe) (~3.6 MB)
- **PowerShell 1-Click Install:** `irm https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/scripts/install.ps1 | iex`
- **Official Repo Artifact:** [artifacts/installer/180HzSetupHubSetup.exe](https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe) (~3.6 MB)
- **Standalone App Binary (In-Repo):** [Download 180HzSetupHub.exe](https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe) (~81 MB)

## Requirements
- Windows 10/11
- .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
- `winget` (App Installer) available on PATH — comes preinstalled on most Windows 10/11 machines

## Run it
```
cd 180HzSetupHub
dotnet restore
dotnet run
```
The app requests admin rights on launch (needed for Windows\Temp / SoftwareDistribution
cleanup and some winget installs). If you don't need that, remove
`<ApplicationManifest>app.manifest</ApplicationManifest>` from the .csproj and delete
`requireAdministrator` from app.manifest (change it to `asInvoker`).

## What each page actually does
- **Dashboard** — checks `winget upgrade` in the background and shows the update count;
  the 01–06 tiles jump to the matching page.
- **Setup Apps** — `winget search "<query>"`, then per-row **Install** runs
  `winget install --id ... -e --silent --accept-package-agreements --accept-source-agreements`.
- **Update Center** — lists `winget upgrade` results; **Upgrade** per app or **Upgrade All**.
- **Uninstaller** — lists `winget list`, with a live name filter and a confirm dialog
  before `winget uninstall`.
- **Cleanup** — scans real sizes of User/Windows Temp, Windows Update cache, Prefetch,
  and Recent Items, lets you pick which to sweep, deletes files best-effort (skips
  anything locked instead of crashing the whole sweep).
- **Activity** — a persisted, live-updating log (`%LocalAppData%\180HzSetupHub\logs\activity.json`)
  that every other page writes to (install/upgrade/uninstall/cleanup results).
- **Storage** — shows and opens `%LocalAppData%\180HzSetupHub`, with a live folder-size readout.

## Structure
```
180HzSetupHub/
  App.xaml(.cs)            — app startup, global exception logging
  MainWindow.xaml(.cs)      — custom title bar (WindowChrome) + sidebar nav + page host/transition
  Theme/Theme.xaml          — all colors, styles, animations (edit this to re-theme the whole app)
  Models/                   — AppItem, ActivityLogEntry, CleanupTarget
  Services/
    WingetService.cs        — winget process wrapper + table parser (the core feature)
    CleanupService.cs       — temp/cache scan + delete
    StorageService.cs       — local data folder helpers
    ActivityLogger.cs       — app-wide observable + persisted log
  Converters/
    ActivityTypeToBrushConverter.cs
  Views/                    — one UserControl per sidebar page
```

## Notes / things you may want to adjust
- Sidebar icons are plain emoji (🏠 ⚙️ 🔄 …) for zero extra assets. Swap them for
  `Segoe MDL2 Assets` / `Segoe Fluent Icons` glyphs (like the caption buttons already use)
  if you want a more uniform icon set.
- `winget`'s table output is parsed by column position from the header row — this is
  winget's actual behavior today, but if a future winget version changes its console
  formatting, `WingetService.ParseTable` is the one place to adjust.
- No unit tests included given the scope — the natural next add would be a test project
  around `WingetService.ParseTable` (pure string-in, list-out, easy to test) and
  `CleanupService` (using a temp directory fixture).
