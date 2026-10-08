========================================================================
180Hz SETUP HUB - COMPLETE APPLICATION MATERIALS & PACKAGES
========================================================================
Project Root: C:\Users\PSYCHOPATH\Downloads\WinSetupHub
Installer Target: C:\Users\PSYCHOPATH\Downloads\WinSetupHub\artifacts\installer\180HzSetupHubSetup.exe
Date Generated: 2026-09-30

FOLDER STRUCTURE & CONTENTS:
------------------------------------------------------------------------
1. Root of 'new folder':
   - 180HzSetupHubSetup.exe (2.0 MB) : Primary standalone bootstrapper installer
   - 180HzSetupHubSetup.exe.config   : .NET configuration descriptor

2. Installer\
   - 180HzSetupHubSetup.exe          : Windows Fluent UI Bootstrapper Installer
   - 180HzSetupHubSetup.exe.config   : Configuration

3. StandaloneApp\
   - 180HzSetupHub.exe (81.74 MB)    : Standalone full Windows WPF Desktop app

4. Assets\
   - AppIcon.ico                     : High-resolution multi-size Windows icon
   - AppIcon-1024.png                : 1024x1024 ultra-HD PNG icon
   - BrandArtwork.png                : Official brand artwork for setup tiles
   - Lottie\                         : 9 LottieFiles vector animations:
       * download.json               : Active download animation
       * gears.json                  : Interlocking spinning gears
       * rocket.json                 : Rocket booster launch
       * check_pop.json              : Bouncy completion checkmark
       * loading_disc.json           : Circular neon spinner
       * scan.json                   : Radar storage scanner
       * wave.json                   : Dynamic telemetry pulse
       * fireworks.json              : Confetti celebration burst
       * toggle_switch.json          : Theme toggle switch

5. Configuration\
   - packages.default.json           : Winget catalog application definitions
   - appsettings.default.json        : Default user preferences & telemetry config

6. Scripts\
   - Build-Installer.ps1             : PowerShell automated installer compilation script

7. Source\WinSetupHub.Setup\
   - WinSetupHub.Setup.csproj        : .NET 4.8 / Windows SDK project file
   - InstallerWindow.xaml & .cs      : Fluent installation UI window
   - UninstallerWindow.xaml & .cs    : Clean uninstallation & repair window
   - InstallerService.cs             : Engine managing downloads, shortcut creation, registry
   - Program.cs                      : Elevation handler & single instance bootstrapper
   - app.manifest                    : RequestedExecutionLevel=requireAdministrator manifest
========================================================================
