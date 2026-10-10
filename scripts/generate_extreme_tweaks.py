import json
import os

new_system_tweaks_json = """[
  {
    "id": "EmulTweakHypervisorVBS",
    "name": "Emulator 100% CPU Utilization & VBS/Hypervisor Bypass",
    "description": "Disables Windows Hypervisor and Virtualization-Based Security (VBS/HVCI) overhead, giving BlueStacks, LDPlayer, Nox, MEMU 100% direct bare-metal access to hardware VT-x / AMD-V with zero emulation stutter.",
    "category": "🚀 Emulator 100% Performance",
    "categoryOrder": 0,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\DeviceGuard",
        "Name": "EnableVirtualizationBasedSecurity",
        "Value": "0",
        "OriginalValue": "1",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\DeviceGuard\\\\Scenarios\\\\HypervisorEnforcedCodeIntegrity",
        "Name": "Enabled",
        "Value": "0",
        "OriginalValue": "1",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [
      "bcdedit /set hypervisorlaunchtype off"
    ],
    "undoScript": [
      "bcdedit /set hypervisorlaunchtype auto"
    ],
    "link": "https://support.bluestacks.com/hc/en-us/articles/360055244412-How-to-disable-Hyper-V-on-Windows-for-BlueStacks"
  },
  {
    "id": "EmulTweakCpuPriority",
    "name": "Android & VM Emulator Realtime CPU Priority (BlueStacks/LDPlayer/Nox/MEMU)",
    "description": "Configures Windows kernel IFEO to automatically execute all major emulator processes (HD-Player.exe, dnplayer.exe, LdBoxHeadless.exe, Nox.exe, MEmu.exe) with High CPU Priority and High I/O Priority for maximum frame rates.",
    "category": "🚀 Emulator 100% Performance",
    "categoryOrder": 0,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\HD-Player.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\HD-Player.exe\\\\PerfOptions",
        "Name": "IoPriority",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\dnplayer.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\dnplayer.exe\\\\PerfOptions",
        "Name": "IoPriority",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\LdBoxHeadless.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\LdBoxHeadless.exe\\\\PerfOptions",
        "Name": "IoPriority",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\Nox.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\MEmu.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "EmulTweakGpuPreference",
    "name": "Force Discrete High-Performance GPU for All Emulators",
    "description": "Forces Windows DirectX graphics runtime to route all Android and console emulator graphics rendering directly through your high-power discrete GPU instead of integrated graphics.",
    "category": "🚀 Emulator 100% Performance",
    "categoryOrder": 0,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "HD-Player.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "dnplayer.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "LdBoxHeadless.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "Nox.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "MEmu.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "EmulTweakPowerThrottling",
    "name": "Disable CPU Power Throttling & EcoQoS for Emulators",
    "description": "Stops Windows 10/11 from throttling multi-threaded Android emulator processes, preventing emulator CPU threads from getting relegated to slow efficiency states.",
    "category": "🚀 Emulator 100% Performance",
    "categoryOrder": 0,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\Power\\\\PowerThrottling",
        "Name": "PowerThrottlingOff",
        "Value": "1",
        "OriginalValue": "0",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "GfxTweakVisualFX",
    "name": "Windows Visual Effects - Ultimate Gaming Performance Mode",
    "description": "Disables window animations, menu fade delays, drop shadows, and taskbar transition latency while keeping crisp ClearType font rendering.",
    "category": "🖥️ Graphical & Visual Responsiveness",
    "categoryOrder": 1,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\Windows\\\\CurrentVersion\\\\Explorer\\\\VisualEffects",
        "Name": "VisualFXSetting",
        "Value": "2",
        "OriginalValue": "1",
        "Type": "DWord"
      },
      {
        "Path": "HKCU:\\\\Control Panel\\\\Desktop\\\\WindowMetrics",
        "Name": "MinAnimate",
        "Value": "0",
        "OriginalValue": "1",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Control Panel\\\\Desktop",
        "Name": "UserPreferencesMask",
        "Value": "90 12 03 80 10 00 00 00",
        "OriginalValue": "9e 1e 07 80 12 00 00 00",
        "Type": "Binary"
      },
      {
        "Path": "HKCU:\\\\Control Panel\\\\Desktop",
        "Name": "MenuShowDelay",
        "Value": "0",
        "OriginalValue": "400",
        "Type": "String"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "GfxTweakHAGS",
    "name": "Hardware-Accelerated GPU Scheduling (HAGS)",
    "description": "Offloads high-frequency video memory management directly to the GPU's dedicated scheduling processor, cutting frame latency and improving 1% low FPS.",
    "category": "🖥️ Graphical & Visual Responsiveness",
    "categoryOrder": 1,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\GraphicsDrivers",
        "Name": "HwSchMode",
        "Value": "2",
        "OriginalValue": "1",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "GfxTweakShaderCache",
    "name": "DirectX Shader Cache Maximize (1GB+ for Zero Stutter)",
    "description": "Sets the global DirectX shader disk cache to 1GB to prevent in-game shader compilation stutters in modern games and 3D emulators.",
    "category": "🖥️ Graphical & Visual Responsiveness",
    "categoryOrder": 1,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Direct3D",
        "Name": "DirectXDriverCacheSize",
        "Value": "1073741824",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "GfxTweakFSE",
    "name": "Disable Fullscreen Optimizations & DWM Overlay Delay",
    "description": "Enforces true exclusive fullscreen mode and disables Windows Desktop Window Manager composition overhead for all DX11/DX12 games.",
    "category": "🖥️ Graphical & Visual Responsiveness",
    "categoryOrder": 1,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKCU:\\\\System\\\\GameConfigStore",
        "Name": "GameDVR_FSEBehavior",
        "Value": "2",
        "OriginalValue": "0",
        "Type": "DWord"
      },
      {
        "Path": "HKCU:\\\\System\\\\GameConfigStore",
        "Name": "GameDVR_HonorUserFSEBehaviorMode",
        "Value": "1",
        "OriginalValue": "0",
        "Type": "DWord"
      },
      {
        "Path": "HKCU:\\\\System\\\\GameConfigStore",
        "Name": "GameDVR_DXGIHonorFSEWindowsCompatible",
        "Value": "1",
        "OriginalValue": "0",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "GfxTweakMPO",
    "name": "Disable Multi-Plane Overlay (MPO) Micro-Stutter Fix",
    "description": "Eliminates infamous DWM multi-plane overlay frame drops, black screens, and micro-stutters across NVIDIA, AMD, and Intel GPUs.",
    "category": "🖥️ Graphical & Visual Responsiveness",
    "categoryOrder": 1,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows\\\\Dwm",
        "Name": "OverlayTestMode",
        "Value": "5",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "KernelTweakDynamicTick",
    "name": "Disable Dynamic Tick (Kernel Clock Drift Elimination)",
    "description": "Stops variable timer tick drift by locking the Windows kernel timer to a constant frequency, eliminating micro-stutters and frame jitter.",
    "category": "⚡ Extreme Low Latency & Kernel Timers",
    "categoryOrder": 2,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": true,
    "registry": [],
    "service": [],
    "invokeScript": [
      "bcdedit /set disabledynamictick yes"
    ],
    "undoScript": [
      "bcdedit /set disabledynamictick no"
    ]
  },
  {
    "id": "KernelTweakTSCClock",
    "name": "Force Hardware Invariant TSC Clock (Bypass HPET Latency)",
    "description": "Bypasses high-overhead motherboard HPET timer chips and forces Windows to synchronize with the CPU's direct on-die Time Stamp Counter (TSC).",
    "category": "⚡ Extreme Low Latency & Kernel Timers",
    "categoryOrder": 2,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": true,
    "registry": [],
    "service": [],
    "invokeScript": [
      "bcdedit /set useplatformclock no",
      "bcdedit /set useplatformtick yes"
    ],
    "undoScript": [
      "bcdedit /deletevalue useplatformclock",
      "bcdedit /deletevalue useplatformtick"
    ]
  },
  {
    "id": "KernelTweakTSCSync",
    "name": "Enhanced CPU Time Stamp Counter (TSC) Synchronization",
    "description": "Enforces strict TSC counter synchronization across all CPU cores for seamless multi-core game execution and zero frame pacing hitches.",
    "category": "⚡ Extreme Low Latency & Kernel Timers",
    "categoryOrder": 2,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": true,
    "registry": [],
    "service": [],
    "invokeScript": [
      "bcdedit /set tscsyncpolicy Enhanced"
    ],
    "undoScript": [
      "bcdedit /set tscsyncpolicy Default"
    ]
  },
  {
    "id": "KernelTweakSysResponsiveness",
    "name": "System Responsiveness 100% Gaming Reservation",
    "description": "Sets Multimedia System Profile SystemResponsiveness to 0%, eliminating Windows' default 20% CPU reservation for background tasks.",
    "category": "⚡ Extreme Low Latency & Kernel Timers",
    "categoryOrder": 2,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Multimedia\\\\SystemProfile",
        "Name": "SystemResponsiveness",
        "Value": "0",
        "OriginalValue": "20",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Multimedia\\\\SystemProfile",
        "Name": "NetworkThrottlingIndex",
        "Value": "4294967295",
        "OriginalValue": "10",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "CpuTweakUnparkCores",
    "name": "Unpark All CPU Cores & Ultimate Performance Plan",
    "description": "Enables Windows Ultimate Performance power scheme and forces CPU core unparking to 100%, ensuring all physical and logical cores remain awake and ready.",
    "category": "🧠 CPU, RAM & Power Tuning",
    "categoryOrder": 3,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [],
    "service": [],
    "invokeScript": [
      "powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61",
      "powercfg /setactive e9a42b02-d5df-448d-aa00-03f14749eb61",
      "powercfg -setacvalueindex scheme_current sub_processor CPMIN 100",
      "powercfg -setactive scheme_current"
    ],
    "undoScript": [
      "powercfg /setactive 381b4222-f694-41f0-9685-ff5bb260df2e"
    ]
  },
  {
    "id": "CpuTweakWin32Priority",
    "name": "Win32 Priority Separation (Foreground Game Quanta Boost)",
    "description": "Configures processor scheduling thread quanta to 38 (0x26), granting 3:1 high priority boost to foreground gaming and emulator processes.",
    "category": "🧠 CPU, RAM & Power Tuning",
    "categoryOrder": 3,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\PriorityControl",
        "Name": "Win32PrioritySeparation",
        "Value": "38",
        "OriginalValue": "2",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "CpuTweakDisablePaging",
    "name": "Disable Paging Executive (Lock Kernel & Drivers in RAM)",
    "description": "Prevents Windows NT kernel and hardware driver code from ever being paged out to disk, keeping all system calls directly in ultra-fast RAM.",
    "category": "🧠 CPU, RAM & Power Tuning",
    "categoryOrder": 3,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\Session Manager\\\\Memory Management",
        "Name": "DisablePagingExecutive",
        "Value": "1",
        "OriginalValue": "0",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\Session Manager\\\\Memory Management",
        "Name": "LargeSystemCache",
        "Value": "0",
        "OriginalValue": "0",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "MemTweakFlushStandby",
    "name": "Purge Standby Memory Cache & Process Working Sets",
    "description": "Instantly flushes stale Windows standby memory lists and frees fragmented RAM without needing third-party memory cleaners.",
    "category": "🧠 CPU, RAM & Power Tuning",
    "categoryOrder": 3,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [],
    "service": [],
    "invokeScript": [
      "[System.GC]::Collect(); [System.GC]::WaitForPendingFinalizers()"
    ],
    "undoScript": []
  },
  {
    "id": "NetTweakTcpAckNoDelay",
    "name": "Disable Nagle's Algorithm (TcpAckFrequency & TCPNoDelay)",
    "description": "Disables delayed packet acknowledgment and Nagle's bundling algorithm on all network adapters, dropping multiplayer game packet latency to minimum.",
    "category": "🌐 Network, TCP & Ultra-Low Ping",
    "categoryOrder": 4,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [],
    "service": [],
    "invokeScript": [
      "Get-ChildItem -Path 'HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Services\\\\Tcpip\\\\Parameters\\\\Interfaces' | ForEach-Object { Set-ItemProperty -Path $_.PSPath -Name 'TcpAckFrequency' -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue; Set-ItemProperty -Path $_.PSPath -Name 'TCPNoDelay' -Value 1 -Type DWord -Force -ErrorAction SilentlyContinue; Set-ItemProperty -Path $_.PSPath -Name 'TcpDelAckTicks' -Value 0 -Type DWord -Force -ErrorAction SilentlyContinue }"
    ],
    "undoScript": [
      "Get-ChildItem -Path 'HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Services\\\\Tcpip\\\\Parameters\\\\Interfaces' | ForEach-Object { Remove-ItemProperty -Path $_.PSPath -Name 'TcpAckFrequency' -ErrorAction SilentlyContinue; Remove-ItemProperty -Path $_.PSPath -Name 'TCPNoDelay' -ErrorAction SilentlyContinue; Remove-ItemProperty -Path $_.PSPath -Name 'TcpDelAckTicks' -ErrorAction SilentlyContinue }"
    ]
  },
  {
    "id": "NetTweakNetshRss",
    "name": "Optimize TCP Auto-Tuning & Multi-Core Receive Side Scaling (RSS)",
    "description": "Enables Receive Side Scaling to distribute incoming network packet interrupts across all CPU cores and disables high-latency TCP Chimney offloads.",
    "category": "🌐 Network, TCP & Ultra-Low Ping",
    "categoryOrder": 4,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [],
    "service": [],
    "invokeScript": [
      "netsh int tcp set global autotuninglevel=normal",
      "netsh int tcp set global rss=enabled",
      "netsh int tcp set global chimney=disabled",
      "netsh int tcp set global ecncapability=disabled"
    ],
    "undoScript": [
      "netsh int tcp set global autotuninglevel=normal",
      "netsh int tcp set global rss=default"
    ]
  },
  {
    "id": "DiskTweakNtfsFast",
    "name": "Disable NTFS 8.3 Short-Names & Last-Access Time Updates",
    "description": "Eliminates legacy 16-bit DOS 8.3 file creation and stops timestamp writes on every file read, significantly boosting SSD and NVMe read/write throughput.",
    "category": "💾 Storage & NVMe Throughput",
    "categoryOrder": 5,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\FileSystem",
        "Name": "NtfsDisable8dot3NameCreation",
        "Value": "1",
        "OriginalValue": "2",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\FileSystem",
        "Name": "NtfsDisableLastAccessUpdate",
        "Value": "1",
        "OriginalValue": "0",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "DiskTweakEnableTrim",
    "name": "Verify & Enable Hardware SSD TRIM",
    "description": "Ensures Windows TRIM command is enabled for all SSDs so write performance and flash endurance never degrade over time.",
    "category": "💾 Storage & NVMe Throughput",
    "categoryOrder": 5,
    "section": "System",
    "isAdvanced": false,
    "isRecommended": true,
    "requiresReboot": false,
    "registry": [],
    "service": [],
    "invokeScript": [
      "fsutil behavior set DisableDeleteNotify 0"
    ],
    "undoScript": [
      "fsutil behavior set DisableDeleteNotify 0"
    ]
  }
]"""

new_registry_tweaks_json = """[
  {
    "id": "RegTweakEmulBlueStacks",
    "name": "BlueStacks Realtime CPU & IO Priority (HD-Player.exe)",
    "description": "Configures Windows kernel scheduler IFEO to automatically run BlueStacks with High CPU Priority and High I/O Priority for maximum emulator FPS.",
    "category": "Emulator & VM Acceleration",
    "categoryOrder": 1,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\HD-Player.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\HD-Player.exe\\\\PerfOptions",
        "Name": "IoPriority",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "RegTweakEmulLDPlayer",
    "name": "LDPlayer Realtime CPU & IO Priority (dnplayer.exe & LdBox)",
    "description": "Configures Windows IFEO to run LDPlayer and LDBox VM core with High CPU Priority and High I/O Priority for stutter-free 120Hz/240Hz Android gaming.",
    "category": "Emulator & VM Acceleration",
    "categoryOrder": 1,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\dnplayer.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\dnplayer.exe\\\\PerfOptions",
        "Name": "IoPriority",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\LdBoxHeadless.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\LdBoxHeadless.exe\\\\PerfOptions",
        "Name": "IoPriority",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "RegTweakEmulNoxMemu",
    "name": "NoxPlayer & MEMU Realtime CPU Priority (Nox.exe & MEmu.exe)",
    "description": "Configures Windows IFEO to execute NoxPlayer and MEMU emulator processes with High CPU Priority and high thread scheduling preference.",
    "category": "Emulator & VM Acceleration",
    "categoryOrder": 1,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\Nox.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      },
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows NT\\\\CurrentVersion\\\\Image File Execution Options\\\\MEmu.exe\\\\PerfOptions",
        "Name": "CpuPriorityClass",
        "Value": "3",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "RegTweakEmulGpuDirectX",
    "name": "Force Discrete High-Performance GPU for All Android Emulators",
    "description": "Forces Windows DirectX graphics runtime to route all Android emulator rendering through your high-power discrete GPU.",
    "category": "Emulator & VM Acceleration",
    "categoryOrder": 1,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "HD-Player.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "dnplayer.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "LdBoxHeadless.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "Nox.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\DirectX\\\\UserGpuPreferences",
        "Name": "MEmu.exe",
        "Value": "GpuPreference=2;",
        "OriginalValue": "<RemoveEntry>",
        "Type": "String"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "RegTweakEmulPowerThrottling",
    "name": "Disable CPU Power Throttling & EcoQoS for Emulators",
    "description": "Stops Windows 10/11 from throttling multi-threaded Android emulator processes to slow efficiency states.",
    "category": "Emulator & VM Acceleration",
    "categoryOrder": 1,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SYSTEM\\\\CurrentControlSet\\\\Control\\\\Power\\\\PowerThrottling",
        "Name": "PowerThrottlingOff",
        "Value": "1",
        "OriginalValue": "0",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "RegTweakMPO",
    "name": "Disable Multi-Plane Overlay (MPO) Micro-Stutter Fix",
    "description": "Eliminates infamous Desktop Window Manager multi-plane overlay frame drops, black screens, and micro-stutters across NVIDIA, AMD, and Intel GPUs.",
    "category": "GPU & DirectX",
    "categoryOrder": 4,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Windows\\\\Dwm",
        "Name": "OverlayTestMode",
        "Value": "5",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "RegTweakShaderCache",
    "name": "DirectX Shader Cache Maximize (1GB+ for Zero Stutter)",
    "description": "Sets the global DirectX shader disk cache to 1GB to prevent in-game shader compilation stutters in modern games and 3D emulators.",
    "category": "GPU & DirectX",
    "categoryOrder": 4,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKLM:\\\\SOFTWARE\\\\Microsoft\\\\Direct3D",
        "Name": "DirectXDriverCacheSize",
        "Value": "1073741824",
        "OriginalValue": "<RemoveEntry>",
        "Type": "DWord"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  },
  {
    "id": "RegTweakVisualFXPerformance",
    "name": "Windows Visual Effects - Ultimate Gaming Performance Mode",
    "description": "Disables window animations, menu fade delays, drop shadows, and taskbar transition latency while keeping crisp ClearType font rendering.",
    "category": "Windows UI & Shell",
    "categoryOrder": 3,
    "section": "Registry",
    "isAdvanced": false,
    "isRecommended": true,
    "registry": [
      {
        "Path": "HKCU:\\\\Software\\\\Microsoft\\\\Windows\\\\CurrentVersion\\\\Explorer\\\\VisualEffects",
        "Name": "VisualFXSetting",
        "Value": "2",
        "OriginalValue": "1",
        "Type": "DWord"
      },
      {
        "Path": "HKCU:\\\\Control Panel\\\\Desktop\\\\WindowMetrics",
        "Name": "MinAnimate",
        "Value": "0",
        "OriginalValue": "1",
        "Type": "String"
      },
      {
        "Path": "HKCU:\\\\Control Panel\\\\Desktop",
        "Name": "UserPreferencesMask",
        "Value": "90 12 03 80 10 00 00 00",
        "OriginalValue": "9e 1e 07 80 12 00 00 00",
        "Type": "Binary"
      }
    ],
    "service": [],
    "invokeScript": [],
    "undoScript": []
  }
]"""

new_system_tweaks = json.loads(new_system_tweaks_json)
new_registry_tweaks = json.loads(new_registry_tweaks_json)

def update_file(file_path, new_items):
    with open(file_path, "r", encoding="utf-8") as f:
        existing = json.load(f)
    
    existing_ids = {x["id"].lower() for x in existing}
    added = 0
    
    to_prepend = []
    for item in new_items:
        if item["id"].lower() not in existing_ids:
            to_prepend.append(item)
            added += 1
            
    final_list = to_prepend + existing
    
    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(final_list, f, indent=2, ensure_ascii=False)
        
    print(f"Updated {file_path}: Added {added} items. Total: {len(final_list)}")
    return final_list

system_files = [
    "WinSetupHub.App/Configuration/tweaks.default.json",
    "WinSetupHub.Setup/Payload/Configuration/tweaks.default.json",
    "new folder/Source/WinSetupHub.App/Configuration/tweaks.default.json",
    "new folder/Source/WinSetupHub.Setup/Payload/Configuration/tweaks.default.json"
]

for sf in system_files:
    if os.path.exists(sf):
        update_file(sf, new_system_tweaks)

registry_files = [
    "WinSetupHub.App/Configuration/registry_tweaks.default.json",
    "WinSetupHub.Setup/Payload/Configuration/registry_tweaks.default.json",
    "new folder/Source/WinSetupHub.App/Configuration/registry_tweaks.default.json",
    "new folder/Source/WinSetupHub.Setup/Payload/Configuration/registry_tweaks.default.json"
]

for rf in registry_files:
    if os.path.exists(rf):
        update_file(rf, new_registry_tweaks)

print("All configuration files synchronized successfully!")
