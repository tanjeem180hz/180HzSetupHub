# 180Hz Setup Hub Architecture

180Hz Setup Hub is a WPF desktop app that wraps Windows Package Manager (`winget`) with a safer, friendlier setup experience.

## Project Layout

```text
WinSetupHub.App/
  Configuration/        Default package and app settings JSON
  Models/               Plain data records and enums
  Security/             Validation and hardening helpers
  Services/             App, catalog, process, update, and winget services
    Progress/           Install progress parsing, formatting, and speed sampling
    Profiles/           Built-in setup profile rules
    Winget/             Winget output parsing helpers
  ViewModels/           UI-facing package state and computed display properties
  MainWindow.xaml       Main WPF layout
  MainWindow.xaml.cs    UI orchestration and event handling
Data/
  config/               User-editable runtime config
  logs/                 Local command/session logs
  downloads/            Reserved for future self-update downloads
```

## Runtime Flow

1. `MainWindow` creates the app services and points storage to a writable 180Hz Setup Hub data folder.
2. `PackageCatalog` loads `Data\config\packages.json`, enriches it from bundled defaults, and rejects unsafe package IDs.
3. `WingetService` checks installed status, latest versions, and available upgrades only when the user requests a single, selected, or all-app check.
4. `PackageViewModel` exposes clean UI state such as status badges, action labels, icon URLs, and version text.
5. Install and upgrade commands run through `ProcessRunner`, which uses `ProcessStartInfo.ArgumentList` instead of shell command strings.
6. `MainWindow` creates a cancellable operation token for user-started check/install/update queues; Stop cancels the active command and remaining queue, while Pause waits before starting the next app.
7. Progress text is built by `Services\Progress` helpers so UI code does not own network-speed or ETA formatting logic.
8. `SetupProfileCatalog` owns built-in app selection profiles, keeping profile rules out of WPF event handlers.

## Security Boundaries

- The app only runs fixed executables: `winget` and `UsoClient.exe`.
- Package IDs are validated by `PackageIdValidator` before any command runs.
- Winget commands use `--exact` and `--source winget`.
- User data stays under the selected local 180Hz Setup Hub data folder.
- The app does not capture the desktop or browser windows.

## Adding A New App

Edit `<180Hz Setup Hub data folder>\config\packages.json` and add:

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

Use `winget search "app name"` to find the exact package ID.

## Extension Points

- `PackageCatalog`: add profile support or signed catalog validation.
- `WingetService`: add richer package metadata and uninstall support.
- `AppUpdateService`: add signed self-update download and install.
- `Services\Progress`: improve ETA when winget exposes better progress events.
- `ViewModels\PackageViewModel`: adjust status wording and visual state without touching command execution.

## UX Rules

- App startup loads the catalog and preselects the most useful apps, but does not scan every app.
- Setup profiles can quickly select useful groups such as Developer, Browsers, and Media & Utilities.
- Per-app `Check` updates status for exactly that app.
- `Check Selected` and `Check All` are explicit user actions.
- `Pause` waits for the current winget command to finish, then holds the queue before the next app.
- `Stop` cancels the active winget command and marks the interrupted app as stopped so it can be checked again.
- `Install Selected` can install selected unchecked or missing apps, which supports fresh Windows setup.
- `Update Selected` only updates selected apps already known to have updates.
- `Update All Found` only updates apps currently marked `Update available`; it never starts an unrestricted whole-machine upgrade.
