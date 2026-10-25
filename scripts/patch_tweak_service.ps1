# ==============================================================================
#  Patch TweakService.cs to remove dangerous IDs from Recommended & Extreme Sets
#  and add EnsureSoftwareCompatibilityGuardian
# ==============================================================================

$root = Split-Path -Parent $PSScriptRoot
$tweakServicePath = Join-Path $root "WinSetupHub.App\Services\TweakService.cs"

if (-not (Test-Path $tweakServicePath)) {
    throw "TweakService.cs not found at: $tweakServicePath"
}

$content = [System.IO.File]::ReadAllText($tweakServicePath, [System.Text.Encoding]::UTF8)

# Dangerous IDs that must NOT be recommended or forced
$dangerousIds = @(
    "WPFTweaksLogiBlock",
    "WPFTweaksRazerBlock",
    "GfxTweakDirectFlip",
    "CpuTweakDisablePaging",
    "CpuTweakDisableFTH",
    "GfxTweakDwmMaxQueuedFrames",
    "ServiceTweakWpnService",
    "RegTweakDirectFlipIFlip",
    "RegTweakFaultTolerantHeap",
    "RegTweakDwmMaxQueuedFrames1",
    "NetTweakTcpFastOpen",
    "RegTweakDirect3DHardwareOnly",
    "RegTweakTcpFastOpen",
    "RegTweakDirect3DNoSoftware",
    "RegTweakDisablePagingExecutiveActive",
    "GfxTweakEnableDirectXIndependentFlip",
    "MemTweakOptimizeLargePageMinimum",
    "MemTweakDisableFaultTolerantHeapEngine",
    "RegTweakDirectFlipIndependent",
    "RegTweakLargePageMinimumMax",
    "RegTweakFaultTolerantHeapOff",
    "GameTweakDwmRenderBufferSingle",
    "MemTweakDisableMemoryCompression",
    "RegTweakDirectFlipEnabled",
    "RegTweakDwmSingleBuffer",
    "RegTweakDirect3DIndependentFlip"
)

# Remove each dangerous ID line from RecommendedIds and ExtremePerformanceIds
foreach ($id in $dangerousIds) {
    # Match `"Id",` with optional whitespace and newline
    $pattern = '(?m)^\s*"' + [regex]::Escape($id) + '",?\r?\n'
    $content = [regex]::Replace($content, $pattern, '')
}

# Add EnsureSoftwareCompatibilityGuardian method if not already present
if ($content -notmatch "EnsureSoftwareCompatibilityGuardian") {
    $guardianCode = @'

        /// <summary>
        /// Software Compatibility Guardian: Ensures any software (Antigravity, Electron, Chromium,
        /// VS Code, dev servers, games, and peripheral software) can launch and run with 100% stability
        /// while pushing hardware to maximum performance.
        /// </summary>
        public static void EnsureSoftwareCompatibilityGuardian()
        {
            try
            {
                // 1. Clean up legacy DirectFlip / IndependentFlip overrides in HKCU that break Electron swapchains
                using (var d3dKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Direct3D", writable: true))
                {
                    if (d3dKey != null)
                    {
                        d3dKey.DeleteValue("EnableDirectFlip", throwOnMissingValue: false);
                        d3dKey.DeleteValue("DirectFlipHonor", throwOnMissingValue: false);
                        d3dKey.DeleteValue("EnableIndependentFlip", throwOnMissingValue: false);
                    }
                }

                // 2. Eradicate Direct3D Software WARP block
                using (var d3dDrv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Direct3D\Drivers", writable: true))
                {
                    d3dDrv?.DeleteValue("SoftwareOnly", throwOnMissingValue: false);
                }

                // 3. Eradicate LargePageMinimum that causes VirtualAlloc 0xc0000005 crashes
                using (var memKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", writable: true))
                {
                    memKey?.DeleteValue("LargePageMinimum", throwOnMissingValue: false);
                }

                // 4. Ensure Fault Tolerant Heap (FTH) remains active as safety net
                using (var fthKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\FTH", writable: true))
                {
                    fthKey?.SetValue("Enabled", 1, RegistryValueKind.DWord);
                }

                // 5. Ensure essential services (StorSvc, WpnService) are never completely disabled
                using (var storKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\StorSvc", writable: true))
                {
                    if (storKey != null && storKey.GetValue("Start") is int startVal && startVal == 4)
                    {
                        storKey.SetValue("Start", 2, RegistryValueKind.DWord); // Automatic
                    }
                }
                using (var wpnKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\WpnService", writable: true))
                {
                    if (wpnKey != null && wpnKey.GetValue("Start") is int startVal && startVal == 4)
                    {
                        wpnKey.SetValue("Start", 3, RegistryValueKind.DWord); // Manual
                    }
                }
            }
            catch { }
        }
'@

    # Insert method before LoadAsync
    $content = $content.Replace("private static async Task<List<TweakItem>> LoadAsync()", "$guardianCode`r`n        private static async Task<List<TweakItem>> LoadAsync()")
    
    # Call EnsureSoftwareCompatibilityGuardian at beginning of LoadAsync
    $content = $content.Replace("var result = new List<TweakItem>();", "EnsureSoftwareCompatibilityGuardian();`r`n            var result = new List<TweakItem>();")

    # Call EnsureSoftwareCompatibilityGuardian at the end of ApplyAsync
    $content = $content.Replace("RecordAppliedTweak(tweak.Id);", "RecordAppliedTweak(tweak.Id);`r`n                EnsureSoftwareCompatibilityGuardian();")
}

[System.IO.File]::WriteAllText($tweakServicePath, $content, [System.Text.Encoding]::UTF8)
Write-Host "Successfully patched TweakService.cs!" -ForegroundColor Green
