# VRemind for Windows (VRemind-win) v1 Design Specification

- **Date:** 2026-11-13
- **Minimum Target OS:** Windows 10 version 1809 / build `10.0.17763` (Windows 10 Enterprise LTSC 2019) through Windows 11
- **Devbox / Contributor Baseline:** Windows 10 21H2 / build `10.0.19044` (Windows 10 IoT Enterprise LTSC 2021) and newer
- **Frontend Framework:** Native WinUI 3 (Windows App SDK, Unpackaged C# / .NET 9, `TargetPlatformMinVersion = 10.0.17763.0`)
- **Backend Engine:** Swift `VRemindCore` (`C:\CaitDev\VRemind-win\VRemindCore`) compiled as a headless companion CLI (`vremind-core.exe`)

---

## 1. Intent and Why

**VRemind** is a native macOS menu bar utility that monitors VTubers and creators on YouTube and Twitch without API keys, showing live/upcoming/offline status in a compact popover and firing desktop notifications when streams go live.

The goal of **VRemind-win** is to bring VRemind to Windows users as a native **WinUI 3** system tray utility with the same core front-end behavior as the Mac app, starting with a rock-solid **v1** focused on the tray icon, taskbar flyout popover, watchlist management, and functional clickable Windows toast notifications.

Crucially, `VRemindCore` (Swift) is used directly as a shared, drop-in backend engine rather than being rewritten in C#. Whenever YouTube or Twitch changes their markup or APIs, updating `VRemindCore` updates both `VRemind` (macOS) and `VRemind-win` (Windows) like a Lego piece.

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals (v1)
1. **Drop-In `VRemindCore` Integration:** Build a thin, headless Swift companion executable (`vremind-core.exe`) on Windows that links directly against `../VRemindCore` and owns all channel normalization, scraping, polling passes, state persistence (`channels.json`, `settings.json`, `statuses.json`), and notification deduplication.
2. **Native Unpackaged WinUI 3 Tray Utility (Windows 10 1809+ LTSC Support):** Run as a lightweight background tray process on **Windows 10 version 1809 (`10.0.17763`, LTSC 2019)**, **Windows 10 21H2 (`10.0.19044`, LTSC 2021)**, and **Windows 11** without a main taskbar window and without requiring the Microsoft Store or MSIX certificate sideloading. As long as Microsoft supports an LTSC release, VRemind-win supports it.
3. **Mac-Parity Tray Icon & Flyout Popover (`360×440` DIP):**
   - System tray icon that switches between a normal monochrome antenna icon (all offline/upcoming) and a red antenna icon (any watched channel live).
   - Left-clicking the tray icon toggles a borderless `360×440` WinUI 3 flyout window anchored cleanly above the Windows taskbar/tray area that auto-dismisses when focus is lost.
   - Right-clicking the tray icon opens a quick context menu (`Open VRemind`, `Refresh Now`, `Settings...`, `Quit VRemind`).
   - Popover UI matching Mac `MenuBarStatusView`: header (`VRemind`, `[● N LIVE]` badge, Refresh, Settings, Quit), scrollable `LIVE NOW` / `UPCOMING` / `OFFLINE` sections, clickable channel rows with avatar + platform badge + uptime/countdown/last-streamed subtitle + hover delete button, and bottom add-channel bar (platform dropdown, handle/URL input, Add button, inline validation errors, and `SecretCheck` dialog).
4. **Functional Clickable Windows Toast Notifications:**
   - Native Windows toast alerts for `🔴 LIVE`, `⏳ UPCOMING`, and the Dec 31 developer birthday message, respecting `notifyOnLive`, `notifyOnUpcoming`, and `playNotificationSound`.
   - Clicking a toast opens the stream URL (or `mailto:` link) in the default browser even in an unpackaged app on Windows 10 LTSC, without spawning a duplicate VRemind process.
5. **Minimal v1 Settings Window:**
   - Accessible from the popover gear button (`⚙`) or tray context menu.
   - Toggles for **Notify when channels go live**, **Notify for upcoming streams**, **Play notification sound**, and **Launch at Windows login**.
6. **Reproducible Windows 10 LTSC Toolchain Setup:**
   - Clear bootstrap scripts/instructions to install the missing build prerequisites on this machine (.NET 9 SDK, Visual Studio 2022 C++ / Windows SDK Build Tools, and Swift for Windows).

### Non-Goals (Deferred to v1.1+)
- First-run Onboarding wizard window (`OnboardingView`).
- Celebration Mode visual effects in the popover (animated rainbow text gradients and CPU confetti overlay). *(Note: `VRemindCore` still evaluates Dec 31 developer birthday notifications.)*
- GitHub Releases auto-updater, custom `MarkdownView`, and native release-notes video player.
- JSON Backup/Restore file pickers and Factory Reset flow.
- Windows Desktop Widgets.

### Anti-Goals
- **Do not duplicate scraping or domain logic in C#.** All YouTube HTML extraction, Twitch GraphQL queries, handle/URL parsing, section sorting (`SecretCheck` demotion), and deduplication rules must execute inside Swift `VRemindCore`.
- **Do not drop Windows 10 1809 (`10.0.17763`, LTSC 2019) compatibility.** Never gate core features on Windows 10 2004+ (`19041`) or Windows 11-only APIs without a working 1809 baseline implementation.
- **Do not require MSIX signing or Microsoft Store infrastructure.** Windows 10 LTSC 2019 (1809) and LTSC 2021 (21H2) strip the Store by default; the app must run and deliver clickable toast notifications as an unpackaged Win32/WinUI 3 app.
- **Do not allow multiple running instances.** Launching the executable a second time or activating a toast notification must route to the existing tray instance via a named mutex / single-instance activation pipe.

---

## 3. Environment & Constraints

1. **Devbox vs. Minimum Target OS:**
   - **Devbox / Contributor Baseline:** Windows 10 21H2 (`10.0.19044.0`, IoT Enterprise LTSC 2021, x64). Currently has `.NET 9.0.7` runtimes installed; no `.NET SDK`, Visual Studio Build Tools, Windows SDK, or Swift toolchain is installed yet.
   - **Minimum Target Runtime OS:** **Windows 10 version 1809 (`10.0.17763.0`, Enterprise LTSC 2019, x64)**.
   - WinUI 3 (Windows App SDK) supports Windows 10 version 1809 (`10.0.17763.0`) as its minimum OS floor (`<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>`).
2. **Unpackaged WinUI 3 & 1809 Compatibility Specifics:**
   - Requires `<WindowsPackageType>None</WindowsPackageType>`, `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>`, and `<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>` in `.csproj` so end users on LTSC 2019 (1809) or LTSC 2021 (21H2) do not need to install the Windows App Runtime MSIX package.
   - WinUI 3 does not provide a built-in `TrayIcon` or `NSPopover` equivalent; tray icon management and taskbar-anchored flyout positioning require Win32 interop (`Shell_NotifyIconW` + `AppWindow` positioning and `WM_ACTIVATE` transient dismissal), using only Win32 APIs present in 1809 (`17763`).
   - **1809-Compatible Unpackaged Windows Toast Notifications:** Because Windows App SDK's `AppNotificationManager` requires Windows 10 build `19041` (2004)+ for unpackaged apps, `WindowsToastService` must instead use the **Windows 10 1809 (`17763`)-compatible WinRT `ToastNotificationManager` + COM `INotificationActivationCallback` (`DesktopNotificationManagerCompat`) pattern**:
     1. Call `SetCurrentProcessExplicitAppUserModelID("MomochiSoundpost.VRemind")` at process startup.
     2. Register a COM `[ComImport, Guid("53E31837-6600-4A81-9395-75CFFE746F94")] INotificationActivationCallback` class (`VRemindToastActivator` with a fixed CLSID) in `HKCU\Software\Classes\CLSID\{<CLSID>}\LocalServer32` pointing to `"VRemindWin.exe" -ToastActivated` and via `CoRegisterClassObject` at runtime.
     3. Ensure a Start Menu shortcut (`%APPDATA%\Microsoft\Windows\Start Menu\Programs\VRemind.lnk`) exists with `System.AppUserModel.ID = "MomochiSoundpost.VRemind"` and `System.AppUserModel.ToastActivatorCLSID = "{<CLSID>}"`.
     4. Create and show XML toast payloads via `Windows.UI.Notifications.ToastNotificationManager.CreateToastNotifier("MomochiSoundpost.VRemind")`. This works identically across Windows 10 1809 (LTSC 2019), Windows 10 21H2 (LTSC 2021), and Windows 11.
3. **Swift on Windows (`FoundationNetworking`):**
   - `VRemindCore` is already written for cross-platform Swift (`#if canImport(FoundationNetworking)` and continuation-wrapped `URLSession.dataTask`).
   - Building `vremind-core.exe` on Windows requires the official Swift for Windows x64 toolchain plus Visual Studio C++ Build Tools and the Windows 10/11 SDK.
   - For distribution, the required Swift runtime DLLs (`swiftCore.dll`, `Foundation.dll`, `FoundationNetworking.dll`, `BlocksRuntime.dll`, libcurl/icu DLLs from the Swift Windows runtime) are copied next to `vremind-core.exe` during the build/publish step so end users do not need Swift installed.

---

## 4. Approaches Considered

### Approach 1: C# (.NET 9) + WinUI 3 (Unpackaged) with a C# Port of `VRemindCore`
- **Description:** Reimplement `VRemindCore`'s ~15 Swift files in a C# class library (`VRemind.Core`) using `HttpClient` and `System.Text.Json`, verified against `VRemindCoreTests` fixtures.
- **Trade-offs:** Single toolchain (.NET SDK only) and no Swift runtime DLLs to bundle, but every future YouTube `/live` or Twitch GraphQL scraper change must be ported and maintained in two languages (Swift and C#).

### Approach 2: C# (.NET 9) + WinUI 3 (Unpackaged) Frontend + Headless Swift `VRemindCore` CLI *(Chosen)*
- **Description:** Compile a small headless Swift CLI (`vremind-core.exe`) that imports `VRemindCore` directly and reads/writes JSON in `%APPDATA%\VRemind\`. The C# WinUI 3 app manages the system tray icon, flyout popover UI, 180s timer, power events, and Windows toast notifications, invoking `vremind-core.exe` asynchronously for state reads, channel mutations, and refresh passes.
- **Trade-offs:** Requires both .NET SDK and Swift for Windows + VS C++ Build Tools on the build machine and bundles Swift runtime DLLs alongside the app, **but** keeps `VRemindCore` as a 100% drop-in shared engine across macOS and Windows with zero duplicated scraper logic.

### Approach 3: C++/WinRT + WinUI 3 (Unpackaged) with a C++ Port of `VRemindCore`
- **Description:** Native C++20/WinRT WinUI 3 app with a C++ port of `VRemindCore`.
- **Trade-offs:** Smallest runtime overhead, but high WinRT IDL/XAML boilerplate and double scraper maintenance.

**Decision:** **Approach 2** was selected by the user so `VRemindCore` acts as a true Lego-piece drop-in backend for both `VRemind` (Mac) and `VRemind-win`.

---

## 5. Architecture & Component Design

### 5.1 Repository & Directory Layout

Within `C:\CaitDev\VRemind-win`:

```text
C:\CaitDev\VRemind-win\
├── VRemindCore\                  # Existing shared Swift package (untouched upstream repo)
├── VRemind\                      # Existing macOS reference repo (untouched)
├── docs\
│   └── superpowers\
│       └── specs\
│           └── 2026-11-13-vremind-win-v1-design.md
├── scripts\
│   ├── setup-toolchain.ps1       # Installs/verifies .NET 9 SDK, VS C++ Build Tools, Swift for Windows
│   ├── build-cli.ps1             # Builds vremind-core.exe and stages Swift runtime DLLs
│   └── build-app.ps1             # Builds VRemindWin unpackaged app + stages vremind-core.exe + assets
├── VRemindCoreCLI\               # Thin Swift executable package wrapping ../VRemindCore
│   ├── Package.swift
│   ├── Sources\VRemindCoreCLI\
│   │   └── main.swift
│   └── Tests\VRemindCoreCLITests\
└── VRemindWin\                   # C# .NET 9 Unpackaged WinUI 3 solution
    ├── VRemindWin.sln
    ├── src\VRemindWin\
    │   ├── VRemindWin.csproj
    │   ├── App.xaml / App.xaml.cs
    │   ├── Program.cs            # Single-instance mutex, AUMID init, Toast activation routing
    │   ├── Assets\               # Tray icons (tray-normal.ico, tray-live.ico, app-icon.ico)
    │   ├── Models\               # DTOs matching VRemindCoreCLI JSON output
    │   ├── Services\
    │   │   ├── CoreCliBridge.cs          # Async process runner for vremind-core.exe
    │   │   ├── StreamMonitorCoordinator.cs # 180s timer, single-flight refresh, power/sleep pause
    │   │   ├── WindowsToastService.cs    # AUMID shortcut setup + AppNotificationManager toasts
    │   │   ├── TrayIconService.cs        # Win32 Shell_NotifyIconW + context menu + flyout positioning
    │   │   └── StartupRegistryService.cs # HKCU\...\Run launch-at-login sync
    │   ├── ViewModels\
    │   │   ├── PopoverViewModel.cs
    │   │   └── SettingsViewModel.cs
    │   └── Views\
    │       ├── PopoverWindow.xaml / .cs  # Borderless 360x440 transient tray flyout window
    │       └── SettingsWindow.xaml / .cs # Minimal v1 preferences window
    └── tests\VRemindWin.Tests\           # xUnit tests for CLI JSON parsing, ViewModels, URL/toast routing
```

### 5.2 Data Storage (`%APPDATA%\VRemind\`)

All persistent files live in `%APPDATA%\VRemind\` (or a custom directory passed via `--data-dir <path>` for deterministic unit/integration testing):

1. `channels.json`: Read/written via `VRemindCore.JSONFileChannelStorage`.
   - On first run (when `channels.json` does not exist on disk), `vremind-core.exe` seeds `channels.json` with the default channel (`id: "youtube:@bukancaitlyn"`, `platform: .youtube`, `name: "Caitlyn Momochi"`, `handle: "@bukancaitlyn"`), matching Mac `ChannelStore.defaultChannels`. (If the user later deletes all channels, `channels.json` exists as `[]` and stays empty.)
2. `settings.json`: Read/written via `VRemindCore.JSONFileSettingsStorage` (`AppSettings`).
3. `statuses.json`: Last known `[String: StreamStatus]` dictionary serialized by `vremind-core.exe` so `StreamMonitorEngine.refreshPass` has `currentStatuses` across CLI invocations and can detect `.live` -> `.offline` transitions.

### 5.3 Headless Swift Companion CLI (`VRemindCoreCLI` / `vremind-core.exe`)

`vremind-core.exe` is invoked with `--data-dir <path>` (defaulting to `%APPDATA%\VRemind`) and writes a single UTF-8 JSON object to `stdout` (with `CREATE_NO_WINDOW` in C# so no console window ever flashes).

#### Subcommands:
1. `vremind-core.exe state [--data-dir <dir>]`
   - Loads `channels.json`, `settings.json`, and `statuses.json`.
   - Computes `StreamMonitorEngine.splitSections(channels:statuses:)`.
   - Emits `CliStateEnvelope` JSON (see schema below) without making network requests.
2. `vremind-core.exe refresh [--data-dir <dir>]`
   - Loads `channels.json`, `settings.json`, and `statuses.json`.
   - Runs `await StreamMonitorEngine().refreshPass(channels: channels, settings: settings, currentStatuses: statuses)`:
     - On `.metadataUpdated(channelId, name, avatarURL)`: updates channel `name` (if non-empty) and `avatarURL`.
     - On `.statusUpdated(channelId, status)`: updates `statuses[channelId] = status`; if `status` is `.live(_, _, startedAt)` sets `channel.lastStreamedAt = startedAt ?? Date()`; if transitioning from `.live` to `.offline` sets `channel.lastStreamedAt = Date()`.
     - On `.notifyLive(channel, videoId, title)`: sets `channel.lastNotifiedVideoId = videoId` and appends a `live` notification item to `notifications` output (with resolved `watchURL` via `StreamProviderRegistry.default.watchURL`).
     - On `.notifyUpcoming(channel, videoId, title)`: sets `channel.lastNotifiedUpcomingId = videoId` and appends an `upcoming` notification item to `notifications` output.
     - On `.notifyDeveloperBirthday(year)`: sets `settings.lastNotifiedDevBirthdayYear = year` and appends a `developerBirthday` notification item (`mailto:y2karyl@disroot.org?subject=Happy%20Birthday%20Caitlyn!`).
   - Saves updated `channels.json`, `settings.json`, and `statuses.json` atomically.
   - Outputs `CliStateEnvelope` JSON including the `notifications` array produced during this pass.
3. `vremind-core.exe add --platform <youtube|twitch> --input "<raw>" [--ignore-secret] [--data-dir <dir>]`
   - Trims input, runs `WatchedChannel.detectPlatformAndHandle(from:)` first, falling back to `WatchedChannel.normalizeHandle(_:for:)`.
   - If invalid, returns `{ "outcome": "validation_error", "errorMessage": "<exact Mac message>" }`:
     - YouTube: `"Please enter a valid channel handle or YouTube URL"`
     - Twitch: `"Please enter a valid Twitch username or URL"`
   - Checks case-insensitive duplicate against `WatchedChannel.makeID(platform:handle:)`. If duplicate, returns `{ "outcome": "validation_error", "errorMessage": "Channel is already in your watchlist" }`.
   - If `resolvedPlatform == .youtube && SecretCheck.isTarget(normalized) && !ignoreSecret`: returns `{ "outcome": "secret_prompt", "secretDialog": { "title": ..., "message": ..., "abort": ..., "cancel": ..., "ignore": ... } }` without modifying disk.
   - Otherwise appends `WatchedChannel`, saves `channels.json`, and returns `{ "outcome": "added", "state": <CliStateEnvelope> }`.
4. `vremind-core.exe remove --id "<channelId>" [--data-dir <dir>]`
   - Removes matching channel from `channels.json` and `statuses.json`, saves both, and returns `{ "outcome": "removed", "state": <CliStateEnvelope> }`.
5. `vremind-core.exe settings --notify-on-live <bool> --notify-on-upcoming <bool> --play-sound <bool> --launch-at-login <bool> [--data-dir <dir>]`
   - Updates the specified flags in `settings.json`, saves, and returns `{ "outcome": "updated", "state": <CliStateEnvelope> }`.

#### `CliStateEnvelope` JSON View-Ready Projection:
To keep the C# frontend 100% free of domain/formatting duplication, `vremind-core.exe` pre-formats each channel item in `sections.live`, `sections.upcoming`, and `sections.offline` with:
- `id` (`"youtube:@bukancaitlyn"`), `platform` (`"youtube"` | `"twitch"`), `platformDisplayName` (`"YouTube"` | `"Twitch"`), `name`, `handle`, `formattedHandle` (`"@bukancaitlyn"` for YouTube, `"ironmouse"` for Twitch), `avatarURL` (nullable string).
- `statusKind` (`"live"` | `"upcoming"` | `"offline"` | `"unknown"`).
- `streamTitle` (nullable string, for `live` and `upcoming`).
- `timeLabel` (string: for `live`, `ElapsedTime.format(from: startedAt, to: now)` e.g. `"42m"`; for `upcoming`, `ElapsedTime.upcomingString(until: scheduledStartTime, from: now)` e.g. `"in 25m"`; for `offline`/`unknown`, `ElapsedTime.lastStreamedString(from: lastStreamedAt, to: now)` e.g. `"Last streamed 2d ago"` or `"Offline"`).
- `watchURL` (string resolved via `StreamProviderRegistry.default.watchURL(for: channel, status: status)`).

### 5.4 WinUI 3 Tray Icon & Flyout Popover Behavior

1. **System Tray Icon (`TrayIconService`):**
   - Registers a Win32 `NOTIFYICONDATAW` icon on startup.
   - Icon switches between `tray-normal.ico` (white/adaptive antenna icon, tooltip `"VRemind"`) and `tray-live.ico` (red antenna icon, tooltip `"VRemind (Live now)"`) whenever `state.liveCount > 0`.
   - **Left-click (`WM_LBUTTONUP` / `NIN_SELECT`):** Toggles `PopoverWindow`.
   - **Right-click (`WM_RBUTTONUP` / `WM_CONTEXTMENU`):** Shows a native popup menu with `Open VRemind`, `Refresh Now`, `Settings...`, and `Quit VRemind`.
2. **Flyout Positioning & Transient Dismissal (`PopoverWindow`):**
   - Fixed logical size `360 × 440` DIP (scaled by monitor DPI via `GetDpiForWindow`).
   - Styled as a borderless, non-resizable tool window (`IsShownInSwitchers = false`, `IsAlwaysOnTop = true`, rounded corners on Win11 / clean border on Win10 `19044`).
   - Positioned horizontally centered on the tray icon (`Shell_NotifyIconGetRect`, clamped to `SystemParametersInfo(SPI_GETWORKAREA)` with an 8 DIP margin) and vertically adjacent to the taskbar edge.
   - Dismisses automatically when the window loses activation (`Window.Activated` with `WindowActivationState.Deactivated`), except while a modal `SecretCheck` dialog is open.
3. **Popover Layout & Interactions:**
   - **Header:** `"VRemind"` title on left, red `[● N LIVE]` pill when `liveCount > 0`; right-aligned icon buttons for **Refresh** (triggers immediate refresh pass; spins/disables while `IsRefreshing`), **Settings** (hides popover and opens/activates `SettingsWindow`), and **Quit** (exits application cleanly).
   - **Channel List:**
     - Empty placeholder when all three sections are empty.
     - Sections rendered in order: `LIVE NOW (N)`, `UPCOMING (N)`, `OFFLINE (N)` (each hidden if empty).
     - Each channel row displays:
       - 36×36 circular avatar (`PersonPicture` / `Ellipse` with remote `avatarURL` or initial monogram fallback) + small bottom-right platform dot (YouTube `#FF0000`, Twitch `#9146FF`).
       - Top line: bold `name` + muted `formattedHandle` (tooltip `"<PlatformDisplayName>: <handle>"`).
       - Second line: `streamTitle` + `· <timeLabel>` for live/upcoming, or `<timeLabel>` (`"Last streamed ..."`) for offline.
       - Right side: red `LIVE` badge when `statusKind == "live"`, plus a trash delete button (`🗑`) visible on pointer hover.
       - Clicking the row opens `watchURL` in the default browser via `Process.Start(new ProcessStartInfo(watchURL) { UseShellExecute = true })`.
   - **Add Channel Bar:**
     - Platform selector (`YouTube` / `Twitch` with colored dot), text box with dynamic placeholder (`"Add YouTube @handle..."` / `"Add Twitch username..."`), and `Add` button.
     - Pressing Enter or clicking `Add` calls `vremind-core.exe add`.
     - If `outcome == "secret_prompt"`, shows a 3-button dialog (`Abort`, `Cancel`, `Ignore`); clicking `Ignore` calls `vremind-core.exe add --ignore-secret`.
     - After adding a channel, clears the input and triggers an immediate background `refresh` pass.

### 5.5 Polling, Single-Flight Refresh & Power Management (`StreamMonitorCoordinator`)

- On app startup:
  1. Calls `vremind-core.exe state` immediately to populate the initial watchlist (including the seeded `@bukancaitlyn` channel on first run) so the popover is ready right away.
  2. Starts the background polling loop (`180` seconds interval) which immediately runs `vremind-core.exe refresh`.
- **Single-flight coalescing:** If a manual refresh or post-add refresh is requested while a `refresh` process is already running, sets `needsAnotherRefresh = true` and runs one follow-up pass once the current pass completes (matching Mac `StreamMonitorService.refreshAll()`).
- **Channel removal race safety:** Tracks an integer `statusEpoch` (or reloads from `vremind-core.exe state` if a channel was removed mid-refresh) so a deleted channel never reappears when an in-flight refresh finishes.
- **Sleep / Lock pause:** Subscribes to `Microsoft.Win32.SystemEvents.PowerModeChanged` (`Suspend` / `Resume`) and `SessionSwitch` (`SessionLock` / `SessionUnlock`), pausing the 180s polling timer while suspended/locked and triggering an immediate refresh upon resume/unlock.

### 5.6 Windows Toast Notifications & Single-Instance Activation (`WindowsToastService`)

1. **Single-Instance & 1809-Compatible AUMID/COM Bootstrap (`Program.cs`):**
   - Calls `SetCurrentProcessExplicitAppUserModelID("MomochiSoundpost.VRemind")` at entry.
   - Registers `HKCU\Software\Classes\CLSID\{D8B4E62A-5C39-4E8B-A193-7E4A6F90C112}\LocalServer32` -> `"<path-to-VRemindWin.exe>" -ToastActivated` (no admin rights required since it lives under `HKCU\Software\Classes`).
   - Ensures `%APPDATA%\Microsoft\Windows\Start Menu\Programs\VRemind.lnk` exists pointing to `VRemindWin.exe` with `System.AppUserModel.ID = "MomochiSoundpost.VRemind"` and `System.AppUserModel.ToastActivatorCLSID = "{D8B4E62A-5C39-4E8B-A193-7E4A6F90C112}"`.
   - Uses a named mutex (`Local\MomochiSoundpost.VRemind.SingleInstance`) + `CoRegisterClassObject` for `INotificationActivationCallback`. When a user clicks a toast while VRemind-win is running, Windows invokes `INotificationActivationCallback.Activate(appUserModelId, invokedArgs, ...)` directly in the running process (or launches `VRemindWin.exe -ToastActivated`, which registers the COM callback, processes the click URL, and exits if another instance already holds the tray mutex).
2. **Sending Toasts (`ToastNotificationManager.CreateToastNotifier("MomochiSoundpost.VRemind")`):**
   - Works natively on Windows 10 1809 (`17763`), 21H2 (`19044`), and Windows 11.
   - For each notification returned in `vremind-core.exe refresh`'s `notifications` array:
     - `live`: Title = `channelName`, Body = `"🔴 LIVE: " + title`, Launch args = `action=openUrl&url=<Uri.EscapeDataString(watchURL)>`.
     - `upcoming`: Title = `channelName`, Body = `"⏳ UPCOMING: " + title`, Launch args = `action=openUrl&url=<Uri.EscapeDataString(watchURL)>`.
     - `developerBirthday`: Title = `"🎉 Happy Birthday to the Developer!"`, Body = `"Today is Caitlyn Momochi's birthday! Click to send a birthday wish."`, Launch args = `action=openUrl&url=<Uri.EscapeDataString(mailtoURL)>`.
     - Audio: `<audio silent="true"/>` when `playSound == false`.
3. **Handling Toast Clicks:**
   - Validates that the decoded target URL uses `http://`, `https://`, or `mailto:` (matching `LinkOpener` security rules) before opening it with `UseShellExecute = true`.

---

## 6. What Is Left to the Builder

- Exact Win32 P/Invoke vs. `H.NotifyIcon.WinUI` choice for `TrayIconService` (either is acceptable as long as left-click flyout positioning, right-click menu, icon swap, and clean exit removal work reliably on Windows 10 build `17763` / 1809 and `19044` / 21H2).
- Exact visual styling tweaks in WinUI 3 XAML (`AcrylicBrush` / `Mica` fallback to solid dark/light theme brush on Windows 10 `17763` and `19044`).
- Exact icon rasterization script to generate `tray-normal.ico`, `tray-live.ico`, and `app-icon.ico` from `VRemind/Resources/Icons/app-icon.png` and an antenna vector path.
