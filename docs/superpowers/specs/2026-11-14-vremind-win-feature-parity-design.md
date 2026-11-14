# VRemind for Windows (`VRemind-win`) Complete Feature Parity Design Specification

- **Date:** 2026-11-14
- **Target Release:** `VRemind-win` v1.3.1 (Mac Feature Parity)
- **Minimum Target OS:** Windows 10 version 1809 / build `10.0.17763` (Windows 10 Enterprise LTSC 2019) through Windows 11
- **Devbox / Contributor Baseline:** Windows 10 21H2 / build `10.0.19044` (Windows 10 IoT Enterprise LTSC 2021) and newer
- **Frontend Framework:** Native WinUI 3 (Windows App SDK, Unpackaged C# / .NET 9, `TargetPlatformMinVersion = 10.0.17763.0`)
- **Backend Engine:** Swift `VRemindCore` (`C:\CaitDev\VRemind-win\VRemindCore`) + `VRemindCoreCLI` (`vremind-core.exe`)

---

## 1. Intent and Why

`VRemind-win` v1 established the core Windows system tray app: multi-channel YouTube & Twitch monitoring via `vremind-core.exe`, a `360×440` DIP taskbar flyout popover, 1809-compatible unpackaged COM toast notifications, and a minimal 4-checkbox settings window.

The goal of this project is to bring `VRemind-win` to **complete tray-app feature parity with macOS `VRemind` v1.3.1**—implementing the remaining features from the Mac app:
1. **Celebration Mode & Holiday Easter Egg** (multilingual milestone detection, animated rainbow badges/headers/accents, popover confetti rain, and `Reduce Motion`).
2. **Full 3-Tab Settings Window** (`General`, `Notifications`, `About`) including portable **JSON Backup & Restore**, **Factory Reset**, **Update Controls + Easter Egg**, **Test Notification**, and **About / Social Links**.
3. **First-Run Onboarding Wizard** (`OnboardingWindow`) for new installations.
4. **GitHub Software Updater & Native Markdown/Video Release Notes Viewer** (`UpdateAvailableWindow`, `MarkdownParser`, `MarkdownViewControl`), with a built-in test toast / release-notes preview fallback while the `VRemind-win` repository remains private.

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
1. **Celebration Mode Parity:**
   - Expose per-channel celebration status (`isCelebration`, `celebrationType`) and popover-level celebration state (`isPopoverCelebrating`) from `VRemindCoreCLI` using `VRemindCore.CelebrationDetector`.
   - Render animated rainbow gradients on celebrating `LIVE`/`UPCOMING` badges, section headers, and time labels in `PopoverView`, slowing the gradient cycle from `3.0s` to `10.0s` when `reduceMotion` is enabled.
   - Render a DirectX 10 (`D3D_FEATURE_LEVEL_10_0`)-compliant, hardware-accelerated confetti particle overlay (`ConfettiOverlayControl`) inside `PopoverView` at native display refresh rate (vsync via `CompositionTarget.Rendering` and GPU-accelerated `CompositeTransform` / `Microsoft.UI.Composition` visuals) when `isPopoverCelebrating && !reduceMotion`, halting completely whenever the popover is hidden or closed.
   - Dispatch the Dec 31 developer birthday notification immediately if `@bukancaitlyn` is added on Dec 31 (in addition to background refresh passes), throttled to once per calendar year via `lastNotifiedDevBirthdayYear`.
2. **Full 3-Tab Settings Window (`SettingsWindow`):**
   - Replace the minimal v1 settings panel with a `480×480` DIP 3-tab window (`General`, `Notifications`, `About`) matching macOS `PreferencesView`.
   - Support cross-platform JSON Backup & Restore (`BackupPayload` schema v1, default filename `vremind-backup-YYYY-MM-DD.json`) using Win32 native file dialogs (`GetSaveFileNameW` / `GetOpenFileNameW` via `comdlg32.dll`, which work reliably in unpackaged WinUI 3 on Windows 10 1809 LTSC without COM window-handle bugs).
   - Support Factory Reset (`Reset All Data to Factory Defaults...`) with a destructive confirmation dialog.
3. **First-Run Onboarding (`OnboardingWindow`):**
   - Automatically present `OnboardingWindow` (`460×520` DIP) on startup when `settings.hasCompletedOnboarding == false`.
   - Allow configuring the starter channel (pre-filled with `@bukancaitlyn` on YouTube, or any custom YouTube/Twitch handle/URL), sending a test Windows toast notification, setting GitHub auto-update consent, and completing onboarding.
4. **GitHub Updater & Native Markdown/Video Viewer (`UpdateAvailableWindow`):**
   - Implement `vremind-core.exe check-update` using `VRemindCore.GitHubRelease` and `VRemindCore.SemanticVersion`.
   - While `momochisoundpost/VRemind-win` is private/closed-source (or returns HTTP 404), manual update checks send a test Windows toast notification and offer a preview of `UpdateAvailableWindow` populated with the v1.3.1 release notes (including blockquotes, lists, tables, code blocks, images, and looping muted video).
   - Render release notes in a 100% native WinUI 3 `MarkdownViewControl` + `MediaPlayerElement` (zero `WebView2` dependency) with `Skip This Version`, `Remind Me Later`, and `Download Update` actions.

### Non-Goals
- **Windows 11 Desktop Widget Provider:** Deferred to lowest priority outside this milestone so the main tray application reaches full parity first without Windows 11 / MSIX packaging distractions.
- **In-place binary self-replacement:** Clicking `Download Update` opens the release `.zip` asset or release page in the default browser (matching macOS `VRemind`).

### Anti-Goals
- **No `WebView2` runtime requirement:** Windows 10 1809 LTSC does not ship with Edge WebView2 Runtime pre-installed. All Markdown and video rendering must use native WinUI 3 XAML controls and `MediaPlayerElement`.
- **No duplicated domain logic:** `CelebrationDetector`, `SemanticVersion`, `GitHubRelease`, and `BackupPayload` validation/sanitization must live in Swift (`VRemindCore` / `VRemindCoreCLI`) rather than being re-invented with divergent rules in C#.
- **No idle CPU/GPU drain:** Confetti timers, rainbow brush animations, and `MediaPlayer` instances must stop and release resources immediately when their parent window hides or closes.

---

## 3. Approaches Considered

### Approach 1: CoreCLI-First Logic + Native WinUI 3 Shell *(Chosen)*
- **Description:** Extend `vremind-core.exe` (`VRemindCoreCLI`) to expose celebration metadata in `CliStateEnvelope` and add `export-backup`, `restore-backup`, `reset`, `complete-onboarding`, and `check-update` subcommands backed directly by `VRemindCore`. `VRemindWin` implements the native WinUI 3 windows (`SettingsWindow`, `OnboardingWindow`, `UpdateAvailableWindow`), XAML `Canvas` confetti & rainbow animations, native Win32 Open/Save file pickers, and native XAML Markdown + video release notes viewer.
- **Trade-offs:** Adds new subcommands and DTO fields to the CLI bridge, **but** guarantees 100% behavioral and backup-file compatibility between macOS and Windows with full unit test coverage in both `swift test` and `dotnet test`.

### Approach 2: C#-Side Logic + Minimal CLI Changes
- **Description:** Only expose `isCelebration` from `vremind-core.exe`, and reimplement `BackupPayload`, factory reset, `SemanticVersion`, and `GitHubRelease` fetching in C# (`VRemindWin.Core`).
- **Trade-offs:** Fewer CLI subcommands, but duplicates version comparison, backup sanitization, and state reset rules across Swift and C#.

**Decision:** **Approach 1** was selected by the user.

---

## 4. Detailed Component Design

### 4.1 `VRemindCoreCLI` Extensions (`vremind-core.exe`)

#### 4.1.1 Enriched `CliChannelItem` & `CliStateEnvelope`
- `CliChannelItem` gains:
  - `isCelebration: Bool` — Computed via:
    - For `.live(_, let title, _)`: `CelebrationDetector.detect(title: title).isCelebration || isDevBirthdayToday(for: channel)`
    - For `.upcoming(_, let title, _)`: `CelebrationDetector.detect(title: title).isCelebration || isDevBirthdayToday(for: channel)`
    - For `.offline` / `.unknown`: `isDevBirthdayToday(for: channel)` (true when `CelebrationDetector.isDeveloperBirthday(channel: channel, date: now, calendar: calendar)` is true on Dec 31).
  - `celebrationType: String?` — `"birthday"`, `"debut"`, `"anniversary"`, `"developerBirthday"`, or `nil`.
- `CliStateEnvelope` gains:
  - `isPopoverCelebrating: Bool` — `true` when at least one channel in `sections.live` has `isCelebration == true`, OR at least one channel in `sections.upcoming` has `isCelebration == true` **and** its `scheduledStartTime` is in the same calendar day as `now` (`calendar.isDate(scheduledStart, inSameDayAs: now)`), OR `CelebrationDetector.isDeveloperBirthday` is true for any watched channel on `now`.
  - `appVersion: String` — `"1.3.1"` (single source of truth for version display and backup headers).

#### 4.1.2 Developer Birthday Immediate Trigger on `add`
- In `CliRunner.runAdd`: After appending `newChannel`, if `CelebrationDetector.isDeveloperBirthday(channel: newChannel, date: now, calendar: calendar)` is `true` and `settings.lastNotifiedDevBirthdayYear != calendar.component(.year, from: now)`:
  - Sets `settings.lastNotifiedDevBirthdayYear = calendar.component(.year, from: now)`, saves `settings.json`, and includes the `developerBirthday` `CliNotificationItem` in the returned `CliStateEnvelope.notifications` array so `VRemindWin` immediately displays the birthday toast.

#### 4.1.3 Extended `settings` Subcommand Options
`vremind-core.exe settings` continues to accept `--notify-on-live`, `--notify-on-upcoming`, `--play-sound`, and `--launch-at-login`, and adds optional flags:
- `--reduce-motion <true|false>`
- `--update-consent <undecided|allowed|neverAsk>`
- `--ignored-update-tag <tag|none>` (passing `"none"` or `""` clears `ignoredUpdateTag` to `nil`)
- `--has-completed-onboarding <true|false>`

#### 4.1.4 New Subcommand: `complete-onboarding`
`vremind-core.exe complete-onboarding [--platform <youtube|twitch>] [--input "<raw>"] [--update-consent <undecided|allowed|neverAsk>] [--data-dir <dir>]`
- Mirrors macOS `OnboardingViewModel.completeOnboarding()`:
  - If `--input` is omitted, empty, or resolves to YouTube `@bukancaitlyn`: ensures `@bukancaitlyn` (`Caitlyn Momochi`) is present in `channels.json`.
  - If `--input` is non-empty: validates via `WatchedChannel.detectPlatformAndHandle` / `normalizeHandle`. If invalid, returns `{ "outcome": "validation_error", "errorMessage": "..." }` without mutating disk.
  - If valid and not `@bukancaitlyn`: if `channels` currently contains only the seeded default `youtube:@bukancaitlyn`, replaces it with the user's chosen starter channel; otherwise appends it (if not already present).
  - Sets `settings.updateConsent` (defaulting to `.allowed` if omitted) and `settings.hasCompletedOnboarding = true`.
  - Saves `channels.json` and `settings.json` and returns `{ "outcome": "completed", "state": <CliStateEnvelope> }`.

#### 4.1.5 New Subcommand: `reset`
`vremind-core.exe reset [--data-dir <dir>]`
- Mirrors macOS `PreferencesViewModel.performFactoryReset()`:
  - Resets `settings` to `AppSettings(hasCompletedOnboarding: true)` (keeps `hasCompletedOnboarding: true` so resetting inside Settings does not re-pop Onboarding immediately, or matches default settings with `launchAtLogin: false`, `notifyOnLive: true`, `notifyOnUpcoming: false`, `playNotificationSound: true`, `reduceMotion: false`, `updateConsent: .undecided`). *(Builder call: keep `hasCompletedOnboarding: true` during in-app factory reset so the user isn't unexpectedly locked behind the onboarding modal right after clicking Reset in Settings.)*
  - Resets `channels.json` to `[WatchedChannel.defaultCaitlynChannel]`.
  - Clears `statuses.json` to `[:]`.
  - Returns `{ "outcome": "reset", "state": <CliStateEnvelope> }`.

#### 4.1.6 New Subcommands: `export-backup` & `restore-backup`
- `BackupPayload` (`schemaVersion = 1`, `appVersion`, `exportedAt`, `settings`, `channels`) is added to `VRemindCoreCLIKit` matching macOS `BackupPayload.swift` byte-for-byte (ISO-8601 dates, pretty-printed sorted keys).
- `vremind-core.exe export-backup --file "<path>" [--data-dir <dir>]`:
  - Loads current `channels.json` and `settings.json`, constructs `BackupPayload`, writes atomically to `<path>`, and returns `{ "outcome": "exported", "state": <CliStateEnvelope> }`.
- `vremind-core.exe validate-backup --file "<path>" [--data-dir <dir>]`:
  - Reads `<path>`, runs `BackupPayload.decodeAndValidate(from:)`, and returns `{ "outcome": "valid_backup", "backupChannelCount": N, "state": <CliStateEnvelope> }` or `{ "outcome": "backup_error", "errorMessage": "<BackupError.localizedDescription>" }`.
- `vremind-core.exe restore-backup --file "<path>" [--data-dir <dir>]`:
  - Reads and validates `<path>`, runs `payload.sanitizedForRestore()` (sets `hasCompletedOnboarding = true` and clears `lastNotifiedVideoId` / `lastNotifiedUpcomingId` on all restored channels), saves `channels.json` and `settings.json`, clears `statuses.json` to `[:]`, and returns `{ "outcome": "restored", "state": <CliStateEnvelope> }`.

#### 4.1.7 New Subcommand: `check-update`
`vremind-core.exe check-update [--manual] [--repo-url "<url>"] [--data-dir <dir>]`
- Default URL: `https://api.github.com/repos/momochisoundpost/VRemind-win/releases/latest`.
- If `--manual` is not passed and `settings.updateConsent != .allowed`, returns `{ "outcome": "skipped_not_consented", "updateResult": { "status": "skippedNotConsented" } }`.
- Performs GET with `User-Agent: VRemind-win/1.3.1` and `Accept: application/vnd.github+json`.
- Updates `settings.lastUpdateCheckDate = now` on successful HTTP response.
- Compares `SemanticVersion(release.tagName)` vs `SemanticVersion("1.3.1")`:
  - `remote > local`:
    - If `!manual && release.tagName == settings.ignoredUpdateTag`: returns `status: "skippedIgnored"`.
    - Otherwise returns `status: "updateAvailable"` with `release: GitHubReleaseDto` (including `tagName`, `name`, `body`, `htmlURL`, and preferred download URL: first `.zip` or `.exe` asset `browserDownloadURL`, falling back to `htmlURL`).
  - `local > remote`: returns `status: "localVersionNewer"`, `remoteVersion: release.tagName`.
  - `local == remote`: returns `status: "upToDate"`, `latestVersion: release.tagName`.
  - On HTTP 404 or network error: returns `status: "failed"`, `httpStatusCode: <code?>`, `errorMessage: "<reason>"`.

---

### 4.2 Celebration Mode UI in `VRemindWin` (`PopoverView`)

1. **Rainbow Visual Styling (`RainbowAnimationHelper` / `PopoverView`):**
   - Palette matches macOS `RainbowPalette`: `#FF0055`, `#FF9900`, `#FFE600`, `#33CC33`, `#00BFFF`, `#9933FF`, `#FF0055`.
   - When `PopoverWindow` is visible and any displayed item has `IsCelebration == true`, `CompositionTarget.Rendering` (at native display vsync, shifting over a `3.0s` cycle when `!ReduceMotion` or a gentle `10.0s` cycle when `ReduceMotion == true`) updates the `StartPoint` / `EndPoint` of a shared `LinearGradientBrush` on the DirectX 10/11 composition pipeline.
   - Applied to:
     - Celebrating row's `LIVE` / `UPCOMING` status pill background and avatar ring border.
     - Celebrating row's `TimeLabel` foreground.
     - `LIVE NOW` / `UPCOMING` section header text when any channel in that section has `IsCelebration == true`.
     - Header `[● N LIVE]` pill when any live channel is celebrating.
   - Channel names and stream titles remain standard high-contrast primary/secondary text colors for readability.

2. **DirectX 10 Hardware-Accelerated Popover Confetti Rain (`ConfettiOverlayControl`):**
   - Placed as the top-most child in `PopoverView`'s `RootGrid` with `IsHitTestVisible="False"`.
   - Deterministic 45-particle pool (rectangles, circles, streamers) using the exact same sine/cosine seed formulas and velocities as macOS `ConfettiOverlayView.swift`.
   - **DirectX 10 (`D3D_FEATURE_LEVEL_10_0`) Hardware Acceleration:** Driven by `CompositionTarget.Rendering` (locked to display vsync, e.g. 60 Hz / 144 Hz) and mutates GPU-accelerated `CompositeTransform` (`TranslateX`, `TranslateY`, `Rotation`) / `Microsoft.UI.Composition` `Visual` properties directly on the Direct3D 10/11 DirectComposition pipeline without triggering per-frame XAML CPU `Measure`/`Arrange` layout passes (`Canvas.SetLeft`/`SetTop` are avoided during animation).
   - Active **only** when `PopoverWindow` is visible AND `ViewModel.IsPopoverCelebrating` is `true` AND `!ViewModel.Settings.ReduceMotion`.
   - When inactive or when `PopoverWindow` hides, unsubscribes from `CompositionTarget.Rendering` and hides the layer so background GPU/CPU usage is strictly `0%`.

---

### 4.3 Full 3-Tab Settings Window (`SettingsWindow` & `SettingsViewModel`)

`SettingsWindow` is sized to `480×480` DIP and uses a 3-tab selector (`General`, `Notifications`, `About`):

1. **Tab 1: General**
   - **Startup & Motion:**
     - `Launch VRemind at Windows login` (checkbox)
     - `Reduce Motion (Turn off popover confetti & slow rainbow)` (checkbox)
   - **Updates:**
     - `Current Version: 1.3.1`
     - `Automatically check for updates` (checkbox bound to `updateConsent == "allowed"`)
     - `Check for Updates Now` button + `Preview Update Notes` button + inline status label.
     - **Private-Repo Test Notification Behavior:** When the user clicks `Check for Updates Now` and GitHub returns HTTP 404 (because `VRemind-win` is still private/closed-source), `SettingsViewModel` dispatches a Windows toast notification (`"🔔 VRemind Update & Toast Check"`, `"Repository is currently private — notifications and updater UI are ready!"`), updates the status text to `"Repo is currently private (test notification sent)."`, and opens the `UpdateAvailableWindow` with the built-in v1.3.1 sample release notes so the user can test the full update dialog immediately.
     - **Easter Egg Alert:** If `check-update` returns `localVersionNewer` (or via a diagnostic trigger), displays a `ContentDialog` titled `"Did you steal Caitlyn's computer?"` with message `"Your version (1.3.1) is newer than what's available on GitHub."` and buttons `Yes` (opens `https://www.youtube.com/watch?v=HKieGUH9pzg`) and `No`.
   - **Data & Backup:**
     - `Export Backup...` button: opens Win32 `GetSaveFileNameW` pre-filled with `vremind-backup-YYYY-MM-DD.json`, calls `vremind-core.exe export-backup --file <path>`, and shows `"Backup exported to <filename>."`.
     - `Restore Backup...` button: opens Win32 `GetOpenFileNameW` (`*.json`), calls `vremind-core.exe validate-backup --file <path>`. If invalid, shows `"Backup Failed"` dialog. If valid, prompts `"Restore Backup?"` (`"This will replace your current watchlist (N channel(s)) and app preferences with the selected backup. This cannot be undone."`). On confirm, calls `vremind-core.exe restore-backup --file <path>`, syncs `StartupRegistryService`, and triggers an immediate `StreamMonitorCoordinator.RequestImmediateRefresh()`.
   - **Danger Zone:**
     - Red `"Danger Zone"` header + `Reset All Data to Factory Defaults...` button.
     - Prompts `"Reset All Data?"` (`"This will restore default settings, reset your watchlist back to @bukancaitlyn, and disable launch at login. This action cannot be undone."`).
     - On confirm, calls `vremind-core.exe reset`, disables launch at login in `StartupRegistryService`, and refreshes `StreamMonitorCoordinator`.

2. **Tab 2: Notifications**
   - **Stream Alerts:**
     - `Notify when a stream goes live on air`
     - `Notify when an upcoming stream / waiting room appears`
     - `Play sound with notifications`
   - **Notification Check & System Settings:**
     - `Send Test Notification` button: sends an immediate sample toast via `WindowsToastService` (`"🎉 VRemind Test Notification"`, `"Notifications are working! Click to open @bukancaitlyn."`, target URL `https://www.youtube.com/@bukancaitlyn`).
     - `Open Windows Notification Settings` button: opens `ms-settings:notifications` via `UseShellExecute = true`.

3. **Tab 3: About**
   - Centered layout with `app-icon.ico` / PNG image (`54×54`), `"VRemind"`, `"Version 1.3.1"`, attribution copy (`"VRemind by Caitlyn Momochi, initially vibecoded for their Hackintosh video"`), italic fork copy (`"You don't like what I did? Go fork yourself, it's free and open source."`), and 4 social buttons:
     - `YouTube` (`https://www.youtube.com/@bukancaitlyn`)
     - `Twitter/X` (`https://twitter.com/Y2Karyl`)
     - `GitHub` (`https://github.com/momochisoundpost`)
     - `Instagram` (`https://www.instagram.com/bukancaitlyn`)

---

### 4.4 First-Run Onboarding Window (`OnboardingWindow` & `OnboardingViewModel`)

- On application startup, after `StreamMonitorCoordinator.InitializeAsync()` loads initial state, if `!state.Settings.HasCompletedOnboarding`, `App.xaml.cs` opens `OnboardingWindow` (`460×520` DIP, centered on screen).
- **Sections:**
  1. **Header:** App icon, `"Welcome to VRemind"`, `"Your lightweight Windows tray companion for VTuber & creator streams."`
  2. **Step 1 — Choose a Channel to Watch:** Platform dropdown (`YouTube` / `Twitch`) + handle/URL `TextBox` pre-filled with `@bukancaitlyn`, plus inline red validation error text if invalid.
  3. **Step 2 — Desktop Notifications:** Explanation that VRemind uses native Windows toast alerts + a `"Send Test Notification"` button (shows a green `"✓ Test notification sent"` badge once clicked).
  4. **Step 3 — Automatic Updates:** Checkboxes for `"Automatically check for updates on GitHub"` (default checked) and `"Don't ask me again"` (mutually exclusive).
  5. **Footer:** Primary `"Get Started"` button. Calls `vremind-core.exe complete-onboarding`, triggers `StreamMonitorCoordinator.RequestImmediateRefresh()`, and closes `OnboardingWindow`.

---

### 4.5 Software Update Window & Native Markdown/Video Renderer (`UpdateAvailableWindow`, `MarkdownParser`, `MarkdownViewControl`)

1. **`MarkdownParser` (`VRemindWin.Core/Services/MarkdownParser.cs`):**
   - Pure, unit-tested C# port of macOS `MarkdownParser.swift` producing `IReadOnlyList<MarkdownBlock>`:
     - `Heading(int Level, string Text)`
     - `Paragraph(string Text)`
     - `Bullet(string Text)`
     - `NestedBullet(int Indent, string Text)`
     - `NumberedList(int Index, string Text)`
     - `TaskItem(bool Checked, string Text)`
     - `Blockquote(string Text)`
     - `Table(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)`
     - `CodeBlock(string Code)`
     - `Divider`
     - `Image(string Url, string? Alt)`
     - `Video(string Url, string? Alt)` (supports `<video>`/`<source src="...">` HTML tags and `.mp4`/`.mov`/`.m4v` Markdown links/images; `.webm` stays a clickable link).
   - Also includes an inline span tokenizer (`MarkdownInlineParser`) for `**bold**`, `*italic*`, `` `code` ``, `[label](url)`, bare `https://...` links, and `<br>` line breaks.

2. **`MarkdownViewControl` (`VRemindWin/Views/Controls/MarkdownViewControl.cs`):**
   - Renders `MarkdownBlock` items into a native WinUI 3 `StackPanel`:
     - Headings, paragraphs, bullets, numbered lists, task items (`☑` / `☐`) with `RichTextBlock` inline formatting and clickable `Hyperlink` spans (validated via `LinkSafetyValidator`).
     - Blockquotes rendered with a 3px accent left border, subtle secondary card fill, and italic text.
     - Code blocks rendered in a rounded secondary border with `Consolas` monospace font.
     - GFM tables rendered as a bordered `Grid` with header row shading.
     - Images rendered via `Image` (`BitmapImage`) with rounded corners.
     - Videos rendered via `MediaPlayerElement` (`AutoPlay = true`, `IsMuted = true`, `IsLoopingEnabled = true`) inside a rounded container with a top-right `Muted` / `Unmuted` toggle button, click-to-pause, and an `"Open Video in Browser"` fallback button on `MediaFailed`. All `MediaPlayer` instances are tracked and disposed when `UpdateAvailableWindow` closes.

3. **`UpdateAvailableWindow` (`480×420` DIP):**
   - Displays release title, current vs. new version, scrollable `MarkdownViewControl`, and footer buttons:
     - `Skip This Version` -> saves `ignoredUpdateTag` via CLI and closes.
     - `Remind Me Later` -> closes without ignoring.
     - `Download Update` -> opens the asset download URL (or release HTML URL) in the default browser and closes.

---

## 5. What Is Left to the Builder

- Exact WinUI 3 tab control choice in `SettingsWindow` (either a clean segmented radio-button tab bar at the top of `SettingsWindow` or `TabView`, whichever renders cleaner without extra WinUI tab-strip chrome in a `480×480` utility window on Windows 10 1809/21H2).
- Exact sample Markdown + video URL used in the built-in v1.3.1 release-notes preview when `VRemind-win` is private.
- Exact particle count tuning in `ConfettiOverlayControl` (45 particles default, vsync-driven via `CompositionTarget.Rendering` + GPU `CompositeTransform`).
