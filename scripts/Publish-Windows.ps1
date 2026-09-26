param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$CompressSingleFile
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "WinSetupHub.App\WinSetupHub.App.csproj"
$output = Join-Path $root "artifacts\publish\$Runtime"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
$compressionEnabled = if ($CompressSingleFile) { "true" } else { "false" }

$userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
if (Test-Path -LiteralPath $userDotnet) {
    $dotnet = $userDotnet
} elseif (-not $dotnet) {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
}

if (-not (Test-Path -LiteralPath $dotnet)) {
    throw ".NET SDK was not found. Install Microsoft.DotNet.SDK.8, then run this script again."
}

& $dotnet publish $project `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=$compressionEnabled `
    --output $output

Write-Host "Published to $output"
