$jsonPath = Join-Path $PSScriptRoot "..\WinSetupHub.App\Configuration\packages.default.json"
$pkgs = Get-Content $jsonPath -Raw | ConvertFrom-Json

Write-Host "Total packages in catalog: $($pkgs.Count)"

# Let's inspect which packages have version strings
$hasVer = $pkgs | Where-Object { $_.version }
Write-Host "Packages with version: $($hasVer.Count)"
