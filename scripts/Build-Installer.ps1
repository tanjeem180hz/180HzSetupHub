param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $root "WinSetupHub.App\WinSetupHub.App.csproj"
$setupProject = Join-Path $root "WinSetupHub.Setup\WinSetupHub.Setup.csproj"
$appPublishDir = Join-Path $root "artifacts\publish\$Runtime"
$installerPublishDir = Join-Path $root "artifacts\installer"
$payloadDir = Join-Path $root "WinSetupHub.Setup\Payload"
$payloadConfigDir = Join-Path $payloadDir "Configuration"

$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
$userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (Test-Path -LiteralPath $userDotnet) {
    $dotnet = $userDotnet
} elseif (-not $dotnet) {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
}

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET 8 SDK was not found. Please install the .NET SDK before running this script."
}

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " 1. Building 180Hz Setup Hub App Package " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

& $dotnet publish $appProject `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    --output $appPublishDir

$appExe = Join-Path $appPublishDir "180HzSetupHub.exe"
if (-not (Test-Path -LiteralPath $appExe)) {
    throw "Application binary was not generated: $appExe"
}

$appFileInfo = Get-Item -LiteralPath $appExe
$appSizeMb = [Math]::Round($appFileInfo.Length / 1MB, 2)
Write-Host "App package built: $appExe ($appSizeMb MB)" -ForegroundColor Green

Write-Host "`nStaging configurations for web bootstrapper..." -ForegroundColor Green
New-Item -ItemType Directory -Path $payloadConfigDir -Force | Out-Null

$catalogJson = Join-Path $root "WinSetupHub.App\Configuration\packages.default.json"
$appSettingsJson = Join-Path $root "WinSetupHub.App\Configuration\appsettings.default.json"
if (Test-Path -LiteralPath $catalogJson) {
    Copy-Item -Path $catalogJson -Destination (Join-Path $payloadConfigDir "packages.default.json") -Force
}
if (Test-Path -LiteralPath $appSettingsJson) {
    Copy-Item -Path $appSettingsJson -Destination (Join-Path $payloadConfigDir "appsettings.default.json") -Force
}
$tweaksJson = Join-Path $root "WinSetupHub.App\Configuration\tweaks.default.json"
if (Test-Path -LiteralPath $tweaksJson) {
    Copy-Item -Path $tweaksJson -Destination (Join-Path $payloadConfigDir "tweaks.default.json") -Force
}
$regTweaksJson = Join-Path $root "WinSetupHub.App\Configuration\registry_tweaks.default.json"
if (Test-Path -LiteralPath $regTweaksJson) {
    Copy-Item -Path $regTweaksJson -Destination (Join-Path $payloadConfigDir "registry_tweaks.default.json") -Force
}

# Ensure the 72 MB executable is not embedded so the installer stays ~2 MB
$payloadExe = Join-Path $payloadDir "180HzSetupHub.exe"
if (Test-Path -LiteralPath $payloadExe) {
    Remove-Item -LiteralPath $payloadExe -Force -ErrorAction SilentlyContinue
}

Write-Host "`n==========================================" -ForegroundColor Cyan
Write-Host " 2. Building 2-3 MB Web Bootstrapper...   " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

New-Item -ItemType Directory -Path $installerPublishDir -Force | Out-Null

& $dotnet build $setupProject `
    --configuration $Configuration `
    --output $installerPublishDir

$installerExe = Join-Path $installerPublishDir "180HzSetupHubSetup.exe"
if (-not (Test-Path -LiteralPath $installerExe)) {
    throw "Installer binary was not generated: $installerExe"
}

$fileInfo = Get-Item -LiteralPath $installerExe
$sizeMb = [Math]::Round($fileInfo.Length / 1MB, 2)

# Sync build outputs directly into "installer" and "new folder"
$installerDir = Join-Path $root "installer"
if (-not (Test-Path -LiteralPath $installerDir)) {
    New-Item -ItemType Directory -Path $installerDir -Force | Out-Null
}
Copy-Item -Path $installerExe -Destination (Join-Path $installerDir "180HzSetupHubSetup.exe") -Force
Write-Host "Updated installer in: $installerDir" -ForegroundColor Cyan

$newFolderDir = Join-Path $root "new folder"
if (Test-Path -LiteralPath $newFolderDir) {
    Copy-Item -Path $installerExe -Destination (Join-Path $newFolderDir "180HzSetupHubSetup.exe") -Force
    Copy-Item -Path $installerExe -Destination (Join-Path $newFolderDir "Installer\180HzSetupHubSetup.exe") -Force
    Copy-Item -Path $appExe -Destination (Join-Path $newFolderDir "StandaloneApp\180HzSetupHub.exe") -Force
    Write-Host "Updated artifacts in: $newFolderDir" -ForegroundColor Cyan
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host " BUILD SUCCESSFUL! " -ForegroundColor Green
Write-Host " Bootstrapper Installer : $installerExe ($sizeMb MB)" -ForegroundColor Green
Write-Host " Standalone App Package : $appExe ($appSizeMb MB)" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
