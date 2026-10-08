# ==============================================================================
#  180Hz Setup Hub - Official 1-Click PowerShell Web Installer & Bootstrapper
#  Repository: https://github.com/tanjeem180hz/180HzSetupHub
# ==============================================================================

[CmdletBinding()]
param(
    [switch]$Standalone,
    [switch]$Silent,
    [string]$DestinationDir = ""
)

$ErrorActionPreference = "Stop"

# Ensure modern TLS 1.2 / TLS 1.3 security protocols
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
} catch {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}

Write-Host ""
Write-Host " =============================================================== " -ForegroundColor DarkGreen
Write-Host "    180Hz Setup Hub - High-Refresh Performance Setup Assistant    " -ForegroundColor Green
Write-Host " =============================================================== " -ForegroundColor DarkGreen
Write-Host ""

$isElevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isElevated) {
    Write-Host " [i] Running with standard privileges. Elevation will be requested for installation." -ForegroundColor Yellow
} else {
    Write-Host " [OK] Administrator environment confirmed." -ForegroundColor Green
}

if ($Standalone) {
    Write-Host " [*] Mode: Standalone Full Offline Package (~81 MB)" -ForegroundColor Cyan
    $urls = @(
        "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe",
        "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/artifacts/publish/win-x64/180HzSetupHub.exe"
    )

    $targetDir = if (-not [string]::IsNullOrWhiteSpace($DestinationDir)) {
        $DestinationDir
    } else {
        Join-Path $env:LOCALAPPDATA "Programs\180Hz Setup Hub"
    }

    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    $targetExe = Join-Path $targetDir "180HzSetupHub.exe"
    $minSizeBytes = 50000000 # 50 MB minimum
} else {
    Write-Host " [*] Mode: Official Bootstrapper Installer (~3.6 MB)" -ForegroundColor Cyan
    $urls = @(
        "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe",
        "https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/artifacts/installer/180HzSetupHubSetup.exe",
        "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/installer/180HzSetupHubSetup.exe"
    )

    $tempDir = [System.IO.Path]::GetTempPath()
    $targetExe = Join-Path $tempDir "180HzSetupHubSetup.exe"
    $minSizeBytes = 2000000 # 2 MB minimum
}

Write-Host " [*] Downloading package from GitHub..." -ForegroundColor Cyan

$downloadSuccess = $false
foreach ($url in $urls) {
    try {
        Write-Host "     Connecting to: $url" -ForegroundColor Gray
        
        # Clean old incomplete temporary file
        if (Test-Path -LiteralPath $targetExe) {
            try { Remove-Item -LiteralPath $targetExe -Force -ErrorAction SilentlyContinue } catch { }
        }

        # Multi-engine download fallback for maximum PowerShell compatibility (5.1 -> 7+)
        $webClient = New-Object System.Net.WebClient
        $webClient.Headers.Add("User-Agent", "180HzSetupHub-PSInstaller/1.0")
        $webClient.DownloadFile($url, $targetExe)

        if ((Test-Path -LiteralPath $targetExe) -and ((Get-Item -LiteralPath $targetExe).Length -ge $minSizeBytes)) {
            $downloadSuccess = $true
            $sizeMb = [Math]::Round(((Get-Item -LiteralPath $targetExe).Length / 1MB), 2)
            Write-Host " [OK] Download completed successfully ($sizeMb MB)." -ForegroundColor Green
            break
        }
    } catch {
        Write-Host "     Warning: Mirror unavailable ($($_.Exception.Message)). Trying next fallback..." -ForegroundColor Yellow
    }
}

if (-not $downloadSuccess) {
    # Final attempt with Invoke-WebRequest
    try {
        Invoke-WebRequest -Uri $urls[0] -OutFile $targetExe -UseBasicParsing -TimeoutSec 120
        if ((Test-Path -LiteralPath $targetExe) -and ((Get-Item -LiteralPath $targetExe).Length -ge $minSizeBytes)) {
            $downloadSuccess = $true
            $sizeMb = [Math]::Round(((Get-Item -LiteralPath $targetExe).Length / 1MB), 2)
            Write-Host " [OK] Download completed successfully via web request ($sizeMb MB)." -ForegroundColor Green
        }
    } catch { }
}

if (-not $downloadSuccess) {
    Write-Host ""
    Write-Host " [X] Failed to download package. Please check your internet connection." -ForegroundColor Red
    Write-Host "     Manual Download: https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe" -ForegroundColor Yellow
    Write-Host "     Repository:      https://github.com/tanjeem180hz/180HzSetupHub" -ForegroundColor Cyan
    exit 1
}

# Launching package
Write-Host " [*] Launching 180Hz Setup Hub..." -ForegroundColor Green

$arguments = ""
if ($Silent) {
    $arguments = "--silent"
}

try {
    if (-not $isElevated) {
        Start-Process -FilePath $targetExe -ArgumentList $arguments -Verb RunAs
    } else {
        Start-Process -FilePath $targetExe -ArgumentList $arguments
    }
    Write-Host " [OK] 180Hz Setup Hub started successfully!" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host " [*] Starting process without elevation prompt..." -ForegroundColor Yellow
    Start-Process -FilePath $targetExe -ArgumentList $arguments
}
