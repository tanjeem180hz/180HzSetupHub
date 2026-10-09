$appPath = "C:\Users\PSYCHOPATH\Downloads\WinSetupHub\artifacts\publish\win-x64\180HzSetupHub.exe"
if (-not (Test-Path -LiteralPath $appPath)) {
    $appPath = "C:\Users\PSYCHOPATH\Downloads\WinSetupHub\artifacts\installer\180HzSetupHubSetup.exe"
}

Write-Host "Launching 180Hz Setup Hub elevated on PC..." -ForegroundColor Cyan

$taskName = "Run180HzSetupHub"
# Create one-time task with highest privileges to bypass non-interactive elevation limits
schtasks /create /tn $taskName /tr "`"$appPath`"" /sc once /st 00:00 /f /rl highest | Out-Null
schtasks /run /tn $taskName | Out-Null
Start-Sleep -Seconds 1
schtasks /delete /tn $taskName /f | Out-Null

Start-Sleep -Seconds 2
$procs = Get-Process -Name "180HzSetupHub" -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host "180Hz Setup Hub is RUNNING on your desktop! (PID: $($procs[0].Id))" -ForegroundColor Green
} else {
    Write-Host "Triggered launch via Windows Shell." -ForegroundColor Yellow
    & explorer.exe $appPath
}
