<p align="center">
  <img src="WinSetupHub.Setup/Assets/BrandArtwork.png" alt="180Hz Setup Hub" width="800" style="border-radius: 8px;">
</p>

<h1 align="center">180Hz Setup Hub</h1>

<p align="center">
  <strong>The Ultimate Windows Post-Install &amp; Gaming Optimization Setup Assistant</strong>
</p>

<p align="center">
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-00FF66?style=for-the-badge&logo=windows&logoColor=050B06" alt="Platform"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe"><img src="https://img.shields.io/badge/Installer%20Size-~3.6%20MB-00FF66?style=for-the-badge&logo=speedtest&logoColor=050B06" alt="Installer Size"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe"><img src="https://img.shields.io/badge/Standalone%20App-~81%20MB-00FF66?style=for-the-badge&logo=windows&logoColor=050B06" alt="Standalone App"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe"><img src="https://img.shields.io/badge/Runtime-.NET%208%20%2B%204.8-162919?style=for-the-badge&logo=dotnet&logoColor=00FF66" alt=".NET"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/blob/main/SECURITY.md"><img src="https://img.shields.io/badge/Security-Hardened-00FF66?style=for-the-badge&logo=shield&logoColor=050B06" alt="Security"></a>
</p>

---

## ⚡ 1-Click Direct Download & Installation

Choose your preferred way to install or run 180Hz Setup Hub:

<p align="center">
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe">
    <img src="https://img.shields.io/badge/Download-Official%20Installer%20(~3.6%20MB)-00FF66?style=for-the-badge&logo=windows&logoColor=050B06" alt="Download 180Hz Setup Hub Installer" height="48">
  </a>
  &nbsp;&nbsp;
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe">
    <img src="https://img.shields.io/badge/Download-Standalone%20Full%20App%20(~81%20MB)-162919?style=for-the-badge&logo=windows&logoColor=00FF66" alt="Download Standalone Full App Package" height="48">
  </a>
</p>

### 📥 Download Options

| Package | Size | Description | Link |
| :--- | :--- | :--- | :--- |
| **Official Bootstrapper (In-Repo)** | **~3.6 MB** | Fast web bootstrapper installer with live speed meter & self-repair | [📥 Download 180HzSetupHubSetup.exe (Raw)](https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe) • [View in Repo](artifacts/installer/180HzSetupHubSetup.exe) |
| **Official Direct Binary** | **~3.6 MB** | Permanent repo artifact bootstrapper | [⚡ Direct Artifact Link](https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/installer/180HzSetupHubSetup.exe) |
| **Standalone Full App Package** | **~81 MB** | Offline single-file desktop application (No installation required) | [📦 Download Standalone 180HzSetupHub.exe (Raw)](https://github.com/tanjeem180hz/180HzSetupHub/raw/main/artifacts/publish/win-x64/180HzSetupHub.exe) • [View in Repo](artifacts/publish/win-x64/180HzSetupHub.exe) |

### 💻 1-Click PowerShell Install (No Browser Needed)

If you prefer installing or running directly from the terminal, open **PowerShell** and run:

```powershell
# Recommended: Official Web Installer (~3.6 MB)
irm https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/scripts/install.ps1 | iex

# Alternative: Standalone Full Offline Package (~81 MB)
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/scripts/install.ps1))) -Standalone
```

---

## 🌟 Key Highlights

### 🎨 Cyber Carbon & Toxic Neon App Installer Experience
* **High-Tech Cyber Aesthetic:** Precision Cyber Carbon (`#080B08`) and Toxic Neon (`#00FF66`) interface matching the 180Hz Setup Hub theme with verified **PRO** and **Verified Safe** badges.
* **Launch When Ready:** Automatically starts 180Hz Setup Hub once installation finishes.
* **Instant Startup:** Runs on native .NET Framework 4.8 pre-built into Windows 10 & 11, starting in milliseconds without demanding extra runtimes or external popups.

### 📶 Real-Time Dynamic Download Speed Meter
* Measures live download throughput during installation and repair.
* Dynamically scales units based on connection speed:
  * Low / Mobile: `650.4 KB/s`
  * Broadband: `14.2 MB/s`
  * Gigabit Fiber: `1.1 GB/s`

### 🛠️ Built-in Repair & Self-Healing Diagnostics
* The Modify / Uninstall wizard contains a dedicated **Repair** feature.
* **1-Click Repair:** Automatically stops frozen processes, fixes PowerShell script execution policies, verifies Windows Package Manager (`winget`), clears corrupted temp files, and restores clean package catalogs and shortcuts without touching your saved settings.

### 📦 Windows Package Manager (`winget`) Integration
* Installs apps directly from Microsoft's official verified repository manifests with automatic checksum verification.
* Checks prerequisites silently and configures `winget` automatically if missing on the host system.

### 🎯 Curated Setup Profiles
* **Gamers & Enthusiasts:** Discord, Steam, Epic Games, OBS Studio, GPU drivers, and essential gaming runtimes.
* **Developers:** Visual Studio Code, Git, Windows Terminal, Node.js, Python, Docker Desktop, DBeaver.
* **Productivity & Utilities:** 7-Zip, Everything, Microsoft PowerToys, Notepad++, VLC, Chrome, Firefox.

### ⚡ Dual-Section Optimization Suite & Deep Registry Tuning Engine
* **Two Dedicated Optimization Modules:** Cleanly partitioned into **⚡ System Tweaks** and **🗃️ Registry Optimization** with instant segmented pill switching, category filters, and real-time search.
* **System Optimizations:** Fine-tunes high-performance power plans, Windows visual effects, telemetry reduction, and gaming priority presets.
* **Deep Windows Registry Engine:** Curated catalog of 32+ high-impact registry modifications across 7 performance sectors:
  * *Gaming & Input Latency:* Disables mouse acceleration, optimizes `CSRSS` thread priority, enables exclusive Game Mode flags, and eliminates fullscreen optimization input lag.
  * *Network & Ping Optimization:* Disables Windows network throttling index (`NetworkThrottlingIndex`), sets TCP ACK frequency (`TcpAckFrequency`), configures non-delayed TCP delivery (`TCPNoDelay`), and tunes MMCSS gaming QoS.
  * *CPU & RAM Scheduling:* Maximizes Win32 processor scheduling priority for foreground gaming tasks, disables paging executive (`DisablePagingExecutive`) to keep kernel in RAM, and trims idle memory footprint.
  * *Windows UI & Shell Responsiveness:* Drops menu show delay to 0ms, disables window animation latency, and minimizes shell hover delays for razor-sharp response.
  * *GPU & DirectX Acceleration:* Enables Hardware Accelerated GPU Scheduling (HAGS), disables DWM composition delays, and activates DirectX performance flags.
  * *Privacy & Telemetry:* Eliminates Connected User Experiences diagnostic telemetry, activity tracking, and Cortana background overhead.
  * *Filesystem & Disk Throughput:* Disables NTFS 8.3 short-name creation and last-access time updates for ultra-fast SSD/NVMe read/write cycles.
* **Target Path Badges & Instant Checkpoint:** Every registry tweak displays exact registry root paths, keys, and values (`🔑 HKLM\... ➔ Name = Value`) with a 1-click **🛡️ RESTORE POINT** backup generator before applying bulk modifications.

### 🛡️ Revo-Grade Deep Uninstallation Engine
* **5-Phase Advanced Methodology:** Creates system protection checkpoints and registry backups, executes official vendor uninstallers with graceful fallback, then performs multi-level heuristic residual scans (Safe, Moderate, Advanced).
* **Residual Registry & File Eradication:** Scans both 64-bit and 32-bit registry trees, `%AppData%`, `%ProgramData%`, and Program Files roots to safely purge orphaned registry keys, leftover app data, and shortcut remnants with defense-in-depth OS protection.
* **Instant Invalidation & Live Sync:** Automatically refreshes catalog state and invalidates caches across all sections upon completion so uninstalled apps never remain marked as installed.

### 🧭 Universal History Navigation & Hardware Mouse Control
* **Persistent Title Bar Navigation:** Sleek `[ ‹ Back ]` pill button, `[ › ]` forward button, and dynamic breadcrumbs with glowing status indicator accessible across every section.
* **Full Hardware Mouse Support:** Seamless backward (`Mouse 4` / `XButton1`) and forward (`Mouse 5` / `XButton2`) navigation compatible with all gaming and productivity mice (Logitech, Razer, Corsair, etc.) via WPF input and Win32 `WM_APPCOMMAND` / `WM_XBUTTONDOWN`.
* **Keyboard Hotkeys:** Rapid navigation using `Alt + Left Arrow`, `Alt + Right Arrow`, or `Backspace`.
* **Zero-Latency In-Memory Stack:** Instantaneous page switching with state preservation and background memory trimming.

---

## 🖥️ System Requirements

| Specification | Requirement |
| :--- | :--- |
| **Operating System** | Windows 10 (version 1903 or later) / Windows 11 (64-bit) |
| **Processor** | 64-bit (x64) compatible processor |
| **Memory (RAM)** | 2 GB minimum (4 GB recommended) |
| **Storage** | ~150 MB free disk space |
| **Network** | Active internet connection for downloading package catalogs and apps |

---

## 📁 Installation Directory & Paths

180Hz Setup Hub installs cleanly in user-space without cluttering system roots:

```text
%LOCALAPPDATA%\Programs\180Hz Setup Hub\
├── 180HzSetupHub.exe          # Main application executable
├── Uninstall.exe              # Uninstaller & Repair wizard
├── Configuration\
│   ├── packages.default.json        # Curated default package catalog
│   ├── tweaks.default.json          # Curated system optimization tweaks
│   ├── registry_tweaks.default.json # Curated Windows registry tuning catalog
│   └── appsettings.default.json
└── Data\
    ├── config\                # User-modified catalog & settings
    ├── downloads\             # Cached downloads
    └── logs\                  # Session diagnostic logs
```

---

## 🛠️ Adding Custom Apps to the Catalog

Add any software to your catalog by editing:

```text
%LOCALAPPDATA%\Programs\180Hz Setup Hub\Data\config\packages.json
```

Add an object with this structure:

```json
{
  "id": "Google.Chrome",
  "name": "Google Chrome",
  "category": "Browsers",
  "description": "Fast and secure web browser by Google.",
  "iconUrl": "https://www.google.com/s2/favicons?domain=google.com&sz=128",
  "accentColor": "#4285F4",
  "essential": true
}
```

> **Tip:** Search for any package ID via PowerShell:
> ```powershell
> winget search "app name"
> ```

---

## 🔨 Building from Source

### 1-Click Automated Build
To compile both the application package and the 2 MB bootstrapper installer:

```powershell
powershell.exe -ExecutionPolicy Bypass -File scripts\Build-Installer.ps1
```

Generated outputs:
* **Bootstrapper Installer:** `artifacts\installer\180HzSetupHubSetup.exe` (~3.6 MB)
* **Standalone App Binary:** `artifacts\publish\win-x64\180HzSetupHub.exe` (~81 MB)

---

## 🔒 Security & Integrity

* See [`SECURITY.md`](SECURITY.md) for vulnerability reporting and security policies.
* See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for internal architecture, component interaction, and design patterns.

---

<p align="center">
  Developed with ❤️ by <a href="https://github.com/tanjeem180hz"><strong>Muhammad Tanjeem (180Hz)</strong></a>
</p>
