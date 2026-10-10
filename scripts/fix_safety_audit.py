import json
import os

system_files = [
    "WinSetupHub.App/Configuration/tweaks.default.json",
    "WinSetupHub.Setup/Payload/Configuration/tweaks.default.json",
    "new folder/Source/WinSetupHub.App/Configuration/tweaks.default.json",
    "new folder/Source/WinSetupHub.Setup/Payload/Configuration/tweaks.default.json"
]

for sf in system_files:
    if not os.path.exists(sf):
        continue
    with open(sf, "r", encoding="utf-8") as f:
        tweaks = json.load(f)
        
    for t in tweaks:
        tid = t.get("id")
        
        if tid == "WPFTweaksServices":
            t["undoScript"] = [
                "Remove-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control' -Name 'SvcHostSplitThresholdInKB' -ErrorAction SilentlyContinue"
            ]
        elif tid == "WPFTweaksWidget":
            t["undoScript"] = [
                "winget install --id 9MSSGKG348SP --accept-source-agreements --accept-package-agreements --silent 2>$null"
            ]
        elif tid == "WPFTweaksWindowsAI":
            t["undoScript"] = [
                "Set-Service -Name WSAIFabricSvc -StartupType Manual -ErrorAction SilentlyContinue; Enable-WindowsOptionalFeature -FeatureName Recall -Online -NoRestart -ErrorAction SilentlyContinue 2>$null"
            ]
        elif tid in ["MemTweakFlushStandby", "WPFTweaksDiskCleanup", "WPFTweaksRestorePoint", "WPFTweaksDeleteTempFiles"]:
            t["undoScript"] = [
                "Write-Host 'Maintenance action state is clean.' -ForegroundColor Gray"
            ]
        elif tid == "WPFMultiplaneOverlay":
            for r in t.get("registry", []):
                if not r.get("OriginalValue"):
                    r["OriginalValue"] = "<RemoveEntry>"
                    
    with open(sf, "w", encoding="utf-8") as f:
        json.dump(tweaks, f, indent=2, ensure_ascii=False)
        
    print(f"Patched safety undo coverage in: {sf}")

print("Safety audit fixes applied successfully!")
