# 180Hz Setup Hub - 1-Click PowerShell Web Installer
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

$downloadUrl = "https://github.com/tanjeem180hz/180HzSetupHub/releases/latest/download/180HzSetupHubSetup.exe"
$installerPath = Join-Path $env:TEMP "180HzSetupHubSetup.exe"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " 180Hz Setup Hub - 1-Click Web Installer  " -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Fetching latest installer package (~2 MB)..." -ForegroundColor Yellow

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath -UseBasicParsing
    Write-Host "Download complete. Starting 180Hz Setup Hub..." -ForegroundColor Green
    Start-Process -FilePath $installerPath
} catch {
    Write-Host "Error downloading installer: $_" -ForegroundColor Red
    Write-Host "Please download manually from: https://github.com/tanjeem180hz/180HzSetupHub/releases" -ForegroundColor Yellow
}
