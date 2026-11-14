# Windows 11 on ARM64 — Volunteer Testing & Build Guide

Windows on ARM64 has taken off recently thanks to **Qualcomm Snapdragon X** laptops/mini-PCs, **NVIDIA RTX Spark / Grace-Blackwell** workstations, and **Apple Silicon Macs** running Windows 11 in Parallels Desktop or VMware Fusion.

Because the primary development box for `VRemind-win` is an `x64` Windows 10 IoT Enterprise LTSC workstation, we are looking for volunteers running **Windows 11 on ARM64** to test native `win-arm64` builds and report back!

> [!IMPORTANT]
> **OS Requirement (`ARM64`):** The ARM64 build of VRemind **only supports Windows 11 on ARM64 (22H2 or newer)**. Windows 10 on ARM is a discontinued/dead platform and is not supported. (For `x64` PCs, Windows 10 version 1809 LTSC and newer remain fully supported.)

---

## Option A: Quick Volunteer Testing (No Compiler Required — 5 Minutes)

If you just want to test the native ARM64 app on your Snapdragon X, NVIDIA ARM64 system, or Apple Silicon Windows 11 VM without installing any build tools:

1. **Download the pre-built ARM64 artifact:**
   - Open the repository's [**Actions → Windows ARM64 Build**](../../actions/workflows/windows-arm64.yml) tab (or the [**Releases**](../../releases) page if attached to the latest release).
   - Click the latest green workflow run on `main` and download the **`VRemindWin-arm64`** artifact (`VRemindWin-arm64.zip`).
2. **Extract and launch:**
   - Extract `VRemindWin-arm64.zip` to any folder (for example, `C:\Tools\VRemindWin-arm64\`).
   - Run `VRemindWin.exe`.

### Verification Checklist

Please walk through these checks and copy the checklist below into a GitHub Issue or Discussion:

- [ ] **Native ARM64 Processes (Zero x64 Emulation):**
  - Open **Task Manager → Details** tab, right-click the column header → **Select columns**, and enable **Architecture**.
  - Verify `VRemindWin.exe` shows **`ARM64`**.
  - Trigger a refresh from the tray menu (*Refresh Now*) and verify `vremind-core.exe` briefly spawns as **`ARM64`** (or check its PE header via PowerShell below).
- [ ] **System Tray & Popover Flyout:**
  - Left-clicking the tray icon opens the 2-column popover flyout (`NOW LIVE`, `UPCOMING (<24H)`, `LATER`).
  - Right-clicking opens the native context menu.
- [ ] **Settings & Holodex / YouTube Sync:**
  - Open **Settings**, add an API key (Holodex and/or YouTube Data API v3), search and add a channel, and click **Refresh Now**.
  - Verify upcoming and live streams populate accurately.
- [ ] **Toast Notifications:**
  - In **Settings → General**, click **Send Test Notification** and verify a native Windows 11 toast notification appears.
- [ ] **Celebration (Party) Mode:**
  - Toggle **Party Mode** from the popover header and verify the animated gradient border renders smoothly on your ARM64 GPU (Adreno / RTX / Apple Virtual GPU).
- [ ] **Launch at Login & Backup/Restore:**
  - Toggle **Launch at Login** and test **Export Backup** in Settings.

---

## Option B: Building from Source on a Windows 11 ARM64 Machine

If you want to compile both the Swift `VRemindCoreCLI` engine (`aarch64-unknown-windows-msvc`) and the WinUI 3 `VRemindWin` shell (`win-arm64`) locally on your Windows 11 ARM64 machine:

### 1. Check Toolchain Status (Offline, 0 MB Network Usage)

Open PowerShell at the repository root and run:

```powershell
.\scripts\check-toolchain.ps1 -Architecture arm64
```

This checks for:
- `.NET 9 SDK` (`ARM64`)
- `Visual Studio 2022 Build Tools` with `Microsoft.VisualStudio.Component.VC.Tools.ARM64` + Windows 10/11 SDK
- `Swift for Windows 6.0.3` (`ARM64` installer)

### 2. Install Any Missing Prerequisites (~2–4 GB)

```powershell
.\scripts\install-toolchain.ps1 -Architecture arm64 -ConfirmDownload
```

> [!NOTE]
> Once installation completes, **close and reopen PowerShell** so `PATH` and `SDKROOT` environment variables refresh.

### 3. Run Unit Tests

```powershell
# Run Swift CLI contract tests
swift test --package-path VRemindCoreCLI -Xcc -D__MM_MALLOC_H

# Run .NET 9 xUnit tests
dotnet test .\VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj
```

### 4. Build and Package the Self-Contained ARM64 Bundle

```powershell
.\scripts\build-app.ps1 -Configuration Release -Architecture arm64
```

This produces a self-contained, zero-redistributable folder at:
- `dist\VRemindWin-arm64\VRemindWin.exe` (`ARM64`)
- `dist\VRemindWin-arm64\vremind-core.exe` (`ARM64`, along with native `ARM64` Swift runtime DLLs)

You can verify that every binary in the bundle is native `ARM64` (`0xAA64`) using PowerShell:

```powershell
foreach ($file in @('VRemindWin.exe', 'vremind-core.exe', 'swiftCore.dll')) {
    $p = Join-Path .\dist\VRemindWin-arm64 $file
    $bytes = [System.IO.File]::ReadAllBytes($p)
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    Write-Host ("{0,-20} -> 0x{1:X4} ({2})" -f $file, $machine, $(if ($machine -eq 0xAA64) { 'ARM64' } elseif ($machine -eq 0x8664) { 'x64' } else { 'Other' }))
}
```

---

## Option C: Cross-Compiling from an `x64` Devbox (Maintainer Reference)

Your normal `x64` workflow (`.\scripts\check-toolchain.ps1`, `.\scripts\build-app.ps1`, `dotnet build`) defaults to `x64` and **never requires ARM64 tools installed locally**.

If you ever want to cross-compile the `ARM64` build from an `x64` Windows machine:
1. **WinUI 3 / .NET 9 (`VRemindWin.exe`)** cross-compiles out of the box on any `x64` machine with the `.NET 9 SDK`:
   ```powershell
   dotnet publish .\VRemindWin\src\VRemindWin\VRemindWin.csproj -c Release -r win-arm64 -p:Platform=ARM64 -o .\artifacts\winui-arm64
   ```
2. **Swift CLI (`vremind-core.exe`)** requires:
   - Visual Studio C++ ARM64 build tools (`Microsoft.VisualStudio.Component.VC.Tools.ARM64`, installed via `.\scripts\install-toolchain.ps1 -Architecture arm64 -ConfirmDownload`).
   - Extracted `ARM64` Swift runtime DLLs (`swiftCore.dll`, `Foundation*.dll`, etc.) passed via `-SwiftRuntimeDir <path>` to `.\scripts\build-cli.ps1 -Architecture arm64` (because the `x64` Swift installer only ships `x64` runtime DLLs under `Runtimes\6.0.3\usr\bin`, and `build-cli.ps1` inspects the PE COFF header to prevent ever mixing `x64` DLLs into an `arm64` bundle).
   - For most releases, letting the GitHub Actions `windows-11-arm` runner build and upload `VRemindWin-arm64.zip` is simpler and requires zero extra disk space on your `x64` devbox.

---

## Reporting Results

When opening a GitHub Issue with your test results, please include:
1. **Device / SoC** (e.g., Surface Laptop 7 Snapdragon X Elite, Lenovo Yoga Slim 7x Snapdragon X Plus, NVIDIA RTX Spark, MacBook Pro M3 Max running Parallels Desktop 20)
2. **Windows 11 Build Number** (run `winver` or `[Environment]::OSVersion.Version`)
3. **Completed Verification Checklist** above and any screenshots or logs from `%LOCALAPPDATA%\VRemind\logs\` if something misbehaved.
