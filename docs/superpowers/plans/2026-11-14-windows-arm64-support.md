# Windows 11 ARM64 Support & Volunteer Guide Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add non-breaking `arm64` (`win-arm64` / `aarch64-unknown-windows-msvc`) build and toolchain support, a `windows-11-arm` GitHub Actions artifact workflow, and a Windows 11 ARM64 volunteer testing guide without altering default `x64` devbox behavior.

**Architecture:** `VRemindWin.csproj`, `VRemindWin.sln`, and the PowerShell scripts under `scripts/` gain opt-in `-Architecture <x64|arm64>` parameters that default to `x64` so existing devbox commands behave identically. A `.github/workflows/windows-arm64.yml` workflow builds, tests, and packages `VRemindWin-arm64.zip` natively on `windows-11-arm`, paired with `docs/WINDOWS-ARM64.md` and a volunteer callout in `README.md`.

**Tech Stack:** C# / .NET 9 / WinUI 3 (`win-x64`, `win-arm64`), Swift 6.0.3 (`x86_64-unknown-windows-msvc`, `aarch64-unknown-windows-msvc`), PowerShell 5.1+, GitHub Actions (`windows-11-arm`).

**Spec:** `docs/superpowers/specs/2026-11-14-windows-arm64-support-design.md`

## Global Constraints

- `x64` runtime target: Windows 10 version 1809 (build `17763`, Enterprise LTSC 2019)+ and Windows 11 (`x64`).
- `arm64` runtime target: **Windows 11 (`arm64`) only** (Snapdragon X, NVIDIA RTX Spark, Apple Silicon VMs); Windows 10 on ARM is explicitly unsupported.
- Zero-regression `x64` devbox builds: Running `check-toolchain.ps1`, `install-toolchain.ps1`, `build-cli.ps1`, `build-app.ps1`, `dotnet build`, or `dotnet test` with no `-Architecture` flag must default to `x64` and require zero ARM64 tools installed locally.
- `check-toolchain.ps1` must make zero network calls (`0 MB`), and `install-toolchain.ps1` must refuse downloads unless `-ConfirmDownload` is passed.
- `build-cli.ps1 -Architecture arm64` must never copy `x64` Swift runtime DLLs into an `arm64` bundle.

## Review Focus

- Running `dotnet build VRemindWin\VRemindWin.sln -c Debug -p:Platform=x64` without `-r` must still resolve `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` and succeed with 0 warnings/errors.
- Running `check-toolchain.ps1 -Architecture arm64 -Json` on an `x64` machine missing `VC.Tools.ARM64` must report `VsCppAndWin10Sdk.Installed == false` and `TargetArchitecture == "arm64"` while default `check-toolchain.ps1 -Json` still reports `AllInstalled == true`.
- Running `install-toolchain.ps1 -Architecture arm64` without `-ConfirmDownload` when `VC.Tools.ARM64` is missing must exit with code `2` and print the `-ConfirmDownload` metered-data warning without downloading anything.
- Running `build-cli.ps1 -Architecture arm64` on an `x64` host where `Runtimes\6.0.3\usr\bin\swiftCore.dll` is PE `AMD64` (`0x8664`) must refuse to stage mismatched `x64` Swift runtime DLLs unless `-SwiftRuntimeDir` points to `ARM64` (`0xAA64`) DLLs.
- Running `dotnet publish VRemindWin\src\VRemindWin\VRemindWin.csproj -c Release -r win-arm64 -p:Platform=ARM64` on the `x64` devbox must cross-compile the managed/WinUI 3 host cleanly.

---

### Task 1: Multi-Architecture Project Config & Opt-In `-Architecture` Scripts

**Files:**
- Modify: `VRemindWin/src/VRemindWin/VRemindWin.csproj`
- Modify: `VRemindWin/VRemindWin.sln`
- Modify: `scripts/check-toolchain.ps1`
- Modify: `scripts/install-toolchain.ps1`
- Modify: `scripts/build-cli.ps1`
- Modify: `scripts/build-app.ps1`
- Test: `scripts/tests/verify-toolchain-scripts.ps1`

**Interfaces:**
- Produces:
  - `check-toolchain.ps1 [-Json] [-Architecture <x64|arm64>]` (default `'x64'`; JSON output adds `TargetArchitecture`)
  - `install-toolchain.ps1 [-ConfirmDownload] [-Architecture <x64|arm64>] [-SkipDotNet] [-SkipVsBuildTools] [-SkipSwift] [-SkipGit]` (default `'x64'`)
  - `build-cli.ps1 [-Configuration <debug|release>] [-Architecture <x64|arm64>] [-OutputDir <string>] [-SwiftRuntimeDir <string>]` (default `'x64'`)
  - `build-app.ps1 [-Configuration <Debug|Release>] [-Architecture <x64|arm64>] [-OutputDir <string>]` (default `'x64'`; default `$OutputDir` is `dist\VRemindWin` for `x64` and `dist\VRemindWin-arm64` for `arm64`)

- [ ] **Step 1: Extend `scripts/tests/verify-toolchain-scripts.ps1` with failing multi-arch assertions**

Add checks to `scripts/tests/verify-toolchain-scripts.ps1` verifying:
1. Default `check-toolchain.ps1 -Json` returns `TargetArchitecture -eq 'x64'`.
2. `check-toolchain.ps1 -Architecture arm64 -Json` returns `TargetArchitecture -eq 'arm64'` and valid prerequisite objects without network calls.
3. `install-toolchain.ps1 -Architecture arm64` without `-ConfirmDownload` exits `2` (or `0` if ARM64 tools are installed) without downloading.
4. `VRemindWin.csproj` declares `<Platforms>x64;ARM64</Platforms>` and `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>`.
5. `build-cli.ps1` and `build-app.ps1` expose an `-Architecture` parameter accepting `x64` and `arm64`.

- [ ] **Step 2: Run `scripts/tests/verify-toolchain-scripts.ps1` to verify it fails**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\tests\verify-toolchain-scripts.ps1`
Expected: FAIL on missing `TargetArchitecture` / `-Architecture` parameter.

- [ ] **Step 3: Update `VRemindWin.csproj` and `VRemindWin.sln` for `x64` + `ARM64`**

- In `VRemindWin/src/VRemindWin/VRemindWin.csproj`, replace `<Platforms>x64</Platforms>` and `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` with:
  ```xml
  <Platforms>x64;ARM64</Platforms>
  <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
  <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and '$(Platform)' == 'ARM64'">win-arm64</RuntimeIdentifier>
  <RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-x64</RuntimeIdentifier>
  ```
- In `VRemindWin/VRemindWin.sln`, add `Debug|ARM64` and `Release|ARM64` in `SolutionConfigurationPlatforms` and map `VRemindWin` (`{A8C24E11-2D10-4E83-B90B-3B7D27F1D902}`) to `Debug|ARM64` / `Release|ARM64` and `VRemindWin.Core` / `VRemindWin.Tests` to `Debug|Any CPU` / `Release|Any CPU`.

- [ ] **Step 4: Add `-Architecture <x64|arm64>` to `check-toolchain.ps1`, `install-toolchain.ps1`, `build-cli.ps1`, and `build-app.ps1`**

- `scripts/check-toolchain.ps1`:
  - Add `[ValidateSet('x64', 'arm64')] [string]$Architecture = 'x64'`.
  - Use `$vcComponent = if ($Architecture -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' } else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }` when querying `vswhere.exe`.
  - Include `TargetArchitecture = $Architecture.ToLowerInvariant()` in `$report`.
- `scripts/install-toolchain.ps1`:
  - Add `[ValidateSet('x64', 'arm64')] [string]$Architecture = 'x64'` and pass `-Architecture $Architecture` to `check-toolchain.ps1`.
  - When installing VS Build Tools for `arm64`, include `'--add', 'Microsoft.VisualStudio.Component.VC.Tools.ARM64'`.
  - When installing Swift or .NET 9 on an ARM64 OS (`$env:PROCESSOR_ARCHITECTURE -eq 'ARM64'`), use `https://aka.ms/dotnet/9.0/dotnet-sdk-win-arm64.exe` and `https://download.swift.org/swift-6.0.3-release/windows10-arm64/swift-6.0.3-RELEASE/swift-6.0.3-RELEASE-windows10-arm64.exe`.
- `scripts/build-cli.ps1`:
  - Add `[ValidateSet('x64', 'arm64')] [string]$Architecture = 'x64'` and `[string]$SwiftRuntimeDir = ''`.
  - For `arm64`: require `Microsoft.VisualStudio.Component.VC.Tools.ARM64` via `vswhere.exe`, locate `vcvarsarm64.bat` (if host is ARM64) or `vcvarsamd64_arm64.bat` (on x64 host), pass `--triple aarch64-unknown-windows-msvc` to `swift build`, and read `.build\aarch64-unknown-windows-msvc\$swiftConfig\vremind-core.exe`.
  - Add helper `Get-PeMachine($Path)` reading the PE COFF Machine `uint16` (`0x8664` for `x64`, `0xAA64` for `arm64`) on `swiftCore.dll` in `$runtimeDir`; throw a descriptive error if the runtime DLL architecture does not match `$Architecture`.
- `scripts/build-app.ps1`:
  - Add `[ValidateSet('x64', 'arm64')] [string]$Architecture = 'x64'`.
  - Default `$OutputDir` to `dist\VRemindWin` when `$Architecture -eq 'x64'` and `dist\VRemindWin-arm64` when `$Architecture -eq 'arm64'`.
  - Pass `-Architecture $ArchLower` to `build-cli.ps1` (using `artifacts\cli` for `x64` and `artifacts\cli-arm64` for `arm64`) and run `dotnet publish $winuiProj -c $Configuration -r win-$ArchLower -p:Platform=$($Architecture.ToUpperInvariant()) -o $OutputDir`.

- [ ] **Step 5: Run verification script and solution builds**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\tests\verify-toolchain-scripts.ps1
dotnet build VRemindWin\VRemindWin.sln -c Debug -p:Platform=x64
dotnet publish VRemindWin\src\VRemindWin\VRemindWin.csproj -c Release -r win-arm64 -p:Platform=ARM64 -o artifacts\test-winui-arm64
Remove-Item -Recurse -Force artifacts\test-winui-arm64
```
Expected: All commands exit `0` with `PASS: Toolchain scripts verified.` and `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 6: Commit Task 1**

```powershell
& "C:\Program Files\Git\cmd\git.exe" add VRemindWin/VRemindWin.sln VRemindWin/src/VRemindWin/VRemindWin.csproj scripts/
& "C:\Program Files\Git\cmd\git.exe" commit -m "feat: add opt-in Windows 11 ARM64 build and toolchain script support"
```

---

### Task 2: GitHub Actions `windows-11-arm` Workflow & Volunteer Documentation

**Files:**
- Create: `.github/workflows/windows-arm64.yml`
- Create: `docs/WINDOWS-ARM64.md`
- Modify: `README.md`
- Modify: `scripts/tests/verify-toolchain-scripts.ps1`

**Interfaces:**
- Consumes: `build-app.ps1 -Architecture arm64`, `check-toolchain.ps1 -Architecture arm64` from Task 1.
- Produces: `.github/workflows/windows-arm64.yml`, `docs/WINDOWS-ARM64.md`, and updated `README.md`.

- [ ] **Step 1: Add workflow and documentation checks to `scripts/tests/verify-toolchain-scripts.ps1`**

Append assertions to `scripts/tests/verify-toolchain-scripts.ps1` verifying:
1. `.github/workflows/windows-arm64.yml` exists and specifies `runs-on: windows-11-arm`, `build-app.ps1 -Architecture arm64`, and `VRemindWin-arm64.zip`.
2. `docs/WINDOWS-ARM64.md` exists and documents Windows 11 ARM64 testing (Task Manager `ARM64` check, pre-built `.zip` path, and source-build path).
3. `README.md` links to `docs/WINDOWS-ARM64.md` and states that ARM64 requires Windows 11.

- [ ] **Step 2: Run `scripts/tests/verify-toolchain-scripts.ps1` to verify it fails**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\tests\verify-toolchain-scripts.ps1`
Expected: FAIL because `.github/workflows/windows-arm64.yml` does not exist yet.

- [ ] **Step 3: Create `.github/workflows/windows-arm64.yml`, `docs/WINDOWS-ARM64.md`, and update `README.md`**

- Create `.github/workflows/windows-arm64.yml`:
  - Triggers: `workflow_dispatch`, `push` (`branches: [main]`, `tags: ['v*']`), `pull_request`.
  - Job `build-arm64` on `runs-on: windows-11-arm`:
    - Checkout repo (`actions/checkout@v4`).
    - Setup `.NET 9 SDK` (`actions/setup-dotnet@v4` with `dotnet-version: '9.0.x'`).
    - Install Swift `6.0.3` for Windows ARM64 (`https://download.swift.org/swift-6.0.3-release/windows10-arm64/swift-6.0.3-RELEASE/swift-6.0.3-RELEASE-windows10-arm64.exe` `/quiet`) if `swift.exe` is not present, and export `SDKROOT`.
    - Run `swift test --package-path VRemindCoreCLI -Xcc -D__MM_MALLOC_H` (via `vcvarsarm64.bat`) and `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`.
    - Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1 -Configuration Release -Architecture arm64`.
    - Compress `dist\VRemindWin-arm64` into `dist\VRemindWin-arm64.zip` and upload via `actions/upload-artifact@v4` (`name: VRemindWin-arm64`).
- Create `docs/WINDOWS-ARM64.md`:
  - Explain why Windows 11 ARM64 is supported (Snapdragon X, NVIDIA RTX Spark, Apple Silicon VMs) and why Windows 10 on ARM is excluded.
  - **Option 1: Quick Test (No Compiler Needed):** Download `VRemindWin-arm64.zip` from the latest GitHub Actions `Windows 11 ARM64 Build` artifact (or Releases page), extract, and run `VRemindWin.exe`.
  - **Option 2: Build & Run Tests from Source on Windows 11 ARM64:** Step-by-step commands using `.\scripts\check-toolchain.ps1 -Architecture arm64`, `.\scripts\install-toolchain.ps1 -Architecture arm64 -ConfirmDownload`, `swift test`, `dotnet test`, and `.\scripts\build-app.ps1 -Architecture arm64`.
  - **Volunteer Verification Checklist:** Confirm `VRemindWin.exe` and `vremind-core.exe` both show **`ARM64`** in Task Manager → Details → Architecture, test YouTube + Twitch live/offline polling, test `360×440` tray flyout & DPI scaling, test COM toast notifications, test DirectX 10 Celebration Mode confetti & Reduce Motion gradient, test JSON Backup/Restore, and open a GitHub Issue with findings.
- Update `README.md`:
  - Add a **Windows 11 on ARM64 — Volunteers Wanted!** callout section linking to [`docs/WINDOWS-ARM64.md`](docs/WINDOWS-ARM64.md).
  - Clarify under **Requirements** and **Installation** that `x64` supports Windows 10 1809 LTSC+ & Windows 11, while `ARM64` requires **Windows 11 (`arm64`)**.

- [ ] **Step 4: Run full devbox verification (`verify-toolchain-scripts.ps1`, `swift test`, `dotnet test`, `build-app.ps1`)**

Run:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\tests\verify-toolchain-scripts.ps1
dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1
```
Expected: All pass with exit code `0`, confirming the `x64` devbox build is completely unaffected.

- [ ] **Step 5: Commit Task 2**

```powershell
& "C:\Program Files\Git\cmd\git.exe" add .github/workflows/windows-arm64.yml docs/WINDOWS-ARM64.md README.md scripts/tests/verify-toolchain-scripts.ps1
& "C:\Program Files\Git\cmd\git.exe" commit -m "docs(arm64): add windows-11-arm CI workflow and volunteer testing guide"
```
