# Changelog

## [1.3.1] - 2026-11-14

### Release Notes
**G'day and welcome to Everything Windows!** VRemind is officially coming to Windows with the same energy as the Mac version. Now it's 100% feature-parity (at least the main part, I'm still figuring out the Windows widgets).

Instead of rewriting YouTube and Twitch scraping in a second language or packing Electron + Chromium just to tell you when someone goes live, `VRemind-win` snaps the exact Swift [`VRemindCore`](https://github.com/momochisoundpost/VRemindCore) engine straight into a native unpackaged **WinUI 3** system tray app — running all the way back to **Windows 10 version 1809 (LTSC 2019)** through **Windows 11**.

### Highlights & What's New

#### 🧱 Drop-In Swift `VRemindCore` + Native WinUI 3 Tray Host
- **System Tray Status & Flyout (`360×440` DIP):** Sits cleanly in the Windows notification area with solid normal/red-live antenna icons, left-click DPI-aware flyout anchored near your tray icon, and right-click context menu (`Open VRemind`, `Refresh Now`, `Settings...`, `Quit VRemind`).
- **YouTube & Twitch Support (Zero API Keys):** Add creators by YouTube `@handle`, Twitch username, or full channel URL with automatic platform detection, color-coded avatar platform badges, and bounded 3-concurrent polling that automatically pauses on sleep/lock and resumes on wake/unlock.
- **Unpackaged 1809-Compatible Toast Alerts:** Classic `ToastNotificationManager` + COM `INotificationActivationCallback` pipeline with single-instance pipe routing so alerts and click-to-open work without Microsoft Store packaging.

#### 🎉 DirectX 10 Hardware-Accelerated Celebration Mode & Reduce Motion
- **Multilingual Milestone Detection:** Detects birthday, debut, re-debut, and anniversary streams across **English, Indonesian, and Japanese** stream titles (plus the December 31 birthday surprise for `@bukancaitlyn`).
- **DirectX 10 Confetti Rain (`ConfettiOverlayControl`):** Pre-allocated shapes driven by `CompositionTarget.Rendering` and GPU-backed `CompositeTransform` (`D3D_FEATURE_LEVEL_10_0` compatible) with zero CPU layout passes per frame and zero idle CPU/GPU usage when hidden.
- **Smooth 2-Hue Shifting Rainbow Gradient & Reduce Motion:** Celebrating badges, avatar rings, timestamps, and headers smoothly shift two neighboring hues (`6.0s` cycle in normal mode; `36.0s` calm cycle with confetti disabled when **Reduce Motion** is enabled).

#### ⚙️ 3-Tab Settings, First-Run Onboarding, Backup & Native Markdown/Video Updater
- **First-Run Onboarding (`OnboardingWindow`):** Pick your starter creator, test Windows toast notifications, and choose GitHub auto-update preferences on initial launch or after factory reset.
- **3-Tab Settings (`General`, `Notifications`, `About`):** Launch at Windows login (`HKCU\...\Run`), Reduce Motion, GitHub auto-update checks (including the *"Did you steal Caitlyn's computer?"* Easter egg), portable schema-v1 **JSON Backup & Restore** (`vremind-backup-YYYY-MM-DD.json`, compatible across macOS and Windows), Factory Reset, notification diagnostics + Windows Notification Settings shortcut, and social links.
- **Native Markdown & Looping Video Updater (`UpdateAvailableWindow`):** Renders GitHub Flavored Markdown release notes and `<video>` / `.mp4` clips natively in WinUI 3 (`MarkdownViewControl` + `MediaPlayerElement`) with zero `WebView2` dependency.

### Known Scope
- **Windows 11 Desktop Widget Provider:** Deferred to a future update — core tray monitoring, alerts, celebration mode, backup/restore, onboarding, and updater are at 100% parity with macOS `VRemind` v1.3.1.
