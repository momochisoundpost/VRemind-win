# VRemind for Windows (`VRemind-win`) UWP Window Chrome Design

- **Date:** 2026-11-14
- **Scope:** Small Change (Shared UWP-style 1px border, seamless 32px title bar, and single `✕` close button across `SettingsWindow`, `OnboardingWindow`, and `UpdateAvailableWindow`)

---

## 1. Intent and Why
On Windows 10 and Windows 11, `SettingsWindow`, `OnboardingWindow`, and `UpdateAvailableWindow` currently use the default WinUI 3 / Win32 overlapped window frame—resulting in a bright white Win32 title bar, a 16×16 corner icon, and a legacy Win32 frame edge that clashes with the dark UWP-style client canvas. Meanwhile, `PopoverWindow` already achieves a native, seamless border using `PopoverWindowChromePolicy`. Replacing the Win32 frame on all three standard windows with a shared UWP-style 1px border and integrated 32px title bar makes `VRemind-win` feel like a first-party UWP app (matching the Windows Settings app).

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
- **Shared UWP Window Frame Across All 3 Standard Windows:** Apply consistent UWP chrome to `SettingsWindow` (`480×480` DIP, fixed), `OnboardingWindow` (`460×520` DIP, fixed), and `UpdateAvailableWindow` (`480×420` DIP, resizable).
- **Seamless 32px Title Bar with Close (`✕`) Only:**
  - Full-bleed `SolidBackgroundFillColorBaseBrush` surface up to the top edge (zero white Win32 caption bar or 1px white top strip).
  - Left-aligned `12px` Segoe UI title text (`12px` left margin, no corner icon) that dims when the window is inactive.
  - Single `46×32` DIP UWP Close (`✕`) button (`Segoe MDL2 Assets` `\uE8BB`, `10px`) on the right—transparent at rest, `#E81123` red with white glyph on hover, and `#F1707A` with white glyph on press.
- **OS-Appropriate Crisp 1px UWP Border:**
  - **Windows 10 (`< 22000`):** Crisp `1px` outer XAML border (`#3E3E3E` active / `#2B2B2B` inactive in dark mode; `#D6D6D6` active / `#E5E5E5` inactive in light mode), `CornerRadius = 0`, paired with DWM shadow (`DwmExtendFrameIntoClientArea` + `DWMWA_USE_IMMERSIVE_DARK_MODE`).
  - **Windows 11 (`>= 22000`):** Native DWM rounded corners (`DWMWCP_ROUND`) and 1px DWM border (`DWMWA_BORDER_COLOR`), with XAML `RootBorderThickness = 0` (falling back to the 1px XAML border if DWM attributes fail).
- **Native Window Dragging & Resizing:** Dragging the 32px title bar moves the window (with native Windows snap support), and dragging edges/corners on `UpdateAvailableWindow` resizes it smoothly.

### Non-Goals
- **Minimize or Maximize Caption Buttons:** Explicitly excluded; all three windows display only the `✕` close button.
- **Redesigning Internal Window Content:** Existing tabs, cards, and controls inside `SettingsWindow`, `OnboardingWindow`, and `UpdateAvailableWindow` stay unchanged.

### Anti-Goals
- **Residual Win32 Caption/Border Bleed on Windows 10:** Leaving a 1px white top line, a 3px `WS_DLGFRAME` / `WS_EX_WINDOWEDGE` rim, or disabled grayed-out Min/Max buttons (WinUI 3 issue `#7628`) is a failure.
- **Hiding Standard Windows from Taskbar / Alt+Tab:** Unlike `PopoverWindow` (which sets `WS_EX_TOOLWINDOW`), standard windows must retain `WS_SYSMENU` and normal taskbar/Alt+Tab visibility.

---

## 3. Constraints
- **Target OS Matrix:** Windows 10 version 1809 (`10.0.17763`)+ and Windows 11 (`x64` and `arm64`), unpackaged (`WindowsPackageType=None`, Windows App SDK `1.6`).
- **Testability:** Pure style-stripping, DWM border/corner resolution, and hit-test classification logic must live in `VRemindWin.Core` (`UwpWindowChromePolicy`) and be covered by xUnit tests in `VRemindWin.Tests`.

---

## 4. Approaches Considered

1. **Shared XAML UWP Frame + Win32 Non-Client Subclass *(Selected)*:** Strip the Win32 non-client caption/border via `SetBorderAndTitleBar(false, false)`, style-bit normalization, and `WM_NCCALCSIZE`, render the `32px` UWP title bar and `46×32` `✕` button in a reusable XAML control/helper, route title-bar drag and edge-resize via Win32 `WM_NCLBUTTONDOWN`, and apply the 1px XAML border on Windows 10 or DWM rounded border on Windows 11. *Why chosen:* Guarantees zero white top-border artifacts on Windows 10, exact UWP `✕` hover states, and strictly only the Close button across all Windows builds.
2. **`AppWindow.TitleBar.ExtendsContentIntoTitleBar` + OS Caption Button:** Rely on `AppWindowTitleBar` and `OverlappedPresenter` (`IsMinimizable = false`, `IsMaximizable = false`) plus `DWMWA_USE_IMMERSIVE_DARK_MODE`. *Trade-off:* Less Win32 interop, but `AppWindowTitleBar` button-color APIs are ignored on Windows 10, top non-client 1px border colors remain under Win32 control, and hiding Min/Max buttons with extended title bars is buggy in WinUI 3.
3. **Native Win32 Title Bar with `DWMWA_USE_IMMERSIVE_DARK_MODE` Only:** Keep the stock Win32 caption bar and only set dark mode. *Rejected:* Keeps the 16×16 corner icon and Win32 caption geometry.

---

## 5. Decided Design & Builder Calls

- **Pure Chrome Policy (`VRemindWin.Core/Services/UwpWindowChromePolicy.cs`) *(Builder call)*:**
  - `StripStandardWindowFrameStyles(long currentStyle, long currentExStyle)`: Removes `WS_CAPTION` (`WS_BORDER | WS_DLGFRAME`), `WS_THICKFRAME`, `WS_MINIMIZEBOX`, `WS_MAXIMIZEBOX` and `WS_EX_DLGMODALFRAME`, `WS_EX_WINDOWEDGE`, `WS_EX_CLIENTEDGE`, `WS_EX_STATICEDGE` while **preserving `WS_SYSMENU`** and **not** adding `WS_EX_TOOLWINDOW` (so taskbar and Alt+Tab work normally).
  - `ResolveChromeLayout(int windowsBuildNumber, bool dwmAttributesSucceeded, bool isDarkTheme, bool isWindowActive)`: Returns `UseWin11DwmChrome`, `CornerPreferenceValue` (`DWMWCP_ROUND`), active/inactive `BorderColorRef` (`0x003E3E3E` / `0x002B2B2B` dark, `0x00D6D6D6` / `0x00E5E5E5` light), `RootBorderThickness` (`0.0` on Win11 when DWM succeeds, `1.0` on Win10 or fallback), and title/button opacity/color state.
  - `HitTestResizeBorder(double x, double y, double width, double height, double borderThickness, bool isResizable)`: Pure helper returning the Win32 `HT*` code (`HTLEFT`, `HTRIGHT`, `HTTOP`, `HTBOTTOM`, `HTTOPLEFT`, `HTTOPRIGHT`, `HTBOTTOMLEFT`, `HTBOTTOMRIGHT`, or `HTCLIENT`) for `UpdateAvailableWindow`.
- **Reusable XAML Title Bar & Chrome Controller (`VRemindWin/Views/Controls/UwpTitleBarControl.cs` & `VRemindWin/Helpers/UwpWindowChromeController.cs`) *(Builder call)*:**
  - `UwpTitleBarControl`: `32px` height header with `12px` Segoe UI title text on the left (`Margin="12,0,0,0"`), draggable background region (`ReleaseCapture` + `SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0)` on left-button press), and the `46×32` `✕` button on the right with UWP hover (`#E81123`) and press (`#F1707A`) visual states.
  - `UwpWindowChromeController`: Attaches to a `Window` (`SettingsWindow`, `OnboardingWindow`, `UpdateAvailableWindow`), configures `OverlappedPresenter.SetBorderAndTitleBar(false, false)`, attaches the `WM_NCCALCSIZE` / `WM_ERASEBKGND` subclass, applies `DwmExtendFrameIntoClientArea` (`0, 0, 0, 1`), `DWMWA_USE_IMMERSIVE_DARK_MODE` (`20` with `19` fallback), and Win11 `DWMWA_WINDOW_CORNER_PREFERENCE` / `DWMWA_BORDER_COLOR`, updates active/inactive border and title state on `Window.Activated`, and handles optional edge-resize dragging when `isResizable: true`.
- **Window Integration (`SettingsWindow`, `OnboardingWindow`, `UpdateAvailableWindow`) *(User + Builder call)*:**
  - Wrap each window's root `Grid` in the 1px-capable outer border with `UwpTitleBarControl` in Row 0 (`Height="32"`) and existing content in the rows below.
  - Keep `UpdateAvailableWindow.UpdateRelease(...)` synced so changing `_appWindow.Title` also updates the `UwpTitleBarControl.TitleText`.
