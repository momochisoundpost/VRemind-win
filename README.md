# VRemind for Windows (`VRemind-win`)

<p align="center">
  <img src="VRemindWin/src/VRemindWin/Assets/app-icon.png" alt="VRemind App Icon" width="96">
</p>

<p align="center">
  <a href="https://microsoft.com/windows"><img src="https://img.shields.io/badge/Platform-Windows%2010%20(1809%2B)%20%26%2011-0078D6?logo=windows" alt="Platform: Windows 10 1809+ & 11"></a>
  <a href="https://learn.microsoft.com/windows/apps/winui/winui3/"><img src="https://img.shields.io/badge/UI-WinUI%203%20%2F%20.NET%209-512BD4?logo=dotnet" alt="UI: WinUI 3 / .NET 9"></a>
  <a href="https://github.com/momochisoundpost/VRemindCore"><img src="https://img.shields.io/badge/Engine-Swift%20VRemindCore-F05138?logo=swift" alt="Engine: Swift VRemindCore"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-green" alt="License: MIT"></a>
</p>

A native Windows 10 (1809 LTSC and newer) & Windows 11 system tray VTuber stream reminder built with **WinUI 3** and powered by [`VRemindCore`](https://github.com/momochisoundpost/VRemindCore) in Swift as a drop-in Lego-piece engine.

> [!NOTE]
> **Escaping the Californian Walled Garden & Moving Out to Seattle:** After building [`VRemind`](https://github.com/momochisoundpost/VRemind) in the sunny Cupertino walled garden—where a hobby menu bar utility gets treated like contraband unless you pay a $99/year landlord fee—VRemind has packed its bags, left California, and moved out to rainy Seattle. No Microsoft Store lock-in, no MSIX certificate headaches, and first-class support for Windows 10 LTSC boxes that refuse to die.

---

### 🎧 Vibecoded with Love (Seattle Edition)

> *"Why did I vibecode this? Because I graduated from film school doing sound. What do you expect from someone who did film sound to write world-changing code from scratch? That's dumb."*

This project is built and vibecoded using **Google Antigravity** and **[Superpowers](https://github.com/obra/superpowers)** — taking the battle-tested Swift [`VRemindCore`](https://github.com/momochisoundpost/VRemindCore) engine from the Mac app and snapping it into a native **WinUI 3** system tray frontend on a **Windows 10 IoT Enterprise LTSC 2021** devbox (even through a real-life Wi-Fi outage on mobile data). Because `VRemindCore` runs unchanged as a drop-in companion engine, every upstream YouTube or Twitch scraping fix immediately powers both the Mac menu bar app and the Windows tray app like a Lego piece.

---

VRemind sits quietly in your Windows system tray (notification area), monitors your favorite VTubers and content creators on **YouTube** and **Twitch**, alerts you with native Windows toast notifications the moment they go live or schedule an upcoming waiting room, and lets you jump straight into the stream with a single click.

---

## Key Features (v1.3.1 Feature Parity)

- **Drop-In `VRemindCore` Engine (YouTube & Twitch)**:
  - Powered directly by [`VRemindCore`](https://github.com/momochisoundpost/VRemindCore) compiled natively for Windows (`vremind-core.exe`).
  - Add any VTuber or creator by YouTube `@handle` (e.g. `@bukancaitlyn`, `@filian`, `@ironmouse`) or Twitch username (`ironmouse`), or paste a full `youtube.com` / `twitch.tv` URL for automatic platform detection.
  - Platform dropdown on the left of the input bar with color-coded badges (red for YouTube, purple for Twitch) so creators on both platforms stay clearly separated.
  - Pre-seeded with **Caitlyn Momochi** ([@bukancaitlyn](https://www.youtube.com/@bukancaitlyn)) on YouTube as the default starter channel (fully editable and removable).

- **Live & Upcoming Stream Detection**:
  - **Zero API Keys Required**: Lightweight YouTube `/live` HTML parsing (`YouTubeStreamProvider`) and key-free Twitch public web GraphQL checking (`TwitchStreamProvider`) extract stream titles, IDs, start times, and channel avatars without requiring API keys or developer accounts.
  - **Rich Status Tracking**:
    - `🔴 LIVE`: Highlights active broadcasts with title, elapsed uptime, and direct watch link.
    - `🗓️ UPCOMING`: Shows scheduled waiting rooms and stream start times (e.g., `in 45m`).
    - `Offline`: Displays time elapsed since the channel last streamed.
  - **Smart Concurrency & Power Management**: Concurrency is bounded to a maximum of 3 concurrent network requests to prevent rate limiting. Background polling automatically pauses when Windows enters sleep or the session locks, and resumes immediately upon wake/unlock.
  - **System Tray Icon**: Displays a clean antenna icon when all channels are offline and switches to a steady red icon (`VRemind (Live now)`) when any watched channel goes live.

- **DirectX 10 Hardware-Accelerated Celebration Mode**:
  - Automatically detects birthday, debut, and anniversary streams across **English, Indonesian, and Japanese** stream titles (plus a special December 31 birthday celebration for `@bukancaitlyn`).
  - Renders vsync-locked confetti rain via `CompositionTarget.Rendering` and GPU-backed `CompositeTransform` on the DirectX 10 (`D3D_FEATURE_LEVEL_10_0`) DirectComposition layer with zero CPU layout passes per frame and zero idle CPU/GPU usage when the flyout is closed.
  - Animates calm 2-color shifting-hue rainbow gradients (`6.0s` normal cycle, `36.0s` under **Reduce Motion**) across celebrating badges, avatar rings, time labels, and section headers while disabling confetti rain when **Reduce Motion** is enabled.

- **Native WinUI 3 Taskbar Flyout (`360×440` DIP) & First-Run Onboarding**:
  - Left-clicking the tray icon toggles a borderless, DPI-aware `360×440` WinUI 3 flyout anchored cleanly above the Windows taskbar that auto-dismisses when focus is lost.
  - Right-clicking the tray icon opens a quick native context menu (`Open VRemind`, `Refresh Now`, `Settings...`, `Quit VRemind`).
  - First-run **Onboarding Wizard** (`OnboardingWindow`) lets you pick your first creator, verify Windows toast notifications, and configure software update checks.

- **Native Clickable Windows Toast Notifications (1809 LTSC Compatible)**:
  - Built on the classic WinRT `ToastNotificationManager` + COM `INotificationActivationCallback` pipeline so unpackaged toast notifications work all the way back to **Windows 10 version 1809 (LTSC 2019)** without needing the Microsoft Store.
  - Independent toggles for live broadcasts (`🔴 LIVE`), scheduled waiting rooms (`⏳ UPCOMING`), and notification sounds (`Play notification sound`).
  - Smart deduplication by video ID prevents duplicate alerts for the same stream.
  - Click-to-open: Clicking a toast launches the stream directly in your default web browser while enforcing single-instance routing so you never get duplicate tray icons.

- **Full 3-Tab Settings, Portable JSON Backup & Native Markdown/Video Updater**:
  - **General Tab**: Launch at Windows login (`HKCU\...\Run`), Reduce Motion toggle, GitHub software update checks (with the `"Did you steal Caitlyn's computer?"` Easter egg), portable **JSON Backup & Restore** (`vremind-backup-YYYY-MM-DD.json`, interchangeable between macOS and Windows), and a **Factory Reset** danger zone.
  - **Notifications Tab**: Granular alert toggles, **Send Test Notification** button, and direct link to Windows Notification Settings (`ms-settings:notifications`).
  - **About Tab**: Version info, attribution & open-source fork copy, and quick social links (`YouTube`, `Twitter/X`, `GitHub`, `Instagram`).
  - **Native Markdown & Video Release Notes (`UpdateAvailableWindow`)**: Renders GitHub Flavored Markdown (headings, blockquotes, checklists, GFM tables, code blocks, images) and inline looping `<video>` / `.mp4` release notes using 100% native WinUI 3 XAML and `MediaPlayerElement` with **zero WebView2 dependency**.

---

## Installation

### Option 1: Download Pre-built Release (Unpackaged Portable / Installer)

1. Download `VRemindWin-x64.zip` from the Releases page.
2. Extract the folder anywhere you like and run `VRemindWin.exe`.

> [!NOTE]
> **Windows SmartScreen Notice**: Just like we refused to pay John Apple $99/year in California, we aren't paying a certificate cartel $300/year in Seattle to sign a free open-source VTuber reminder. If Windows Defender SmartScreen says *"Windows protected your PC"*, click **More info** → **Run anyway**.

### Option 2: Build from Source

Requirements: Windows 10 21H2+ (or 1809+) x64 with **.NET 9 SDK**, **Visual Studio 2022 C++ Build Tools + Windows 10 SDK**, and **Swift for Windows**.

```powershell
# 1. Check installed prerequisites offline (0 MB network usage — safe for mobile data!)
powershell -ExecutionPolicy Bypass -File .\scripts\check-toolchain.ps1

# 2. Install any missing SDKs/toolchains (requires -ConfirmDownload when on Wi-Fi)
powershell -ExecutionPolicy Bypass -File .\scripts\install-toolchain.ps1 -ConfirmDownload

# 3. Build the Swift VRemindCore CLI + Self-Contained Unpackaged WinUI 3 App
powershell -ExecutionPolicy Bypass -File .\scripts\build-app.ps1

# 4. Launch VRemind for Windows
.\dist\VRemindWin\VRemindWin.exe
```

---

## Quick Start & Usage

1. **System Tray Status**: Look for the VRemind antenna icon in your Windows system tray (click the `^` overflow chevron and drag it onto the taskbar if Windows hides new icons). If any watched channel goes live, the icon turns solid red.
2. **Add a Creator**: Left-click the tray icon to open the flyout popover, choose **YouTube** or **Twitch** from the left dropdown, enter a handle/username (e.g. `@bukancaitlyn`, `ironmouse`) or paste a channel URL, and press Enter or click **Add**.
3. **Watch a Stream**: Click on any channel row or Windows toast notification to open the stream directly in your default browser.
4. **Remove a Channel**: Hover over a channel row and click the trash can icon.
5. **Settings**: Click the gear icon (`⚙`) in the popover header or right-click the tray icon and select **Settings...** to customize notifications and launch-at-login behavior.
6. **Quit**: Click the power icon (`⏻`) in the popover header or right-click the tray icon and select **Quit VRemind**.

---

## Frequently Asked Questions (FAQ)

### Why are you supporting Windows 10 1809 (LTSC 2019) and 21H2 (IoT LTSC 2021)?
Because on the Mac side I refused to drop macOS 12 Monterey for OCLP and glowing-Apple-logo MacBook users, and on the Seattle side **I will never drop support for something Microsoft still supports**. Windows 10 Enterprise LTSC 2019 (1809, build `17763`) is supported until 2029, and Windows 10 IoT Enterprise LTSC 2021 (21H2, build `19044`) is supported until 2032. LTSC machines strip out the Microsoft Store and consumer bloatware by design—so `VRemind-win` is built from day one as an **unpackaged** WinUI 3 app with a 1809-compatible COM toast pipeline that needs zero Store infrastructure.

### Why was this made using WinUI 3?
Because **WinUI 3 is the Windows equivalent of SwiftUI**—modern, declarative, and genuinely native to Windows without Electron or WebView bloat. Just like the Mac app uses SwiftUI + AppKit for a clean native menu bar experience, `VRemind-win` uses WinUI 3 + Win32 tray interop to feel right at home on Windows 10 (1809+) and Windows 11.

### Why use Swift `VRemindCore` inside a Windows WinUI 3 app?
Because maintaining two separate YouTube HTML scrapers and Twitch GraphQL parsers in two different languages is a trap. By keeping [`VRemindCore`](https://github.com/momochisoundpost/VRemindCore) in pure cross-platform Swift (`Foundation` + `FoundationNetworking`) and compiling it into a headless companion binary (`vremind-core.exe`), every backend update drops into both `VRemind` (macOS) and `VRemind-win` (Windows) like a Lego piece.

### Why is the file size so much bigger than the Mac app?
Honestly? **Because I suck at Windows development (Swift on Mac is my mother tongue)** — and I wanted to make 100% sure you don't have to install any external redistributables just to launch the app. Instead of making you hunt down separate `.NET 9`, `Windows App SDK`, and `Swift for Windows` runtime installers (especially on stripped-down Windows 10 LTSC installs), the release bundle ships everything **self-contained** inside one folder so you can just unzip and run `VRemindWin.exe`. I'll optimize and trim the size down sooner or later!

### Do I need a YouTube or Twitch API key?
**No.** VRemind requires zero API keys, no Google Cloud developer projects, and no Twitch OAuth credentials. It checks YouTube directly via publicly accessible `/live` pages and checks Twitch via public unauthenticated web queries.

### Does this hog RAM or CPU in the background?
**Not at all.**
- Network polling runs once every 3 minutes via `vremind-core.exe`, throttled to a maximum of 3 concurrent requests.
- When Windows enters sleep or you lock your screen, polling pauses automatically and resumes when you return.
- All watchlist and settings state lives in clean, human-readable JSON files under `%APPDATA%\VRemind\`.

### Can I remove Caitlyn Momochi from my watchlist?
**Yes.** Caitlyn Momochi is just the default starter channel so the list isn't blank on first launch. Hover over the channel row, click the trash can icon, and add whoever you like.

---

## Architecture Overview

`VRemind-win` pairs an unpackaged C# `.NET 9` **WinUI 3** system tray host (`VRemindWin`) with the cross-platform Swift [`VRemindCore`](https://github.com/momochisoundpost/VRemindCore) engine compiled as a headless companion CLI (`VRemindCoreCLI` / `vremind-core.exe`):

```text
VRemind-win/
├── VRemindCore/                  # Shared pure-Foundation Swift engine
├── VRemindCoreCLI/               # Headless Swift companion CLI (vremind-core.exe)
│   ├── Package.swift             # Links directly against ../VRemindCore
│   ├── Sources/VRemindCoreCLI/   # CLI entrypoint
│   ├── Sources/VRemindCoreCLIKit/# JSON commands: state, refresh, add, remove, settings, backup, onboarding, check-update
│   └── Tests/VRemindCoreCLITests/
├── VRemindWin/                   # Native C# .NET 9 Unpackaged WinUI 3 Tray Application
│   ├── VRemindWin.sln
│   ├── src/VRemindWin.Core/      # Coordinators, view models, Markdown parser, gradient math, toast XML & Win32 helpers
│   ├── src/VRemindWin/
│   │   ├── Program.cs            # Single-instance mutex & 1809 AUMID/COM bootstrap
│   │   ├── Services/             # TrayIconService, NativeFileDialogService
│   │   └── Views/                # PopoverWindow, PopoverView, SettingsWindow, OnboardingWindow, UpdateAvailableWindow & Controls/
│   └── tests/VRemindWin.Tests/   # xUnit test suite
├── docs/
│   └── superpowers/              # Design specs and implementation plans
└── scripts/
    ├── check-toolchain.ps1       # Zero-network prerequisite & download-size checker
    ├── install-toolchain.ps1     # Data-gated (-ConfirmDownload) SDK & toolchain installer
    ├── generate-icons.ps1        # Multi-size .ico generator (app-icon, tray-normal, tray-live)
    ├── build-cli.ps1             # Builds vremind-core.exe + stages Swift Windows runtime DLLs
    └── build-app.ps1             # Packages self-contained dist/VRemindWin/ bundle
```

---

## Requirements

- **Runtime OS**: Windows 10 version 1809 (build `17763`, Enterprise LTSC 2019) or newer, and Windows 11 (x64).
- **Devbox / Contributor Build OS**: Windows 10 21H2 (build `19044`, IoT Enterprise LTSC 2021) or newer with `.NET 9 SDK`, `Visual Studio 2022 C++ Build Tools + Windows 10 SDK`, and `Swift for Windows`.

> [!NOTE]
> **Windows 11 Widgets:** Windows 11 Desktop Widget Provider support is planned as a separate future milestone — the core system tray app, notifications, celebration mode, onboarding, backup/restore, and updater are already at 100% parity with macOS `VRemind` v1.3.1.

---

## Running Tests

Run the Swift companion CLI tests (from a Visual Studio x64 Native Tools Command Prompt), C# xUnit suite, and toolchain script checks:

```powershell
# Swift VRemindCoreCLI tests (run from VS 2022 x64 Native Tools Command Prompt on Windows)
swift test --package-path VRemindCoreCLI -Xcc -D__MM_MALLOC_H

# C# WinUI 3 coordinator, view model, Markdown parser, gradient, toast XML, and flyout positioning tests
dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj

# Offline toolchain verification scripts
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\tests\verify-toolchain-scripts.ps1
```

---

## Disclaimer

- **Platform Notice**: VRemind is an independent open-source project and is not affiliated with, endorsed by, or associated with YouTube, Google LLC, Twitch Interactive, Inc., Apple Inc., or Microsoft Corporation.
- **Public Scraping Notice**: VRemind checks streams using publicly accessible YouTube `/live` pages and public unauthenticated Twitch web queries without an API key. Upstream platforms may change their markup or payloads from time to time. If stream statuses or avatars fail to parse after a platform update, please open an issue or pull request.

---

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

*"You don't like what I did? Go fork yourself, it's free and open source."*
