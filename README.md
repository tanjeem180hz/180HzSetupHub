# 180Hz Setup Hub

180Hz Setup Hub is a native Windows setup assistant for installing essential apps, upgrading installed apps, checking Windows Update, and preparing a future self-update flow.

## What it does now

- Runs as a self-contained Windows executable; users do not need to install .NET, Python, Node.js, or build tools first.
- Loads an editable app catalog from the 180Hz Setup Hub data folder.
- Uses Windows Package Manager (`winget`) so app installs resolve to the latest available package manifests.
- Installs selected apps and only shows upgrade actions when `winget` reports a newer version.
- Lets users check one app, selected apps, or all apps manually instead of scanning everything on startup.
- Supports installing selected missing apps, updating selected apps, and updating all known available updates.
- Adds Pause, Resume, and Stop controls for long check/install/update queues.
- Includes setup profiles such as Most Useful, Developer, Browsers, Communication, Productivity, and Media & Utilities.
- Shows per-app icons, accent themes, elapsed time, network speed estimate, and progress details during install or upgrade.
- Runs `winget source update` and updates only the apps the user explicitly selects or has already checked as update-available.
- Opens Windows Update and triggers an interactive scan request.
- Stores runtime config, downloads, and logs under a writable 180Hz Setup Hub data folder.
- Includes a configurable GitHub Releases update checker for the app itself.
- Includes early security hardening for package ID validation and safe process execution.

## Run from source

```powershell
cd E:\WinSetupHub
dotnet run --project .\WinSetupHub.App\WinSetupHub.App.csproj
```

## Build the EXE

```powershell
cd E:\WinSetupHub
.\scripts\Publish-Windows.ps1
```

The published executable will be created under `E:\WinSetupHub\artifacts\publish\win-x64`.

The published app is self-contained. Copy `180HzSetupHub.exe` to a fresh Windows PC and run it directly. If the bundled `Configuration` folder is not beside the EXE, 180Hz Setup Hub can still seed its default settings from embedded resources.

Runtime data is chosen in this order:

1. `WINSETUPHUB_DATA_ROOT` environment variable, if set.
2. Existing legacy data folder at `E:\WinSetupHub\Data`.
3. `Data` beside the running EXE, when writable.
4. `%LOCALAPPDATA%\180HzSetupHub\Data`.

## Add more apps

Edit:

```text
<180Hz Setup Hub data folder>\config\packages.json
```

Add objects with this shape:

```json
{
  "id": "Vendor.PackageId",
  "name": "Package Name",
  "category": "Utilities",
  "description": "Short description.",
  "iconUrl": "https://www.google.com/s2/favicons?domain=example.com&sz=128",
  "accentColor": "#246BFE",
  "essential": false
}
```

Find package IDs with:

```powershell
winget search "app name"
```

## Configure app self-update checks

After creating a GitHub repo and releases, edit:

```text
<180Hz Setup Hub data folder>\config\appsettings.json
```

Set `releasesApiUrl` to:

```text
https://api.github.com/repos/tanjeem180hz/180HzSetupHub/releases/latest
```

The current version is stored in `currentVersion`.

## Roadmap

- Signed installer and automatic in-app download/install of new 180Hz Setup Hub releases.
- SHA-256 and signature verification before applying self-updates.
- Signed package catalog support.
- Package groups such as Developer, Gaming, Office, Creator, and Fresh Windows.
- Export/import setup profiles.
- Admin privilege detection before heavyweight installs.
- Better Windows Update status using Microsoft Update APIs.

## Security

See `SECURITY.md` for the threat model and hardening roadmap.

## Developer Notes

See `docs\ARCHITECTURE.md` for the code structure, runtime flow, and extension points.
