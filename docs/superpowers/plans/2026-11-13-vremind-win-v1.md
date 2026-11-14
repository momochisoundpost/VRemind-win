# VRemind for Windows (VRemind-win) v1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the v1 native Windows system tray utility (`VRemind-win`) with a WinUI 3 `360×440` popover, minimal Settings window, and 1809-compatible clickable Windows toast notifications powered by a headless Swift `VRemindCore` companion CLI (`vremind-core.exe`), starting with a mobile-data-safe toolchain verification and bootstrap step.

**Architecture:** A thin Swift executable (`VRemindCoreCLI` -> `vremind-core.exe`) links directly against `../VRemindCore`, persists `channels.json`, `settings.json`, and `statuses.json` in `%APPDATA%\VRemind\`, and exposes deterministic JSON subcommands (`state`, `refresh`, `add`, `remove`, `settings`). An unpackaged C# `.NET 9` WinUI 3 application (`VRemindWin`) runs as a single-instance tray process, invoking `vremind-core.exe` asynchronously on a 180-second timer and on user actions, rendering the taskbar-anchored flyout UI, and dispatching clickable WinRT/COM toast notifications compatible with Windows 10 version 1809 (`10.0.17763`, LTSC 2019) through Windows 11.

**Tech Stack:** Swift 5.9+/6.x (`VRemindCore`, `Foundation`, `FoundationNetworking`, `XCTest`), C# 13 / `.NET 9` (`net9.0-windows10.0.19041.0` with `TargetPlatformMinVersion=10.0.17763.0`), WinUI 3 (`Microsoft.WindowsAppSDK` unpackaged self-contained), Win32 P/Invoke (`Shell_NotifyIconW`, `IShellLinkW`, `IPropertyStore`, `CoRegisterClassObject`), xUnit.

**Spec:** [2026-11-13-vremind-win-v1-design.md](file:///C:/CaitDev/VRemind-win/docs/superpowers/specs/2026-11-13-vremind-win-v1-design.md)

## Global Constraints

- **Minimum Target Runtime OS:** Windows 10 version 1809 / build `10.0.17763.0` (Windows 10 Enterprise LTSC 2019) through Windows 11 (`<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>`).
- **Devbox / Contributor Baseline:** Windows 10 21H2 / build `10.0.19044.0` (Windows 10 IoT Enterprise LTSC 2021).
- **Metered Connection Safety (Task 1 Gate):** Never download SDKs or toolchains automatically without explicit user confirmation (`-ConfirmDownload`), because the devbox may be on metered mobile data.
- **Zero Scraper/Domain Duplication in C#:** All YouTube `/live` HTML parsing, Twitch GraphQL queries, handle/URL normalization, `SecretCheck` detection/demotion, and notification deduplication must run inside Swift `VRemindCore` via `vremind-core.exe`.
- **Unpackaged Deployment:** `<WindowsPackageType>None</WindowsPackageType>` and `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`; no MSIX signing or Microsoft Store dependency.
- **1809-Compatible Toast Pipeline:** Use `SetCurrentProcessExplicitAppUserModelID("MomochiSoundpost.VRemind")`, `HKCU\Software\Classes\CLSID\{D8B4E62A-5C39-4E8B-A193-7E4A6F90C112}\LocalServer32`, Start Menu `VRemind.lnk`, and `ToastNotificationManager.CreateToastNotifier("MomochiSoundpost.VRemind")` (never gate toasts on 2004+ `AppNotificationManager`).
- **Default Starter Channel Seed:** Seed `youtube:@bukancaitlyn` (`Caitlyn Momochi`) only when `channels.json` does not exist on disk; if the user deletes all channels so `channels.json` is `[]`, preserve the empty watchlist.

## Review Focus

1. **Deleting the last channel vs. first-run seeding:** When a user removes `@bukancaitlyn` leaving `0` channels, `channels.json` exists on disk as `[]`; subsequent `state` and `refresh` runs must return an empty watchlist (`0` channels) rather than re-seeding `@bukancaitlyn`. (Tested in Task 2)
2. **Corrupt or missing `statuses.json` on disk:** If `statuses.json` is missing, truncated, or invalid JSON, `vremind-core.exe` must fall back to an empty `[String: StreamStatus]` dictionary (`[:]`) and still complete `state` / `refresh` without throwing an unhandled error. (Tested in Task 2)
3. **Channel removed while a `refresh` pass is in flight:** If the user deletes a channel in the UI while `vremind-core.exe refresh` is mid-flight, `StreamMonitorCoordinator` must increment `statusEpoch` and reload fresh `state` (or discard stale removed channels) so the deleted channel neither reappears in `PopoverViewModel` nor overwrites `channels.json`. (Tested in Task 2 & Task 3)
4. **Stream titles or channel names containing XML special characters (`&`, `<`, `>`, `"`, `'`):** Windows Toast payloads are constructed as XML (`XmlDocument.LoadXml`); unescaped characters like `"Q&A <Live> Stream"` will throw an `XmlException` and silently drop the notification unless escaped via `SecurityElement.Escape`. (Tested in Task 4)
5. **Unsafe or malformed URLs in toast activation arguments:** Clicking a toast or invoking `VRemindWin.exe -ToastActivated` with `file://`, `javascript:`, `cmd:`, or malformed URLs must be rejected by `LinkSafetyValidator.TryValidateAllowedUri` before calling `Process.Start`. (Tested in Task 4)

---

### Task 1: Offline-First Toolchain Verifier & Data-Gated Bootstrap Scripts

**Files:**
- Create: `scripts/check-toolchain.ps1`
- Create: `scripts/install-toolchain.ps1`
- Test: `scripts/tests/verify-toolchain-scripts.ps1`

**Interfaces:**
- Consumes: Local Windows environment (`PATH`, registry, `Program Files`, `LOCALAPPDATA`)
- Produces:
  - `scripts/check-toolchain.ps1 [-Json]`: Zero-network check returning exit code `0` when all 4 prerequisites (`.NET 9 SDK`, `VS C++ Build Tools + Windows 10 SDK >= 10.0.17763.0`, `Swift for Windows >= 5.9`, `Git`) are present, or exit code `1` with a table of missing packages and estimated download sizes.
  - `scripts/install-toolchain.ps1 [-ConfirmDownload] [-SkipSwift] [-SkipDotNet] [-SkipVsBuildTools] [-SkipGit]`: Refuses to make any network request and exits with code `2` unless `-ConfirmDownload` is passed.

- [ ] **Step 1: Write the failing verification test script `scripts/tests/verify-toolchain-scripts.ps1`**

```powershell
$ErrorActionPreference = 'Stop'
$checkScript = Join-Path $PSScriptRoot '..\check-toolchain.ps1'
$installScript = Join-Path $PSScriptRoot '..\install-toolchain.ps1'

# 1. check-toolchain.ps1 -Json must output valid JSON with required keys and make zero network calls
$jsonRaw = & powershell -NoProfile -ExecutionPolicy Bypass -File $checkScript -Json
$report = $jsonRaw | ConvertFrom-Json
$requiredKeys = @('DotNetSdk9', 'VsCppAndWin10Sdk', 'SwiftWindows', 'Git', 'AllInstalled', 'EstimatedDownloadMB')
foreach ($k in $requiredKeys) {
    if ($null -eq $report.$k) { throw "Missing key $k in check-toolchain.ps1 output" }
}

# 2. install-toolchain.ps1 without -ConfirmDownload must refuse to download and exit with code 2
$output = & powershell -NoProfile -ExecutionPolicy Bypass -File $installScript 2>&1
if ($LASTEXITCODE -ne 2) {
    throw "Expected exit code 2 when -ConfirmDownload is omitted, got $LASTEXITCODE"
}
if (($output -join "`n") -notmatch 'ConfirmDownload') {
    throw "Expected metered-data warning mentioning -ConfirmDownload"
}
Write-Host "PASS: Toolchain scripts verified."
```

- [ ] **Step 2: Run the verification script to confirm it fails**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-toolchain-scripts.ps1`
Expected: FAIL with file not found (`check-toolchain.ps1`).

- [ ] **Step 3: Implement `scripts/check-toolchain.ps1` and `scripts/install-toolchain.ps1`**

In `scripts/check-toolchain.ps1`:
- Accept `[switch]$Json`. Perform **zero network requests**.
- Check `DotNetSdk9`: run `dotnet --list-sdks` if `dotnet` is on `PATH` (or `C:\Program Files\dotnet\dotnet.exe`) and match `^9\.`. Estimate: `215` MB if missing.
- Check `VsCppAndWin10Sdk`: check `vswhere.exe` (under `${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe`) for `VC.Tools.x86.x64` AND check `${env:ProgramFiles(x86)}\Windows Kits\10\Include` for any SDK version `>= 10.0.17763.0` (e.g. `10.0.17763.0`, `10.0.19041.0`, `10.0.22621.0`). Estimate: `2800` MB if missing.
- Check `SwiftWindows`: check `swift.exe` on `PATH` or `$env:LOCALAPPDATA\Programs\Swift\Toolchains\*\usr\bin\swift.exe` or `C:\Library\Developer\Toolchains\*\usr\bin\swift.exe`. Estimate: `720` MB if missing.
- Check `Git`: check `git.exe` on `PATH` or `C:\Program Files\Git\cmd\git.exe`. Estimate: `65` MB if missing.
- Compute `AllInstalled` (`$true` only if all 4 are present) and `EstimatedDownloadMB` (sum of missing items). If `-Json` is passed, print `ConvertTo-Json` and exit `0` if `AllInstalled` else `1`. Otherwise print a human-readable checklist with exact sizes and instructions to run `.\scripts\install-toolchain.ps1 -ConfirmDownload` when on Wi-Fi / unmetered data.

In `scripts/install-toolchain.ps1`:
- Accept `[switch]$ConfirmDownload`, `[switch]$SkipDotNet`, `[switch]$SkipVsBuildTools`, `[switch]$SkipSwift`, `[switch]$SkipGit`.
- If `-not $ConfirmDownload`, print: `"Metered connection guard: refusing to download missing packages (~$($report.EstimatedDownloadMB) MB). Re-run with -ConfirmDownload when ready."` and `exit 2`.
- When `-ConfirmDownload` is passed, download and install only the missing components into `$env:TEMP\vremind-toolchain`:
  - **Git (if missing & not skipped):** Download PortableGit or Git for Windows 64-bit silent installer (`/VERYSILENT /NORESTART`).
  - **.NET 9 SDK (if missing & not skipped):** Run official `dotnet-install.ps1 -Channel 9.0 -InstallDir "C:\Program Files\dotnet"` (or user-scoped `$env:LOCALAPPDATA\Microsoft\dotnet` if not elevated).
  - **VS 2022 Build Tools + Windows 10 SDK (if missing & not skipped):** Download `https://aka.ms/vs/17/release/vs_buildtools.exe` and invoke with `--quiet --wait --norestart --nocache --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows10SDK.19041`.
  - **Swift for Windows (if missing & not skipped):** Download the official Swift 6.0.3 Windows x64 installer (`https://download.swift.org/swift-6.0.3-release/windows10/swift-6.0.3-RELEASE/swift-6.0.3-RELEASE-windows10.exe`) and run `/quiet`.
- Re-run `scripts/check-toolchain.ps1` at the end to verify.

- [ ] **Step 4: Run `scripts/tests/verify-toolchain-scripts.ps1` and `scripts/check-toolchain.ps1`**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/tests/verify-toolchain-scripts.ps1`
Expected: `PASS: Toolchain scripts verified.`

- [ ] **Step 5: Pause and confirm with user before any downloads (when executing)**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check-toolchain.ps1`
Expected: Prints the exact status of the 4 prerequisites on this machine without downloading anything. *(During execution: if any prerequisite is missing, ask the user whether to install now or wait until they are off mobile data.)*

---

### Task 2: Headless Swift Companion CLI (`VRemindCoreCLI` / `vremind-core.exe`)

**Files:**
- Create: `VRemindCoreCLI/Package.swift`
- Create: `VRemindCoreCLI/Sources/VRemindCoreCLI/CliModels.swift`
- Create: `VRemindCoreCLI/Sources/VRemindCoreCLI/CliRunner.swift`
- Create: `VRemindCoreCLI/Sources/VRemindCoreCLI/main.swift`
- Create: `VRemindCoreCLI/Tests/VRemindCoreCLITests/CliRunnerTests.swift`
- Create: `scripts/build-cli.ps1`

**Interfaces:**
- Consumes: `VRemindCore` (`../VRemindCore` local Swift package: `WatchedChannel`, `StreamPlatform`, `StreamStatus`, `AppSettings`, `StreamMonitorEngine`, `StreamProviderRegistry`, `CelebrationDetector`, `SecretCheck`, `ElapsedTime`, `JSONFileChannelStorage`, `JSONFileSettingsStorage`)
- Produces:
  - Swift library target `VRemindCoreCLIKit` + executable target `vremind-core` (`VRemindCoreCLI/Package.swift`)
  - `public struct CliStateEnvelope: Codable, Equatable, Sendable`:
    ```swift
    public struct CliChannelItem: Codable, Equatable, Sendable {
        public let id: String
        public let platform: String          // "youtube" | "twitch"
        public let platformDisplayName: String // "YouTube" | "Twitch"
        public let name: String
        public let handle: String
        public let formattedHandle: String   // "@bukancaitlyn" | "ironmouse"
        public let avatarURL: String?
        public let statusKind: String        // "live" | "upcoming" | "offline" | "unknown"
        public let streamTitle: String?
        public let timeLabel: String         // "42m" | "in 25m" | "Last streamed 2d ago" | "Offline"
        public let watchURL: String
    }
    public struct CliSections: Codable, Equatable, Sendable {
        public let live: [CliChannelItem]
        public let upcoming: [CliChannelItem]
        public let offline: [CliChannelItem]
    }
    public struct CliNotificationItem: Codable, Equatable, Sendable {
        public let kind: String              // "live" | "upcoming" | "developerBirthday"
        public let channelName: String?
        public let title: String
        public let body: String
        public let targetURL: String
        public let playSound: Bool
    }
    public struct CliStateEnvelope: Codable, Equatable, Sendable {
        public let liveCount: Int
        public let sections: CliSections
        public let settings: AppSettings
        public let notifications: [CliNotificationItem]
    }
    public struct CliCommandResponse: Codable, Equatable, Sendable {
        public let outcome: String           // "ok" | "added" | "removed" | "updated" | "validation_error" | "secret_prompt"
        public let errorMessage: String?
        public let secretDialog: SecretDialogPayload?
        public let state: CliStateEnvelope?
    }
    ```
  - `public final class CliRunner: Sendable` with `public func execute(arguments: [String], fetcher: @escaping HTTPFetcher = StreamMonitorEngine.defaultFetcher, now: Date = Date(), calendar: Calendar = .current) async throws -> CliCommandResponse`

- [ ] **Step 1: Write the failing Swift tests in `VRemindCoreCLI/Tests/VRemindCoreCLITests/CliRunnerTests.swift`**

Create `VRemindCoreCLI/Package.swift` (referencing `.package(path: "../VRemindCore")`) and `CliRunnerTests.swift` testing:
1. `testFirstRunSeedsDefaultCaitlynChannelWhenFileMissing()`: Running `["state", "--data-dir", tempDir.path]` when `channels.json` does not exist creates `channels.json` with `youtube:@bukancaitlyn` (`Caitlyn Momochi`) and returns `sections.offline.count == 1`.
2. `testRemovingLastChannelPreservesEmptyWatchlistWithoutReseeding()` *(Review Focus #1)*: Running `["remove", "--id", "youtube:@bukancaitlyn", "--data-dir", tempDir.path]` followed by `["state", "--data-dir", tempDir.path]` returns `0` channels across all sections (`channels.json` is `[]`, not re-seeded).
3. `testCorruptStatusesJsonFallsBackGracefully()` *(Review Focus #2)*: Writing `"{not valid json"` to `statuses.json` and running `["state", "--data-dir", tempDir.path]` succeeds with `outcome == "ok"` and statuses defaulted to offline/unknown.
4. `testAddValidationAndDuplicateAndSecretPrompt()`:
   - Invalid YouTube handle `"bad handle"` -> `outcome == "validation_error"`, `errorMessage == "Please enter a valid channel handle or YouTube URL"`.
   - Duplicate `"@bukancaitlyn"` -> `outcome == "validation_error"`, `errorMessage == "Channel is already in your watchlist"`.
   - Secret target without `--ignore-secret` -> `outcome == "secret_prompt"` with non-empty `secretDialog.title`, and channel is not saved; with `--ignore-secret` -> `outcome == "added"` and channel is demoted to the bottom of its section.
5. `testRefreshPassEmitsNotificationsUpdatesMetadataAndPreservesMidRefreshDeletion()` *(Review Focus #3)*: Mock `HTTPFetcher` returning a live YouTube stream emits `notifications` with `kind == "live"`, `body == "🔴 LIVE: Test Title"`, `targetURL == "https://www.youtube.com/watch?v=vid123"`, updates `lastNotifiedVideoId`, and a second `refresh` pass with the same `vid123` emits `0` notifications. Also verify that if a channel is removed from `channels.json` on disk while `fetcher` is awaiting, `refresh` re-reads `channels.json` before saving so the removed channel is not written back to `channels.json`.

- [ ] **Step 2: Run `swift test` in `VRemindCoreCLI` to verify it fails**

Run: `swift test --package-path VRemindCoreCLI`
Expected: FAIL to compile (`CliRunner` and `CliModels` not defined).

- [ ] **Step 3: Implement `CliModels.swift`, `CliRunner.swift`, `main.swift`, and `scripts/build-cli.ps1`**

In `VRemindCoreCLI/Sources/VRemindCoreCLI/CliRunner.swift`:
- Resolve `--data-dir <path>` (defaulting to `ProcessInfo.processInfo.environment["APPDATA"]` + `"/VRemind"`).
- Seed `[WatchedChannel(id: "youtube:@bukancaitlyn", platform: .youtube, name: "Caitlyn Momochi", handle: "@bukancaitlyn")]` only when `!FileManager.default.fileExists(atPath: channelsURL.path)`.
- Load `statuses.json` with `try? JSONDecoder().decode([String: StreamStatus].self, from: data)` falling back to `[:]`.
- In `refresh`: before writing updated `channels` back to `channels.json`, re-load current `channels.json` from disk and filter out any channel IDs that were deleted while the network pass was in flight (`Review Focus #3`). When a channel transitions from `.live` in `currentStatuses` to `.offline` in the new pass, stamp `lastStreamedAt = now`.
- In `main.swift`: invoke `CliRunner().execute(arguments: Array(CommandLine.arguments.dropFirst()))`, encode `CliCommandResponse` with `JSONEncoder` (`.sortedKeys`, `.iso8601`), and write UTF-8 bytes to `FileHandle.standardOutput`.
- In `scripts/build-cli.ps1`: run `swift build -c release --package-path VRemindCoreCLI` and copy `vremind-core.exe` plus required Swift Windows runtime DLLs (`swiftCore.dll`, `Foundation.dll`, `FoundationNetworking.dll`, `BlocksRuntime.dll`, `dispatch.dll`, and adjacent runtime DLLs from the Swift toolchain's runtime bin directory) into `artifacts/cli/`.

- [ ] **Step 4: Run `swift test` in `VRemindCoreCLI` to verify all tests pass**

Run: `swift test --package-path VRemindCoreCLI`
Expected: PASS (all `CliRunnerTests` green).

- [ ] **Step 5: Commit Task 1 & Task 2**

```bash
git add scripts/ VRemindCoreCLI/ docs/
git commit -m "feat(cli): add toolchain scripts and VRemindCoreCLI Swift companion executable"
```

---

### Task 3: C# Core Bridge, Single-Flight Coordinator, Startup Registry & ViewModels

**Files:**
- Create: `VRemindWin/VRemindWin.sln`
- Create: `VRemindWin/src/VRemindWin/VRemindWin.csproj`
- Create: `VRemindWin/src/VRemindWin/Models/CliContracts.cs`
- Create: `VRemindWin/src/VRemindWin/Services/ICoreCliBridge.cs`
- Create: `VRemindWin/src/VRemindWin/Services/CoreCliBridge.cs`
- Create: `VRemindWin/src/VRemindWin/Services/StreamMonitorCoordinator.cs`
- Create: `VRemindWin/src/VRemindWin/Services/StartupRegistryService.cs`
- Create: `VRemindWin/src/VRemindWin/ViewModels/PopoverViewModel.cs`
- Create: `VRemindWin/src/VRemindWin/ViewModels/SettingsViewModel.cs`
- Test: `VRemindWin/tests/VRemindWin.Tests/VRemindWin.Tests.csproj`
- Test: `VRemindWin/tests/VRemindWin.Tests/CoordinatorAndViewModelTests.cs`

**Interfaces:**
- Consumes: `vremind-core.exe` JSON schema from Task 2 (`CliCommandResponse`, `CliStateEnvelope`, `CliChannelItem`, `CliNotificationItem`, `SecretDialogPayload`)
- Produces:
  - `public interface ICoreCliBridge`:
    - `Task<CliCommandResponse> GetStateAsync(CancellationToken ct = default)`
    - `Task<CliCommandResponse> RefreshAsync(CancellationToken ct = default)`
    - `Task<CliCommandResponse> AddChannelAsync(string platform, string input, bool ignoreSecret = false, CancellationToken ct = default)`
    - `Task<CliCommandResponse> RemoveChannelAsync(string channelId, CancellationToken ct = default)`
    - `Task<CliCommandResponse> UpdateSettingsAsync(AppSettingsDto settings, CancellationToken ct = default)`
  - `public interface IToastNotificationDispatcher`:
    - `void Dispatch(CliNotificationItem item)`
  - `public sealed class StreamMonitorCoordinator : INotifyPropertyChanged, IDisposable`:
    - `CliStateEnvelope CurrentState { get; }`
    - `bool IsRefreshing { get; }`
    - `bool IsPausedForSleep { get; }`
    - `Task InitializeAsync(CancellationToken ct = default)`
    - `Task RefreshAllAsync(CancellationToken ct = default)`
    - `Task<CliCommandResponse> AddChannelAsync(string platform, string input, bool ignoreSecret = false, CancellationToken ct = default)`
    - `Task RemoveChannelAsync(string channelId, CancellationToken ct = default)`
    - `Task UpdateSettingsAsync(AppSettingsDto settings, CancellationToken ct = default)`
    - `void HandlePowerOrSessionPause()` / `Task HandlePowerOrSessionResumeAsync()`
  - `public sealed class PopoverViewModel : INotifyPropertyChanged`
  - `public sealed class SettingsViewModel : INotifyPropertyChanged`
  - `public sealed class StartupRegistryService` (`void SetLaunchAtLogin(bool enabled, string exePath)`, `bool IsLaunchAtLoginEnabled()`)

- [ ] **Step 1: Write the failing xUnit tests in `VRemindWin/tests/VRemindWin.Tests/CoordinatorAndViewModelTests.cs`**

Test cases:
1. `RefreshAllAsync_CoalescesConcurrentCallsIntoSingleFollowUpPass()`: Calling `RefreshAllAsync()` twice while the first `ICoreCliBridge.RefreshAsync()` is awaiting runs `RefreshAsync` exactly twice sequentially and dispatches returned `CliNotificationItem` entries via `IToastNotificationDispatcher`.
2. `RemoveChannelAsync_DuringInFlightRefresh_IncrementsEpochAndReloadsCleanState()` *(Review Focus #3)*: Starting `RefreshAllAsync()`, calling `RemoveChannelAsync("youtube:@bukancaitlyn")` while refresh is still awaiting, and then completing the stale refresh must discard the stale refresh's channel list and fetch fresh `GetStateAsync()` so the removed channel does not reappear in `CurrentState`.
3. `PopoverViewModel_AddChannel_HandlesValidationSecretPromptAndSuccess()`:
   - Empty/whitespace input does not call `AddChannelAsync`.
   - `outcome == "validation_error"` sets `ValidationError` to `errorMessage` and keeps `NewHandleInput`.
   - `outcome == "secret_prompt"` invokes `Func<SecretDialogPayload, Task<string>> SecretPromptPresenter`; returning `"abort"` or `"cancel"` stops without adding; returning `"ignore"` calls `AddChannelAsync(..., ignoreSecret: true)`, clears `NewHandleInput`, and triggers `RefreshAllAsync()`.
4. `PowerPauseAndResume_StopsPollingWhileAsleepAndRefreshesOnWake()`: `HandlePowerOrSessionPause()` sets `IsPausedForSleep = true`; `HandlePowerOrSessionResumeAsync()` sets `IsPausedForSleep = false` and triggers `RefreshAllAsync()`.

- [ ] **Step 2: Run `dotnet test` to verify the tests fail**

Run: `dotnet test VRemindWin/tests/VRemindWin.Tests/VRemindWin.Tests.csproj`
Expected: FAIL to compile (types not implemented yet).

- [ ] **Step 3: Implement `CliContracts.cs`, `CoreCliBridge.cs`, `StreamMonitorCoordinator.cs`, `StartupRegistryService.cs`, and ViewModels**

- In `CoreCliBridge.cs`: execute `vremind-core.exe` with `ProcessStartInfo { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 }`, using `ArgumentList` (never string concatenation, so handles/inputs with quotes or spaces cannot inject arguments) and Deserialize with `System.Text.Json`.
- In `StreamMonitorCoordinator.cs`: implement `statusEpoch` counter and `needsAnotherRefresh` single-flight loop matching Mac `StreamMonitorService`. If `statusEpoch` changed while `RefreshAsync` was running, call `GetStateAsync` to reconcile before updating `CurrentState`, while still dispatching any valid `notifications` for channels that remain in the watchlist.
- In `StartupRegistryService.cs`: read/write `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value `"VRemind"` = `"\"<exePath>\""`.

- [ ] **Step 4: Run `dotnet test` to verify all tests pass**

Run: `dotnet test VRemindWin/tests/VRemindWin.Tests/VRemindWin.Tests.csproj`
Expected: PASS (all `CoordinatorAndViewModelTests` green).

- [ ] **Step 5: Commit Task 3**

```bash
git add VRemindWin/
git commit -m "feat(core-win): add C# CLI bridge, single-flight coordinator, and view models"
```

---

### Task 4: 1809-Compatible Unpackaged Windows Toast Notifications & Single-Instance Activation

**Files:**
- Create: `VRemindWin/src/VRemindWin/Services/LinkSafetyValidator.cs`
- Create: `VRemindWin/src/VRemindWin/Services/WindowsToastService.cs`
- Create: `VRemindWin/src/VRemindWin/Services/SingleInstanceService.cs`
- Test: `VRemindWin/tests/VRemindWin.Tests/ToastAndLinkSafetyTests.cs`

**Interfaces:**
- Consumes: `CliNotificationItem` from Task 3
- Produces:
  - `public static class LinkSafetyValidator`:
    - `public static bool TryValidateAllowedUri(string? rawUrl, out Uri? validatedUri)` (allows only `http://`, `https://`, and `mailto:`)
    - `public static bool TryExtractTargetUrlFromToastArgs(string? toastArgs, out Uri? targetUri)` (parses `action=openUrl&url=<escaped>`)
  - `public sealed class WindowsToastService : IToastNotificationDispatcher`:
    - `public const string AppUserModelId = "MomochiSoundpost.VRemind";`
    - `public const string ToastActivatorClsid = "D8B4E62A-5C39-4E8B-A193-7E4A6F90C112";`
    - `public static string BuildToastXml(CliNotificationItem item)`
    - `public void EnsureRegistered(string exePath, Action<Uri> onUrlActivated)`
    - `public void Dispatch(CliNotificationItem item)`

- [ ] **Step 1: Write the failing xUnit tests in `VRemindWin/tests/VRemindWin.Tests/ToastAndLinkSafetyTests.cs`**

Test cases:
1. `BuildToastXml_EscapesSpecialXmlCharactersAndSetsSilentAudioFlag()` *(Review Focus #4)*:
   - Passing a `CliNotificationItem` with `channelName = "Caitlyn & Friends <Live>"`, `body = "🔴 LIVE: Rock & Roll > Everything \"Now\""`, `targetURL = "https://www.youtube.com/watch?v=abc&feature=live"`, `playSound = false` produces XML that loads cleanly into `System.Xml.XmlDocument.LoadXml` without throwing, contains `<audio silent="true" />`, and round-trips `targetURL` via `LinkSafetyValidator.TryExtractTargetUrlFromToastArgs`.
2. `TryExtractTargetUrlFromToastArgs_BlocksUnsafeSchemesAndAllowsHttpHttpsMailto()` *(Review Focus #5)*:
   - `"action=openUrl&url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D123"` -> `true`.
   - `"action=openUrl&url=mailto%3Ay2karyl%40disroot.org%3Fsubject%3DHappy%2520Birthday%2520Caitlyn!"` -> `true`.
   - `"action=openUrl&url=file%3A%2F%2F%2FC%3A%2FWindows%2FSystem32%2Fcmd.exe"` -> `false`.
   - `"action=openUrl&url=javascript%3Aalert(1)"` -> `false`.
   - `""` or malformed query -> `false`.

- [ ] **Step 2: Run `dotnet test` to verify the new tests fail**

Run: `dotnet test VRemindWin/tests/VRemindWin.Tests/VRemindWin.Tests.csproj --filter ToastAndLinkSafetyTests`
Expected: FAIL to compile (`LinkSafetyValidator` and `WindowsToastService` not defined).

- [ ] **Step 3: Implement `LinkSafetyValidator.cs`, `WindowsToastService.cs`, and `SingleInstanceService.cs`**

- In `LinkSafetyValidator.cs`: parse query key `url`, `Uri.UnescapeDataString`, validate `Uri.TryCreate(..., UriKind.Absolute, out var uri)` and require `uri.Scheme` (case-insensitive) in `{"http", "https", "mailto"}` with non-empty host (for `http`/`https`).
- In `WindowsToastService.cs`:
  - `BuildToastXml(CliNotificationItem item)`: escape `launch` attribute, `title`, and `body` using `System.Security.SecurityElement.Escape(...)`. Include `<audio silent="true" />` when `!item.PlaySound`.
  - `EnsureRegistered(string exePath, Action<Uri> onUrlActivated)`:
    1. P/Invoke `SetCurrentProcessExplicitAppUserModelID(AppUserModelId)`.
    2. Write `HKCU\Software\Classes\CLSID\{D8B4E62A-5C39-4E8B-A193-7E4A6F90C112}\LocalServer32` default value = `"\"<exePath>\" -ToastActivated"`.
    3. Create/update `%APPDATA%\Microsoft\Windows\Start Menu\Programs\VRemind.lnk` via COM `IShellLinkW` + `IPropertyStore` (`PKEY_AppUserModel_ID` = `MomochiSoundpost.VRemind`, `PKEY_AppUserModel_ToastActivatorCLSID` = `Guid.Parse(ToastActivatorClsid)`).
    4. Call `CoRegisterClassObject` for `[ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface INotificationActivationCallback` so clicking a toast invokes `onUrlActivated` with the validated `Uri`.
  - `Dispatch(CliNotificationItem item)`: load XML via `Windows.Data.Xml.Dom.XmlDocument.LoadXml(BuildToastXml(item))` and call `ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(new ToastNotification(xmlDoc))`.
- In `SingleInstanceService.cs`: acquire `new Mutex(true, @"Local\MomochiSoundpost.VRemind.SingleInstance", out bool isFirstInstance)` and handles `-ToastActivated` secondary invocations by registering the COM class factory briefly to process the toast click callback before exiting.

- [ ] **Step 4: Run `dotnet test` to verify all tests pass**

Run: `dotnet test VRemindWin/tests/VRemindWin.Tests/VRemindWin.Tests.csproj`
Expected: PASS (all unit tests in `VRemindWin.Tests` green).

- [ ] **Step 5: Commit Task 4**

```bash
git add VRemindWin/
git commit -m "feat(notifications): add 1809-compatible COM toast service and URL safety validator"
```

---

### Task 5: Win32 Tray Icon, `360×440` Popover Window, Minimal Settings Window & Packaging

**Files:**
- Create: `scripts/generate-icons.ps1`
- Create: `VRemindWin/src/VRemindWin/Assets/tray-normal.ico`
- Create: `VRemindWin/src/VRemindWin/Assets/tray-live.ico`
- Create: `VRemindWin/src/VRemindWin/Assets/app-icon.ico`
- Create: `VRemindWin/src/VRemindWin/Services/TrayIconService.cs`
- Create: `VRemindWin/src/VRemindWin/Views/PopoverWindow.xaml`
- Create: `VRemindWin/src/VRemindWin/Views/PopoverWindow.xaml.cs`
- Create: `VRemindWin/src/VRemindWin/Views/SettingsWindow.xaml`
- Create: `VRemindWin/src/VRemindWin/Views/SettingsWindow.xaml.cs`
- Create: `VRemindWin/src/VRemindWin/App.xaml`
- Create: `VRemindWin/src/VRemindWin/App.xaml.cs`
- Create: `VRemindWin/src/VRemindWin/Program.cs`
- Create: `scripts/build-app.ps1`
- Test: `VRemindWin/tests/VRemindWin.Tests/TrayPositioningTests.cs`

**Interfaces:**
- Consumes: `StreamMonitorCoordinator`, `PopoverViewModel`, `SettingsViewModel`, `StartupRegistryService`, `WindowsToastService`, `SingleInstanceService`
- Produces:
  - `public static class TrayFlyoutPositioner`:
    - `public static (int X, int Y, int Width, int Height) CalculateFlyoutBounds((int Left, int Top, int Right, int Bottom) iconRect, (int Left, int Top, int Right, int Bottom) workArea, uint dpi, int logicalWidth = 360, int logicalHeight = 440, int marginDip = 8)`
  - `TrayIconService`: Win32 hidden message window + `Shell_NotifyIconW` (`NIM_ADD`, `NIM_MODIFY`, `NIM_DELETE`), swaps between `tray-normal.ico` (`"VRemind"`) and `tray-live.ico` (`"VRemind (Live now)"`), left-click toggles `PopoverWindow`, right-click shows Win32 `CreatePopupMenu` (`Open VRemind`, `Refresh Now`, `Settings...`, `Quit VRemind`).
  - `PopoverWindow` (`360×440` DIP borderless transient flyout) & `SettingsWindow` (4 toggles).
  - `scripts/build-app.ps1`: builds `VRemindCoreCLI` + `VRemindWin` into `dist/VRemindWin/` as a self-contained unpackaged folder ready to run on Windows 10 1809 (`10.0.17763`) through Windows 11.

- [ ] **Step 1: Write the failing tray flyout positioning tests in `VRemindWin/tests/VRemindWin.Tests/TrayPositioningTests.cs`**

Test cases:
1. `CalculateFlyoutBounds_BottomTaskbar_CentersHorizontallyAndClampsToRightWorkAreaEdge()`:
   - Given `workArea = (0, 0, 1920, 1040)`, `iconRect = (1850, 1040, 1874, 1080)`, `dpi = 96` (100% scale): width is `360`, height is `440`, `X` is clamped to `1920 - 360 - 8 = 1552`, `Y` is `1040 - 440 - 8 = 592`.
2. `CalculateFlyoutBounds_ScalesDimensionsAt150PercentDpi()`:
   - Given `dpi = 144` (150% scale): physical width is `540`, physical height is `660`, margin is `12`.

- [ ] **Step 2: Run `dotnet test` to verify `TrayPositioningTests` fails**

Run: `dotnet test VRemindWin/tests/VRemindWin.Tests/VRemindWin.Tests.csproj --filter TrayPositioningTests`
Expected: FAIL (`TrayFlyoutPositioner` not defined).

- [ ] **Step 3: Implement `generate-icons.ps1`, `TrayIconService.cs`, `PopoverWindow`, `SettingsWindow`, `App.xaml.cs`, `Program.cs`, and `build-app.ps1`**

- Run `scripts/generate-icons.ps1` (using `System.Drawing` in PowerShell to convert `VRemind/Resources/Icons/app-icon.png` into `app-icon.ico` and draw crisp 16×16/32×32 antenna icons for `tray-normal.ico` and `tray-live.ico`).
- In `VRemindWin.csproj`: set `<OutputType>WinExe</OutputType>`, `<TargetFramework>net9.0-windows10.0.19041.0</TargetFramework>`, `<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>`, `<WindowsPackageType>None</WindowsPackageType>`, `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`, `<DefineConstants>DISABLE_XAML_GENERATED_MAIN</DefineConstants>`.
- In `PopoverWindow.xaml` / `.cs`:
  - Configure `OverlappedPresenter` (`SetBorderAndTitleBar(false, false)`, `IsResizable = false`, `IsMaximizable = false`, `IsMinimizable = false`, `IsAlwaysOnTop = true`) and `AppWindow.IsShownInSwitchers = false`.
  - Dismiss (`AppWindow.Hide()`) on `Activated` when `WindowActivationState == WindowActivationState.Deactivated` and `!_isModalDialogOpen`.
  - Bind header (`VRemind`, live count pill, Refresh button, Settings button, Quit button), `ScrollViewer` with `LIVE NOW`, `UPCOMING`, and `OFFLINE` sections, channel row click -> `Process.Start(new ProcessStartInfo(item.WatchURL) { UseShellExecute = true })`, hover delete button, and bottom Add Channel bar (platform `ComboBox`, handle `TextBox` with `KeyDown` Enter submit, `Add` button, red `ValidationError` `TextBlock`, and `ContentDialog` with `Abort`/`Cancel`/`Ignore` when `SecretCheck` triggers).
- In `SettingsWindow.xaml` / `.cs`:
  - 4 `CheckBox` controls bound to `SettingsViewModel` (`NotifyOnLive`, `NotifyOnUpcoming`, `PlayNotificationSound`, `LaunchAtLogin`), saving via `StreamMonitorCoordinator.UpdateSettingsAsync` and syncing `StartupRegistryService`.
- In `scripts/build-app.ps1`:
  - Invoke `scripts/build-cli.ps1`, then `dotnet publish VRemindWin/src/VRemindWin/VRemindWin.csproj -c Release -r win-x64 -o dist/VRemindWin`, and copy `artifacts/cli/*` into `dist/VRemindWin/`.

- [ ] **Step 4: Run `dotnet test` and `scripts/build-app.ps1` to verify the full build and test suite**

Run: `dotnet test VRemindWin/VRemindWin.sln && powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-app.ps1`
Expected: All xUnit tests PASS, and `dist/VRemindWin/VRemindWin.exe` + `dist/VRemindWin/vremind-core.exe` are produced cleanly.

- [ ] **Step 5: Commit Task 5**

```bash
git add scripts/ VRemindWin/
git commit -m "feat(ui): add WinUI 3 tray icon, 360x440 popover, settings window, and build pipeline"
```
