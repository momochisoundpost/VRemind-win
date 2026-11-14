# VRemind for Windows (`VRemind-win`) Complete Feature Parity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring `VRemind-win` to complete tray-app feature parity with macOS `VRemind` v1.3.1 by adding Celebration Mode (DirectX 10 hardware-accelerated confetti + rainbow gradients + Reduce Motion), a 3-tab Settings window (with cross-platform JSON Backup/Restore, Factory Reset, Update controls + Easter egg, Test Notification, and About tab), a First-Run Onboarding wizard, and a GitHub Software Updater with a native WinUI 3 Markdown & video release-notes viewer.

**Architecture:** `VRemindCoreCLI` (`vremind-core.exe`) is extended to expose celebration metadata (`isCelebration`, `celebrationType`, `isPopoverCelebrating`) and new subcommands (`complete-onboarding`, `reset`, `export-backup`, `validate-backup`, `restore-backup`, `check-update`) backed by `VRemindCore`. `VRemindWin` (`VRemindWin.Core` + `VRemindWin`) adds the corresponding C# ViewModels, a pure C# `MarkdownParser`, DirectX 10 vsync-driven `ConfettiOverlayControl` + rainbow brushes, Win32 `comdlg32.dll` file dialogs, and the native WinUI 3 `SettingsWindow`, `OnboardingWindow`, and `UpdateAvailableWindow`.

**Tech Stack:** Swift 5.9+/6.x (`VRemindCore`, `VRemindCoreCLI`, `XCTest`), C# 13 / `.NET 9` (`net9.0-windows10.0.19041.0` with `TargetPlatformMinVersion=10.0.17763.0`), WinUI 3 (`Microsoft.WindowsAppSDK` unpackaged), DirectX 10/11 Composition (`CompositionTarget.Rendering`, `CompositeTransform`), `MediaPlayerElement`, Win32 `comdlg32.dll` (`GetSaveFileNameW` / `GetOpenFileNameW`), xUnit.

**Spec:** [2026-11-14-vremind-win-feature-parity-design.md](file:///C:/CaitDev/VRemind-win/docs/superpowers/specs/2026-11-14-vremind-win-feature-parity-design.md)

## Global Constraints

- **Minimum Target Runtime OS:** Windows 10 version 1809 / build `10.0.17763.0` (Windows 10 Enterprise LTSC 2019) through Windows 11 (`<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>`).
- **Zero `WebView2` Dependency:** Windows 10 1809 LTSC does not pre-install Edge WebView2 Runtime. All Markdown and video rendering in `UpdateAvailableWindow` must use native WinUI 3 XAML controls and `MediaPlayerElement`.
- **DirectX 10 (`D3D_FEATURE_LEVEL_10_0`) Hardware-Accelerated Confetti:** `ConfettiOverlayControl` must drive particle movement via `CompositionTarget.Rendering` (locked to display vsync) and GPU-backed `CompositeTransform` (`TranslateX`, `TranslateY`, `Rotation`) without per-frame `Canvas.SetLeft`/`SetTop` CPU layout invalidations.
- **Zero Idle CPU/GPU Drain:** `ConfettiOverlayControl`, rainbow gradient animations, and `MediaPlayer` instances must halt and release resources immediately when their parent window hides or closes.
- **Cross-Platform Backup Schema (`BackupPayload` v1):** `vremind-core.exe` must encode/decode the exact ISO-8601 JSON backup schema (`schemaVersion: 1`, `appVersion: "1.3.1"`, `exportedAt`, `settings`, `channels`) used by macOS `VRemind` v1.3.1.
- **Exact Attribution & Fork Copy:**
  - Attribution: `"VRemind by Caitlyn Momochi, initially vibecoded for their Hackintosh video"`
  - Fork copy: `"You don't like what I did? Go fork yourself, it's free and open source."`
  - Easter egg alert title: `"Did you steal Caitlyn's computer?"`, primary button opens `https://www.youtube.com/watch?v=HKieGUH9pzg`.

## Review Focus

1. **Future-dated celebration stream vs. today's celebration stream:** If a channel has an `.upcoming` stream with a birthday/debut/anniversary title scheduled for *tomorrow*, `CliChannelItem.isCelebration` must be `true` (so the row badge gets rainbow styling), but `CliStateEnvelope.isPopoverCelebrating` must be `false` (so popover confetti does not rain until the day of the stream). (Tested in Task 1)
2. **Restoring a backup with stale `lastNotifiedVideoId` / `lastNotifiedUpcomingId` or `hasCompletedOnboarding == false`:** Restoring a backup via `restore-backup` must sanitize all channels (`lastNotifiedVideoId = nil`, `lastNotifiedUpcomingId = nil`), force `settings.hasCompletedOnboarding = true`, and clear `statuses.json` so restored live streams can notify cleanly and onboarding does not re-appear. (Tested in Task 1)
3. **Adding `@bukancaitlyn` on December 31st:** Calling `vremind-core.exe add --platform youtube --input "@bukancaitlyn"` on Dec 31 (`year > 1999`) when `lastNotifiedDevBirthdayYear != year` must immediately emit the `developerBirthday` notification in `CliStateEnvelope.notifications` and persist `lastNotifiedDevBirthdayYear = year`, and `StreamMonitorCoordinator.AddChannelAsync` must dispatch that toast. (Tested in Task 1 & Task 2)
4. **Multi-line Markdown blockquotes and `.webm` links vs `.mp4` links:** `MarkdownParser` must strip leading `>` across consecutive blockquote lines without leaking literal `>` characters, and must treat `.mp4`/`.mov`/`.m4v` links or `<video>` tags as `MarkdownBlock.Video` while keeping `.webm` links as clickable paragraph links. (Tested in Task 3)
5. **Private GitHub repo 404 during manual update check:** Because `momochisoundpost/VRemind-win` is currently private, clicking `Check for Updates Now` in `SettingsViewModel` when `check-update` returns HTTP 404/failed must dispatch a test Windows toast notification and invoke the preview release callback so the user can test the toast and `UpdateAvailableWindow` immediately. (Tested in Task 2)

---

### Task 1: `VRemindCoreCLI` Feature Parity Extensions (`vremind-core.exe`)

**Files:**
- Create: `VRemindCoreCLI/Sources/VRemindCoreCLIKit/BackupPayload.swift`
- Modify: `VRemindCoreCLI/Sources/VRemindCoreCLIKit/CliModels.swift`
- Modify: `VRemindCoreCLI/Sources/VRemindCoreCLIKit/CliRunner.swift`
- Test: `VRemindCoreCLI/Tests/VRemindCoreCLITests/CliRunnerTests.swift`

**Interfaces:**
- Consumes: `VRemindCore` (`CelebrationDetector`, `GitHubRelease`, `SemanticVersion`, `AppSettings`, `WatchedChannel`, `StreamMonitorEngine`)
- Produces:
  - `BackupPayload` (`schemaVersion = 1`, `appVersion = "1.3.1"`, `exportedAt`, `settings`, `channels`, `encodedJSONData()`, `decodeAndValidate(from:)`, `sanitizedForRestore()`, `defaultBackupFilename(for:)`)
  - `CliChannelItem`: adds `public let isCelebration: Bool` and `public let celebrationType: String?`
  - `CliStateEnvelope`: adds `public let isPopoverCelebrating: Bool` and `public let appVersion: String`
  - `CliReleaseDto(tagName: String, name: String?, body: String?, htmlURL: String, downloadURL: String)`
  - `CliUpdateCheckResult(status: String, latestVersion: String?, remoteVersion: String?, errorMessage: String?, httpStatusCode: Int?, release: CliReleaseDto?)`
  - `CliCommandResponse`: adds `public let updateResult: CliUpdateCheckResult?` and `public let backupChannelCount: Int?`
  - `CliRunner` subcommands: `complete-onboarding`, `reset`, `export-backup`, `validate-backup`, `restore-backup`, `check-update`, plus extended `settings` flags (`--reduce-motion`, `--update-consent`, `--ignored-update-tag`, `--has-completed-onboarding`) and Dec 31 `add` birthday notification trigger.

- [x] **Step 1: Write failing tests in `VRemindCoreCLI/Tests/VRemindCoreCLITests/CliRunnerTests.swift`**

Add tests in `CliRunnerTests.swift` covering:
1. `testCelebrationMetadataAndFutureUpcomingSuppression()`:
   - Live stream with `"Birthday Stream!"` -> `item.isCelebration == true`, `item.celebrationType == "birthday"`, `envelope.isPopoverCelebrating == true`.
   - Upcoming stream with `"3D Debut"` scheduled 2 days in the future -> `item.isCelebration == true`, `item.celebrationType == "debut"`, `envelope.isPopoverCelebrating == false`.
   - Upcoming stream with `"3D Debut"` scheduled later today -> `envelope.isPopoverCelebrating == true`.
2. `testAddCaitlynOnDec31TriggersBirthdayNotificationOnce()`:
   - On `2026-12-31T12:00:00Z`, removing `@bukancaitlyn` then running `add --platform youtube --input @bukancaitlyn` returns `state.notifications.count == 1` with `kind == "developerBirthday"` and sets `state.settings.lastNotifiedDevBirthdayYear == 2026`.
3. `testExtendedSettingsAndCompleteOnboardingAndFactoryReset()`:
   - `complete-onboarding --platform twitch --input ironmouse --update-consent allowed` replaces the sole default `@bukancaitlyn` channel with `twitch:ironmouse`, sets `hasCompletedOnboarding == true` and `updateConsent == .allowed`.
   - `settings --reduce-motion true --ignored-update-tag v1.4.0` persists both fields.
   - `reset` restores `youtube:@bukancaitlyn`, sets `reduceMotion == false`, and keeps `hasCompletedOnboarding == true`.
4. `testBackupExportValidateAndSanitizedRestore()`:
   - `export-backup --file <backupPath>` writes valid schema v1 JSON.
   - `validate-backup --file <backupPath>` returns `outcome == "valid_backup"` and `backupChannelCount`.
   - `restore-backup --file <backupPath>` overwrites channels/settings, clears `lastNotifiedVideoId` / `lastNotifiedUpcomingId`, and sets `hasCompletedOnboarding == true`.
   - Corrupt JSON returns `outcome == "backup_error"`.
5. `testCheckUpdateOutcomes()`:
   - Remote `v1.4.0` > local `1.3.1` returns `updateResult.status == "updateAvailable"` with `downloadURL` preferring `.zip` asset.
   - Remote `v1.2.0` < local `1.3.1` returns `updateResult.status == "localVersionNewer"`.
   - HTTP 404 returns `updateResult.status == "failed"` with `httpStatusCode == 404`.

- [x] **Step 2: Run `swift test` to verify the new tests fail**

Run: `swift test --package-path VRemindCoreCLI`
Expected: FAIL to compile (`isCelebration`, `isPopoverCelebrating`, `BackupPayload`, `updateResult` not defined).

- [x] **Step 3: Implement `BackupPayload.swift`, `CliModels.swift`, and `CliRunner.swift`, then rebuild `vremind-core.exe`**

1. Create `VRemindCoreCLI/Sources/VRemindCoreCLIKit/BackupPayload.swift` matching `VRemind/Sources/VRemindKit/Models/BackupPayload.swift` (`currentSchemaVersion = 1`, `defaultAppVersion = "1.3.1"`).
2. Update `VRemindCoreCLI/Sources/VRemindCoreCLIKit/CliModels.swift` with `isCelebration`, `celebrationType`, `isPopoverCelebrating`, `appVersion`, `CliReleaseDto`, `CliUpdateCheckResult`, and `backupChannelCount`.
3. Update `VRemindCoreCLI/Sources/VRemindCoreCLIKit/CliRunner.swift`:
   - Compute `CelebrationDetector.detect(title:)` and `CelebrationDetector.isDeveloperBirthday(channel:date:calendar:)` in `makeItem` and `buildEnvelope`.
   - In `runAdd`: check `CelebrationDetector.isDeveloperBirthday(channel: newChannel, date: now, calendar: calendar)` and emit `developerBirthday` notification if `settings.lastNotifiedDevBirthdayYear != year`.
   - Extend `runSettings` with `reduce-motion`, `update-consent`, `ignored-update-tag`, and `has-completed-onboarding`.
   - Implement `runCompleteOnboarding`, `runReset`, `runExportBackup`, `runValidateBackup`, `runRestoreBackup`, and `runCheckUpdate` (accepting `--manual` as a flag in `parseOptions`).

- [x] **Step 4: Run `swift test` and `scripts/build-cli.ps1` to verify**

Run:
```powershell
swift test --package-path VRemindCoreCLI
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-cli.ps1
```
Expected: All Swift tests PASS and `artifacts/cli/vremind-core.exe` builds cleanly.

---

### Task 2: C# CLI Contracts, Bridge, Coordinator, `OnboardingViewModel` & Expanded `SettingsViewModel`

**Files:**
- Modify: `VRemindWin/src/VRemindWin.Core/Models/CliContracts.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/Services/ICoreCliBridge.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/Services/CoreCliBridge.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/Services/StreamMonitorCoordinator.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/ViewModels/PopoverViewModel.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/ViewModels/SettingsViewModel.cs`
- Create: `VRemindWin/src/VRemindWin.Core/ViewModels/OnboardingViewModel.cs`
- Test: `VRemindWin/tests/VRemindWin.Tests/CoordinatorAndViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 JSON schema from `vremind-core.exe`
- Produces:
  - `CliChannelItem`: adds `bool IsCelebration = false`, `string? CelebrationType = null`
  - `CliStateEnvelope`: adds `bool IsPopoverCelebrating = false`, `string AppVersion = "1.3.1"`
  - `CliReleaseDto(string TagName, string? Name, string? Body, string HtmlURL, string DownloadURL)`
  - `CliUpdateCheckResult(string Status, string? LatestVersion, string? RemoteVersion, string? ErrorMessage, int? HttpStatusCode, CliReleaseDto? Release)`
  - `CliCommandResponse`: adds `CliUpdateCheckResult? UpdateResult = null`, `int? BackupChannelCount = null`
  - `ICoreCliBridge` / `CoreCliBridge`: adds `CompleteOnboardingAsync`, `ResetFactoryDefaultsAsync`, `ExportBackupAsync`, `ValidateBackupAsync`, `RestoreBackupAsync`, `CheckForUpdatesAsync(bool manual = false)`
  - `StreamMonitorCoordinator`: dispatches notifications in `AddChannelAsync`; exposes `CompleteOnboardingAsync`, `ResetFactoryDefaultsAsync`, `ExportBackupAsync`, `ValidateBackupAsync`, `RestoreBackupAsync`, `CheckForUpdatesAsync`, `SendTestNotification()`
  - `PopoverViewModel`: exposes `bool IsPopoverCelebrating`, `bool ReduceMotion`, `bool IsLiveSectionCelebrating`, `bool IsUpcomingSectionCelebrating`
  - `SettingsViewModel`: exposes `bool ReduceMotion`, `bool IsAutoUpdateEnabled`, `string CurrentVersion`, `string? UpdateStatusText`, `bool IsCheckingUpdates`, `string? BackupStatusText`, `string? BackupErrorMessage`, `int WatchedChannelCount`, `Func<CliReleaseDto, void>? OnShowUpdateWindow`, `Func<Task>? OnShowEasterEggAlertAsync`, `CheckForUpdatesManuallyAsync()`, `ShowPreviewUpdateWindow()`, `ExportBackupToFileAsync(string filePath)`, `ValidateBackupFileAsync(string filePath)`, `RestoreBackupFromFileAsync(string filePath)`, `PerformFactoryResetAsync()`, `SendTestNotification()`, `SkipUpdateVersionAsync(string tagName)`
  - `OnboardingViewModel`: exposes `string SelectedPlatform`, `string CustomHandle`, `string? ValidationError`, `bool HasValidationError`, `bool EnableUpdateChecks`, `bool NeverAskUpdatesAgain`, `bool HasSentTestNotification`, `SendTestNotification()`, `Task<bool> CompleteOnboardingAsync()`

- [x] **Step 1: Write failing xUnit tests in `VRemindWin/tests/VRemindWin.Tests/CoordinatorAndViewModelTests.cs`**

Add tests in `CoordinatorAndViewModelTests.cs`:
1. `AddChannelAsync_DispatchesNotifications_WhenDevBirthdayReturned()`: verifies that when `AddChannelAsync` returns a state containing a `developerBirthday` notification, `_toastDispatcher.Dispatch` is called once.
2. `PopoverViewModel_ExposesCelebrationAndReduceMotionFlags()`: verifies `IsPopoverCelebrating`, `IsLiveSectionCelebrating`, `IsUpcomingSectionCelebrating`, and `ReduceMotion` reflect `CurrentState`.
3. `SettingsViewModel_CheckForUpdatesManually_WhenPrivateRepo404_SendsTestToastAndOpensPreview()`: verifies that when `CheckForUpdatesAsync(manual: true)` returns `Status == "failed"` with `HttpStatusCode == 404`, `SettingsViewModel` dispatches a test toast notification, sets `UpdateStatusText`, and invokes `OnShowUpdateWindow` with the built-in v1.3.1 preview release.
4. `SettingsViewModel_CheckForUpdatesManually_WhenLocalNewer_TriggersEasterEggAlert()`: verifies `Status == "localVersionNewer"` invokes `OnShowEasterEggAlertAsync`.
5. `SettingsViewModel_FactoryResetAndBackupRestore_SyncStartupAndTriggerRefresh()`: verifies `PerformFactoryResetAsync` disables `StartupRegistryService` and updates coordinator state, and `RestoreBackupFromFileAsync` syncs `StartupRegistryService` with restored `LaunchAtLogin` and triggers a refresh.
6. `OnboardingViewModel_CompletesOnboardingAndHandlesMutualExclusion()`: verifies `EnableUpdateChecks` and `NeverAskUpdatesAgain` toggle mutually exclusively and `CompleteOnboardingAsync()` returns `true` on valid input and `false` with `ValidationError` on invalid input.

- [x] **Step 2: Run `dotnet test` to verify the new tests fail**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`
Expected: FAIL to compile (`OnboardingViewModel`, new `ICoreCliBridge` methods not defined).

- [x] **Step 3: Implement C# contracts, bridge methods, coordinator methods, and ViewModels**

1. Update `CliContracts.cs` with default-valued optional record parameters so existing tests construct cleanly while deserializing all new JSON properties.
2. Update `ICoreCliBridge.cs` and `CoreCliBridge.cs` to pass `--reduce-motion`, `--update-consent`, `--ignored-update-tag`, and `--has-completed-onboarding` in `UpdateSettingsAsync`, and add the 6 new CLI command methods.
3. Update `StreamMonitorCoordinator.cs` to call `DispatchNotificationsSafely(response.State.Notifications)` inside `AddChannelAsync` and add pass-through methods for onboarding, reset, backup/restore, update checks, and `SendTestNotification`.
4. Update `PopoverViewModel.cs`, `SettingsViewModel.cs`, and create `OnboardingViewModel.cs`. Include `SettingsViewModel.BuildPreviewRelease()` containing a rich v1.3.1 Markdown changelog with a blockquote, checklist, table, code block, and `<video>` tag so the release notes viewer can be previewed offline/while the repo is private.

- [x] **Step 4: Run `dotnet test` to verify all tests pass**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`
Expected: PASS.

---

### Task 3: Native Markdown & Video Parser (`MarkdownParser`)

**Files:**
- Create: `VRemindWin/src/VRemindWin.Core/Models/MarkdownModels.cs`
- Create: `VRemindWin/src/VRemindWin.Core/Services/MarkdownParser.cs`
- Test: `VRemindWin/tests/VRemindWin.Tests/MarkdownParserTests.cs`

**Interfaces:**
- Consumes: Raw GitHub release Markdown strings
- Produces:
  - `MarkdownBlock` discriminated record hierarchy in `VRemindWin.Core.Models`:
    - `HeadingBlock(int Level, string Text)`
    - `ParagraphBlock(string Text)`
    - `BulletBlock(string Text)`
    - `NestedBulletBlock(int Indent, string Text)`
    - `NumberedListBlock(int Index, string Text)`
    - `TaskItemBlock(bool Checked, string Text)`
    - `BlockquoteBlock(string Text)`
    - `TableBlock(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)`
    - `CodeBlockItem(string Code)`
    - `DividerBlock()`
    - `ImageBlock(string Url, string? Alt)`
    - `VideoBlock(string Url, string? Alt)`
  - `MarkdownInlineSpan` record hierarchy:
    - `PlainTextSpan(string Text)`
    - `BoldSpan(string Text)`
    - `ItalicSpan(string Text)`
    - `InlineCodeSpan(string Text)`
    - `HyperlinkSpan(string Label, string Url)`
    - `LineBreakSpan()`
  - `MarkdownParser.Parse(string markdown) -> IReadOnlyList<MarkdownBlock>`
  - `MarkdownParser.ParseInline(string text) -> IReadOnlyList<MarkdownInlineSpan>`

- [x] **Step 1: Write failing xUnit tests in `VRemindWin/tests/VRemindWin.Tests/MarkdownParserTests.cs`**

Add tests in `MarkdownParserTests.cs`:
1. `Parse_Blockquotes_StripsGreaterThanAndJoinsParagraphs()`: verifies `> quote line 1\n> quote line 2` produces `BlockquoteBlock("quote line 1 quote line 2")` with no leading `>`.
2. `Parse_ListsAndTasks_ParsesBulletsNestedBulletsNumberedAndChecklists()`: verifies `- item`, `  - nested`, `1. first`, `- [x] done`, `- [ ] todo`.
3. `Parse_TablesAndCodeBlocks_ParsesGfmTableAndFencedCode()`: verifies pipe tables with separator rows and ` ``` ` fenced blocks.
4. `Parse_Media_DetectsHtmlVideoAndMp4Links_LeavesWebmAsParagraph()`: verifies `<video src="https://github.com/user-attachments/assets/1234"></video>` and `[demo](https://example.com/clip.mp4)` become `VideoBlock`, `![shot](https://example.com/pic.png)` becomes `ImageBlock`, and `[webm](https://example.com/clip.webm)` stays `ParagraphBlock`.
5. `ParseInline_ParsesBoldItalicCodeLinksBareUrlsAndBrTags()`: verifies `**bold**`, `*italic*`, `` `code` ``, `[VRemind](https://github.com)`, bare `https://example.com`, and `<br>` tokenize into the expected `MarkdownInlineSpan` sequence.

- [x] **Step 2: Run `dotnet test` to verify `MarkdownParserTests` fails**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`
Expected: FAIL to compile (`MarkdownParser` and `MarkdownModels` not defined).

- [x] **Step 3: Implement `MarkdownModels.cs` and `MarkdownParser.cs`**

Port the block and inline parsing rules from `VRemind/Sources/VRemindKit/Views/Components/MarkdownView.swift` into `MarkdownModels.cs` and `MarkdownParser.cs`.

- [x] **Step 4: Run `dotnet test` to verify `MarkdownParserTests` passes**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`
Expected: PASS.

---

### Task 4: DirectX 10 Hardware-Accelerated Celebration Mode in `PopoverView` (`ConfettiOverlayControl` & Rainbow Accents)

**Files:**
- Create: `VRemindWin/src/VRemindWin/Views/Controls/ConfettiOverlayControl.cs`
- Modify: `VRemindWin/src/VRemindWin/Helpers/UiConverters.cs`
- Modify: `VRemindWin/src/VRemindWin/Views/PopoverView.xaml`
- Modify: `VRemindWin/src/VRemindWin/Views/PopoverView.xaml.cs`
- Modify: `VRemindWin/src/VRemindWin/Views/PopoverWindow.xaml.cs`

**Interfaces:**
- Consumes: `PopoverViewModel.IsPopoverCelebrating`, `PopoverViewModel.ReduceMotion`, `PopoverViewModel.IsLiveSectionCelebrating`, `PopoverViewModel.IsUpcomingSectionCelebrating`, `CliChannelItem.IsCelebration`
- Produces:
  - `ConfettiOverlayControl`: `Canvas`-derived control with `SetActive(bool isActive)` driving 45 deterministic particles via `CompositionTarget.Rendering` and GPU-accelerated `CompositeTransform` (`TranslateX`, `TranslateY`, `Rotation`) on the DirectX 10/11 DirectComposition pipeline.
  - Rainbow `LinearGradientBrush` animation in `PopoverView` (`SetWindowVisible(bool isVisible)`) updating `StartPoint` / `EndPoint` via `CompositionTarget.Rendering` (`3.0s` cycle normal, `10.0s` cycle with `ReduceMotion`) for celebrating row pills/rings/time labels and celebrating section headers.

- [x] **Step 1: Implement `ConfettiOverlayControl.cs` and update `PopoverView.xaml` / `PopoverView.xaml.cs` / `PopoverWindow.xaml.cs`**

1. Create `VRemindWin/src/VRemindWin/Views/Controls/ConfettiOverlayControl.cs`:
   - Pre-allocate 45 shapes (`Rectangle` and `Ellipse`) with deterministic sine/cosine initial positions, sway amplitudes/frequencies, speeds, and rainbow palette colors (`#FF0055`, `#FF9900`, `#FFE600`, `#33CC33`, `#00BFFF`, `#9933FF`).
   - Assign each shape a `CompositeTransform` (`CenterX = width / 2`, `CenterY = height / 2`) once at initialization; never call `Canvas.SetLeft` or `Canvas.SetTop` during animation frames so all transforms execute on the DirectX 10 GPU composition layer.
   - Subscribe to `CompositionTarget.Rendering` only when `isActive == true` and `ActualWidth > 0`; unsubscribe and collapse visibility immediately when `isActive == false` or `Unloaded`.
2. Update `PopoverView.xaml` and `PopoverView.xaml.cs`:
   - Embed `<controls:ConfettiOverlayControl x:Name="ConfettiOverlay" Grid.RowSpan="5" IsHitTestVisible="False" />` as the top overlay in `RootGrid`.
   - Maintain an animated rainbow `LinearGradientBrush` updated on `CompositionTarget.Rendering` when the popover is visible and any channel is celebrating, applying it to celebrating `LIVE` / `UPCOMING` badges, avatar rings, `TimeLabel` foregrounds, `LIVE NOW` / `UPCOMING` section headers, and the top `[● N LIVE]` pill.
3. Update `PopoverWindow.xaml.cs` to call `_popoverView.SetPopoverVisible(true)` in `ShowAt` and `_popoverView.SetPopoverVisible(false)` in `HideFlyout` so all GPU rendering callbacks stop the instant the flyout is dismissed.

- [x] **Step 2: Build `VRemindWin.sln` and run tests to verify**

Run:
```powershell
dotnet build VRemindWin\VRemindWin.sln -c Debug -p:Platform=x64
dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj
```
Expected: `0 Warning(s)`, `0 Error(s)`, all tests PASS.

---

### Task 5: Full 3-Tab `SettingsWindow`, `OnboardingWindow`, `UpdateAvailableWindow` & `MarkdownViewControl`

**Files:**
- Create: `VRemindWin/src/VRemindWin/Services/NativeFileDialogService.cs`
- Create: `VRemindWin/src/VRemindWin/Views/Controls/MarkdownViewControl.cs`
- Create: `VRemindWin/src/VRemindWin/Views/UpdateAvailableWindow.xaml`
- Create: `VRemindWin/src/VRemindWin/Views/UpdateAvailableWindow.xaml.cs`
- Create: `VRemindWin/src/VRemindWin/Views/OnboardingWindow.xaml`
- Create: `VRemindWin/src/VRemindWin/Views/OnboardingWindow.xaml.cs`
- Modify: `VRemindWin/src/VRemindWin/Views/SettingsWindow.xaml`
- Modify: `VRemindWin/src/VRemindWin/Views/SettingsWindow.xaml.cs`
- Modify: `VRemindWin/src/VRemindWin/App.xaml.cs`
- Modify: `README.md`

**Interfaces:**
- Consumes: `SettingsViewModel`, `OnboardingViewModel`, `MarkdownParser`, `CliReleaseDto`
- Produces:
  - `NativeFileDialogService.ShowSaveJsonDialog(IntPtr ownerHwnd, string defaultFileName) -> string?` and `ShowOpenJsonDialog(IntPtr ownerHwnd) -> string?` via `comdlg32.dll` (`GetSaveFileNameW` / `GetOpenFileNameW`)
  - `MarkdownViewControl`: native WinUI 3 renderer for all `MarkdownBlock` and `MarkdownInlineSpan` types, including `MediaPlayerElement` muted looping video playback with top-right `Muted` / `Unmuted` toggle pill, click-to-pause, `"Open Video in Browser"` error fallback, and `CleanupMediaPlayers()` on window close
  - `UpdateAvailableWindow(CliReleaseDto release, string currentVersion, Action<Uri> onOpenUri, Func<string, Task> onSkipVersion)` (`480×420` DIP)
  - `OnboardingWindow(OnboardingViewModel viewModel)` (`460×520` DIP)
  - `SettingsWindow(SettingsViewModel viewModel, Action<Uri> onOpenUri, Action<CliReleaseDto> onOpenUpdateWindow)` (`480×480` DIP, 3 tabs: `General`, `Notifications`, `About`)

- [x] **Step 1: Implement `NativeFileDialogService.cs` and `MarkdownViewControl.cs`**

1. Create `VRemindWin/src/VRemindWin/Services/NativeFileDialogService.cs` wrapping Win32 `OPENFILENAMEW` (`GetSaveFileNameW` / `GetOpenFileNameW` in `comdlg32.dll`) with filter `"JSON Files (*.json)\0*.json\0All Files (*.*)\0*.*\0\0"` and flags `OFN_EXPLORER | OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR`.
2. Create `VRemindWin/src/VRemindWin/Views/Controls/MarkdownViewControl.cs` rendering `MarkdownParser.Parse(markdown)` into native WinUI 3 XAML elements (`RichTextBlock`, bordered blockquote cards, `Consolas` code blocks, `Grid` tables, `Image`, and `MediaPlayerElement` with muted looping autoplay and deterministic `CleanupMediaPlayers()` disposal).

- [x] **Step 2: Implement `UpdateAvailableWindow`, `OnboardingWindow`, and 3-Tab `SettingsWindow`, and wire into `App.xaml.cs`**

1. Create `UpdateAvailableWindow.xaml` / `.cs` (`480×420` DIP) hosting `MarkdownViewControl` and the `Skip This Version`, `Remind Me Later`, and `Download Update` buttons; call `MarkdownControl.CleanupMediaPlayers()` in `Closed`.
2. Create `OnboardingWindow.xaml` / `.cs` (`460×520` DIP) with platform picker, `@bukancaitlyn` starter handle input, `Send Test Notification` button, update consent checkboxes, and `Get Started` button.
3. Upgrade `SettingsWindow.xaml` / `.cs` (`480×480` DIP) to a 3-tab layout (`General`, `Notifications`, `About`) with all controls, `ContentDialog` confirmations for Restore Backup, Factory Reset, Backup Error, and the `"Did you steal Caitlyn's computer?"` Easter egg alert.
4. Update `App.xaml.cs`:
   - In `StartMonitoringAsync`: after `_coordinator.InitializeAsync()`, if `!_coordinator.CurrentState.Settings.HasCompletedOnboarding`, open `OnboardingWindow`.
   - If `_coordinator.CurrentState.Settings.UpdateConsent == "allowed"`, run a background non-manual update check on startup and open `UpdateAvailableWindow` if an update is available.
   - Wire `SettingsWindow` and `UpdateAvailableWindow` callbacks.
5. Update `README.md` to document Celebration Mode, 3-Tab Settings, JSON Backup/Restore, First-Run Onboarding, and the Native Markdown/Video Updater.

- [x] **Step 3: Build full distribution bundle and run all Swift and C# test suites**

Run:
```powershell
swift test --package-path VRemindCoreCLI
dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-app.ps1
```
Expected: All Swift XCTests PASS, all C# xUnit tests PASS, and `dist\VRemindWin\VRemindWin.exe` + `dist\VRemindWin\vremind-core.exe` package cleanly.
