$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$jsonPath = Join-Path $root 'WinSetupHub.App\Configuration\packages.default.json'
$setupJsonPath = Join-Path $root 'WinSetupHub.Setup\Payload\Configuration\packages.default.json'
$themeJsonPath = Join-Path $root '180HzSetupHubTheme\packages.default.json'

$packages = [System.Collections.Generic.List[PSObject]](Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json)

$storeApps = @(
    [PSCustomObject]@{
        id = 'Microsoft.WindowsStore'
        name = 'Microsoft Store'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Official Windows App Store for downloading apps, games, and entertainment.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=microsoft.com/store&sz=128'
        accentColor = '#0078D4'
        essential = $true
        webUrl = 'ms-windows-store://'
        version = 'v22408'
        size = '140 MB'
    },
    [PSCustomObject]@{
        id = '9NKSQGP7F2NH'
        name = 'WhatsApp (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Fast, simple, secure messaging and video calling from Microsoft Store.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=whatsapp.com&sz=128'
        accentColor = '#25D366'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9nksqgp7f2nh'
        version = 'v2.2438.5'
        size = '145 MB'
    },
    [PSCustomObject]@{
        id = '9WZDNCRFJ3TJ'
        name = 'Netflix (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Stream TV series, movies, documentaries, and specials in 4K HDR.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=netflix.com&sz=128'
        accentColor = '#E50914'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9wzdncrfj3tj'
        version = 'v6.99.5'
        size = '85 MB'
    },
    [PSCustomObject]@{
        id = '9NCBCSZSJRSB'
        name = 'Spotify Music (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Millions of songs and podcasts directly from Microsoft Store.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=spotify.com&sz=128'
        accentColor = '#1ED760'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9ncbcszsjrsb'
        version = 'v1.2.46'
        size = '95 MB'
    },
    [PSCustomObject]@{
        id = '9N0DX20HK701'
        name = 'Windows Terminal (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Modern, fast, and powerful multi-tab terminal for Windows command line.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=github.com/microsoft/terminal&sz=128'
        accentColor = '#4E5F70'
        essential = $true
        webUrl = 'https://apps.microsoft.com/detail/9n0dx20hk701'
        version = 'v1.21.2361'
        size = '48 MB'
    },
    [PSCustomObject]@{
        id = 'XP89DCGQ3K6VLD'
        name = 'Microsoft PowerToys (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Power utilities to customize and streamline Windows for greater efficiency.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=github.com/microsoft/powertoys&sz=128'
        accentColor = '#0078D4'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/xp89dcgq3k6vld'
        version = 'v0.84.1'
        size = '165 MB'
    },
    [PSCustomObject]@{
        id = '9PB2MZ1ZMB1S'
        name = 'iTunes (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Apple Music, movies, TV shows, and backup management for iPhone and iPad.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=apple.com&sz=128'
        accentColor = '#FA2D48'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9pb2mz1zmb1s'
        version = 'v12.13.3'
        size = '210 MB'
    },
    [PSCustomObject]@{
        id = '9NBLGGH5L9XT'
        name = 'Instagram (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Connect with friends, share stories, and explore photos and reels on PC.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=instagram.com&sz=128'
        accentColor = '#E4405F'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9nblggh5l9xt'
        version = 'v42.0.2'
        size = '65 MB'
    },
    [PSCustomObject]@{
        id = '9PKNWZZ719L5'
        name = 'TikTok (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Trending short-form mobile videos directly on your Windows desktop.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=tiktok.com&sz=128'
        accentColor = '#000000'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9pknwzz719l5'
        version = 'v1.0.8'
        size = '58 MB'
    },
    [PSCustomObject]@{
        id = '9NZTWSQNTD0S'
        name = 'Telegram Desktop (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Pure instant messaging — simple, fast, secure, and synced across devices.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=telegram.org&sz=128'
        accentColor = '#24A1DE'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9nztwsqntd0s'
        version = 'v5.5.5'
        size = '82 MB'
    },
    [PSCustomObject]@{
        id = '9MV0B5HZVK9Z'
        name = 'Xbox App (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Discover and play PC Game Pass titles, connect with friends, and cloud game.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=xbox.com&sz=128'
        accentColor = '#107C10'
        essential = $true
        webUrl = 'https://apps.microsoft.com/detail/9mv0b5hzvk9z'
        version = 'v2409.1001'
        size = '120 MB'
    },
    [PSCustomObject]@{
        id = 'XPDC2RH70K22MN'
        name = 'Discord (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Free voice, video, and text communication service used by millions.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=discord.com&sz=128'
        accentColor = '#5865F2'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/xpdc2rh70k22mn'
        version = 'v1.0.9164'
        size = '89 MB'
    },
    [PSCustomObject]@{
        id = '9NBLGGH4VVNH'
        name = 'VLC (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Universal open-source multimedia player that plays most audio and video formats.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=videolan.org&sz=128'
        accentColor = '#FF8800'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9nblggh4vvnh'
        version = 'v3.1.2'
        size = '52 MB'
    },
    [PSCustomObject]@{
        id = '9WZDNCRDK3WP'
        name = 'Slack (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Productivity platform that brings team chat and tools together.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=slack.com&sz=128'
        accentColor = '#4A154B'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9wzdncrdk3wp'
        version = 'v4.39.95'
        size = '98 MB'
    },
    [PSCustomObject]@{
        id = '9P6RC76MSMMJ'
        name = 'Adobe Acrobat Reader (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Trusted free standard for viewing, printing, signing, and annotating PDFs.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=adobe.com&sz=128'
        accentColor = '#EC1C24'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9p6rc76msmmj'
        version = 'v24.003'
        size = '280 MB'
    },
    [PSCustomObject]@{
        id = '9P1J8S7CCWWT'
        name = 'Clipchamp (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Fast and easy video editor by Microsoft with AI enhancements.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=clipchamp.com&sz=128'
        accentColor = '#7E57C2'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9p1j8s7ccwwt'
        version = 'v3.1.1'
        size = '75 MB'
    },
    [PSCustomObject]@{
        id = '9NBLGGH5R558'
        name = 'Microsoft To Do (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Smart daily planner to organize your tasks, reminders, and checklists.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=todo.microsoft.com&sz=128'
        accentColor = '#2D6BB5'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9nblggh5r558'
        version = 'v2.124'
        size = '45 MB'
    },
    [PSCustomObject]@{
        id = '9PDXGNCFSCZV'
        name = 'Ubuntu 24.04 LTS (MS Store)'
        category = 'Microsoft Store'
        source = 'msstore'
        description = 'Run Ubuntu Linux directly on Windows without a traditional VM.'
        iconUrl = 'https://www.google.com/s2/favicons?domain=ubuntu.com&sz=128'
        accentColor = '#E95420'
        essential = $false
        webUrl = 'https://apps.microsoft.com/detail/9pdxgncfsczv'
        version = 'v2404.0'
        size = '620 MB'
    }
)

$existingIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($p in $packages) { [void]$existingIds.Add($p.id) }

foreach ($sa in $storeApps) {
    if (-not $existingIds.Contains($sa.id)) {
        $packages.Add($sa)
        [void]$existingIds.Add($sa.id)
    }
}

$jsonOutput = $packages | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($jsonPath, $jsonOutput, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText($setupJsonPath, $jsonOutput, [System.Text.Encoding]::UTF8)
[System.IO.File]::WriteAllText($themeJsonPath, $jsonOutput, [System.Text.Encoding]::UTF8)

Write-Host "Total packages now: $($packages.Count) (including Microsoft Store apps)." -ForegroundColor Green
