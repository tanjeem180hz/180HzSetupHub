# 180Hz Setup Hub - 1-Click PowerShell Web Installer
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$primaryUrl = "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe"
$fallbackUrl = "https://github.com/tanjeem180hz/180HzSetupHub/raw/main/installer/180HzSetupHubSetup.exe"
$installerPath = Join-Path $env:TEMP "180HzSetupHubSetup.exe"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " 180Hz Setup Hub - 1-Click Web Installer  " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Fetching installer package (~2 MB)..." -ForegroundColor Yellow

$downloaded = $false
foreach ($url in @($primaryUrl, $fallbackUrl)) {
    try {
        Invoke-WebRequest -Uri $url -OutFile $installerPath -UseBasicParsing
        if ((Test-Path $installerPath) -and (Get-Item $installerPath).Length -gt 1000000) {
            $downloaded = $true
            break
        }
    } catch {
        # Try next fallback
    }
}

if ($downloaded) {
    Write-Host "Download complete! Launching 180Hz Setup Hub..." -ForegroundColor Green
    Start-Process -FilePath $installerPath
} else {
    Write-Host "Could not download installer directly." -ForegroundColor Red
    Write-Host "Please download manually from: https://github.com/tanjeem180hz/180HzSetupHub/releases/tag/v1.0.0" -ForegroundColor Yellow
}
