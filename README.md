<p align="center">
  <img src="WinSetupHub.Setup/Assets/BrandArtwork.png" alt="180Hz Setup Hub" width="800" style="border-radius: 8px;">
</p>

<h1 align="center">180Hz Setup Hub</h1>

<p align="center">
  <strong>The Ultimate Windows Post-Install &amp; Gaming Optimization Setup Assistant</strong>
</p>

<p align="center">
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/releases/download/v1.0.0/180HzSetupHubSetup.exe"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D7?style=for-the-badge&logo=windows" alt="Platform"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/releases/download/v1.0.0/180HzSetupHubSetup.exe"><img src="https://img.shields.io/badge/Installer%20Size-~2%20MB-107C10?style=for-the-badge&logo=speedtest" alt="Installer Size"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/releases/download/v1.0.0/180HzSetupHubSetup.exe"><img src="https://img.shields.io/badge/Runtime-.NET%208%20%2B%204.8-512BD4?style=for-the-badge&logo=dotnet" alt=".NET"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/releases/download/v1.0.0/180HzSetupHubSetup.exe"><img src="https://img.shields.io/badge/Architecture-x64-555555?style=for-the-badge" alt="Architecture"></a>
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/blob/main/SECURITY.md"><img src="https://img.shields.io/badge/Security-Hardened-0078D7?style=for-the-badge&logo=shield" alt="Security"></a>
</p>

---

## ⚡ 1-Click Direct Download

Click the button below to download the official installer:

<p align="center">
  <a href="https://github.com/tanjeem180hz/180HzSetupHub/releases/download/v1.0.0/180HzSetupHubSetup.exe">
    <img src="https://img.shields.io/badge/Download-180Hz%20Setup%20Hub%20(v1.0.0)-0078D7?style=for-the-badge&logo=windows&logoColor=white" alt="Download 180Hz Setup Hub" height="48">
  </a>
</p>

<p align="center">
  👉 <strong><a href="https://github.com/tanjeem180hz/180HzSetupHub/releases/download/v1.0.0/180HzSetupHubSetup.exe">Direct Download Link: 180HzSetupHubSetup.exe (~2 MB)</a></strong><br>
  <em>(Official 1-Click Installer • Windows 10 &amp; 11 • Live Speed Meter • Automatic Prerequisites &amp; Self-Repair)</em>
</p>

### 💻 1-Click PowerShell Install (No Browser Needed)

If you prefer installing directly from the terminal, open **PowerShell** and run this single command:

```powershell
irm https://raw.githubusercontent.com/tanjeem180hz/180HzSetupHub/main/scripts/install.ps1 | iex
```

---

## 🌟 Key Highlights

### 🎨 Windows 10/11 Native App Installer Experience
* **Native Design:** Modeled after the official Windows App Installer with a clean Segoe UI layout, verified **Trusted App** badge, and standard titlebar controls.
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
│   ├── packages.default.json  # Curated default package catalog
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
* **Installer:** `artifacts\installer\180HzSetupHubSetup.exe` (~1.93 MB)
* **App Binary:** `artifacts\publish\win-x64\180HzSetupHub.exe` (~68 MB)

---

## 🔒 Security & Integrity

* See [`SECURITY.md`](SECURITY.md) for vulnerability reporting and security policies.
* See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) for internal architecture, component interaction, and design patterns.

---

<p align="center">
  Developed with ❤️ by <a href="https://github.com/tanjeem180hz"><strong>Muhammad Tanjeem (180Hz)</strong></a>
</p>
