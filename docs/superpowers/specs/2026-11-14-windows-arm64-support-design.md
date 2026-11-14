# VRemind for Windows (`VRemind-win`) Windows 11 ARM64 Support & Volunteer Testing Design

- **Date:** 2026-11-14
- **Scope:** Small Change (Multi-architecture build configs, opt-in `arm64` scripts, `windows-11-arm` CI workflow, and ARM64 volunteer guide)

---

## 1. Intent and Why
Windows on ARM has gained real traction on **Snapdragon X** laptops, **NVIDIA RTX Spark** systems, and **Apple Silicon Macs running Windows 11 inside Parallels / VMware Fusion**. Because the author develops on an `x64` Windows 10 IoT Enterprise LTSC 2021 machine, `VRemind-win` needs two things:
1. Non-breaking `arm64` build/test infrastructure (`.csproj`, `.sln`, PowerShell scripts, and a GitHub Actions `windows-11-arm` workflow) that produces a ready-to-test `VRemindWin-arm64.zip` without altering default `x64` devbox behavior.
2. A visible call for **Windows 11 ARM64 volunteers** in `README.md` backed by a step-by-step testing and source-build guide (`docs/WINDOWS-ARM64.md`).

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
- **Zero-Regression `x64` Devbox Builds:** Running `.\scripts\check-toolchain.ps1`, `.\scripts\install-toolchain.ps1`, `.\scripts\build-cli.ps1`, `.\scripts\build-app.ps1`, or `dotnet build` / `dotnet test` with no architecture flag continues to target `x64` (`win-x64` / `x86_64-unknown-windows-msvc`) and requires zero ARM64 tools installed locally.
- **Opt-In `arm64` Support Across Project & Scripts:**
  - `VRemindWin.csproj` and `VRemindWin.sln` support both `x64` (`win-x64`) and `ARM64` (`win-arm64`), defaulting to `x64` when unspecified.
  - `check-toolchain.ps1`, `install-toolchain.ps1`, `build-cli.ps1`, and `build-app.ps1` accept `-Architecture <x64|arm64>` (defaulting to `x64`).
- **Automated `windows-11-arm` CI Build & Artifact:** `.github/workflows/build-arm64.yml` runs on GitHub's native `windows-11-arm` runner (`workflow_dispatch`, pushes/PRs, and `v*` tags) to build `vremind-core.exe` (`aarch64-unknown-windows-msvc`), run `swift test` and `dotnet test`, and upload `VRemindWin-arm64.zip` so volunteers can test without installing 4 GB of SDKs.
- **Volunteer Callout & Testing Guide:** `README.md` invites Windows 11 ARM64 testers and links to `docs/WINDOWS-ARM64.md`, which covers both downloading the pre-built CI `.zip` and building/testing from source on ARM64.

### Non-Goals
- **Windows 10 on ARM Support:** Explicitly unsupported. Windows 10 on ARM is a dead platform; `win-arm64` targets **Windows 11 (21H2 / 22000 and newer, 24H2 recommended)** only, while `x64` continues supporting Windows 10 1809 LTSC (`17763`) through Windows 11.

### Anti-Goals
- **Breaking `x64` Devbox Builds:** Any change that causes `check-toolchain.ps1` to report missing prerequisites on the `x64` devbox or breaks default `build-app.ps1` is a failure.
- **Mixing `x64` and `arm64` DLLs:** `build-cli.ps1 -Architecture arm64` must never silently copy `x64` Swift runtime DLLs into an `arm64` output directory.

---

## 3. Constraints
- **Target OS Matrix:**
  - `x64`: Windows 10 version 1809 (build `17763`, Enterprise LTSC 2019)+ and Windows 11 (`x64`).
  - `arm64`: **Windows 11 (`arm64`) only** (Snapdragon X, NVIDIA RTX Spark, Apple Silicon VMs).
- **Metered Connection Safety:** `check-toolchain.ps1` must remain 100% offline (`0 MB`), and `install-toolchain.ps1` must preserve `-ConfirmDownload`.

---

## 4. Approaches Considered

1. **Source-Build Guide + Opt-In Scripts Only:** Update `.csproj`, `.sln`, and `scripts/*.ps1` for `-Architecture arm64` and document how volunteers can install toolchains and compile locally. *Trade-off:* Zero CI config, but high friction for volunteers who just want to test the app on their Snapdragon X / VM.
2. **Opt-In `arm64` Scripts + GitHub Actions `windows-11-arm` Pre-built `.zip` + Volunteer Guide *(Selected)*:** Same non-breaking project/script updates and guide, plus a native `windows-11-arm` GitHub Actions workflow that builds, tests, and uploads `VRemindWin-arm64.zip`. *Why chosen:* Volunteers can either grab a pre-built `.zip` in seconds or compile from source.

---

## 5. Design & Engineering Calls

- **Project & Solution Configuration *(Builder call)*:**
  - `VRemindWin.csproj`: Set `<Platforms>x64;ARM64</Platforms>` and `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>`, plus `<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == '' and '$(Platform)' == 'ARM64'">win-arm64</RuntimeIdentifier>` and `<RuntimeIdentifier Condition="'$(RuntimeIdentifier)' == ''">win-x64</RuntimeIdentifier>` so existing commands without `-r` keep resolving `win-x64`.
  - `VRemindWin.sln`: Add `Debug|ARM64` and `Release|ARM64` solution configurations.
- **Script Parameterization (`-Architecture x64|arm64`, default `'x64'`) *(Builder call)*:**
  - `check-toolchain.ps1` & `install-toolchain.ps1`: When `-Architecture arm64` is passed, check for `Microsoft.VisualStudio.Component.VC.Tools.ARM64` and use the ARM64 Swift (`swift-6.0.3-RELEASE-windows10-arm64.exe` on ARM64 hosts) and `.NET 9` installers when running on an ARM64 OS. Default (`x64`) output and JSON contract remain unchanged.
  - `build-cli.ps1`: When `-Architecture arm64` is passed, select `vcvarsarm64.bat` (on native ARM64 host) or `vcvarsamd64_arm64.bat` (cross-compiling on x64), pass `--triple aarch64-unknown-windows-msvc` to `swift build`, read `.build\aarch64-unknown-windows-msvc\<config>\vremind-core.exe`, and verify the host/runtime Swift DLL architecture before staging.
  - `build-app.ps1`: Accept `-Architecture <x64|arm64>` (default `'x64'`). To preserve backward compatibility on the devbox, keep default `$OutputDir` as `dist\VRemindWin` when `-Architecture x64` is used and `dist\VRemindWin-arm64` when `-Architecture arm64` is used.
- **GitHub Actions Workflow (`.github/workflows/windows-arm64.yml`) *(Builder call)*:**
  - Runs on `windows-11-arm` on `workflow_dispatch`, pushes to `main`, PRs, and `v*` tags.
  - Installs Swift `6.0.3` for Windows ARM64 and `.NET 9 SDK`, runs `swift test` and `dotnet test`, runs `.\scripts\build-app.ps1 -Architecture arm64`, packages `VRemindWin-arm64.zip`, and uploads it as a workflow artifact.
- **Documentation (`README.md` & `docs/WINDOWS-ARM64.md`) *(User + Builder call)*:**
  - `README.md`: Add a **Windows 11 on ARM64 (Volunteers Wanted!)** section and update Requirements/Installation to clarify that `x64` supports Windows 10 1809+ & Windows 11 while `ARM64` targets Windows 11 only.
  - `docs/WINDOWS-ARM64.md`: Step-by-step volunteer checklist covering Path A (downloading `VRemindWin-arm64.zip` from GitHub Actions / Releases) and Path B (building & running `swift test` / `dotnet test` natively on Windows 11 ARM64), plus what to verify in Task Manager (`ARM64` architecture column for both `VRemindWin.exe` and `vremind-core.exe`), tray flyout, toasts, DirectX 10 confetti, and video playback.
