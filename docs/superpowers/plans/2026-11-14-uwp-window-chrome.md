# UWP Window Chrome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the stock Win32 title bar and window frame on `SettingsWindow`, `OnboardingWindow`, and `UpdateAvailableWindow` with a shared UWP-style 1px border, seamless 32px title bar, and single `✕` close button across Windows 10 (1809+) and Windows 11.

**Architecture:** `UwpWindowChromePolicy` in `VRemindWin.Core` encapsulates pure Win32 style normalization (stripping Win32 caption/dialog frame bits while keeping `WS_SYSMENU` and clearing `WS_EX_TOOLWINDOW`), OS/theme/activation border resolution, and resize hit-testing. `UwpTitleBarControl` and `UwpWindowChromeController` in `VRemindWin` attach `WM_NCCALCSIZE` subclassing, DWM dark/border/shadow attributes, XAML 32px header + `46×32` `✕` close button states, and `WM_NCLBUTTONDOWN` dragging/resizing to all three standard windows.

**Tech Stack:** C# / .NET 9 (`net9.0-windows10.0.19041.0`), WinUI 3 (`Microsoft.WindowsAppSDK` 1.6), Win32 (`user32.dll`, `dwmapi.dll`, `comctl32.dll`), xUnit (`VRemindWin.Tests`).

**Spec:** `docs/superpowers/specs/2026-11-14-uwp-window-chrome-design.md`

## Global Constraints

- Target OS Matrix: Windows 10 version 1809 (`10.0.17763`)+ and Windows 11 (`x64` and `arm64`), unpackaged (`WindowsPackageType=None`, Windows App SDK `1.6`).
- Standard windows (`SettingsWindow`, `OnboardingWindow`, `UpdateAvailableWindow`) must retain `WS_SYSMENU` and must not have `WS_EX_TOOLWINDOW` set, preserving taskbar and Alt+Tab presence.
- Title bar height is `32` DIP with `12px` Segoe UI title text (`Margin="12,0,0,0"`, no corner icon) and **only** the `46×32` DIP `✕` close button (`\uE8BB`, `10px`, `#E81123` hover, `#F1707A` press).
- Windows 10 (`< 22000`) uses a `1px` outer XAML border (`0x003E3E3E` active / `0x002B2B2B` inactive dark; `0x00D6D6D6` active / `0x00E5E5E5` inactive light); Windows 11 (`>= 22000`) uses DWM `DWMWCP_ROUND` + `DWMWA_BORDER_COLOR` with XAML `RootBorderThickness = 0.0` (falling back to `1.0` if DWM fails).

## Review Focus

- `OverlappedPresenter.SetBorderAndTitleBar(false, false)` on Windows 10 sets `WS_EX_TOOLWINDOW` and leaves `WS_DLGFRAME` / `WS_EX_WINDOWEDGE`; `StripStandardWindowFrameStyles` must clear `WS_EX_TOOLWINDOW` and all dialog/border bits while keeping `WS_SYSMENU`.
- Active vs. inactive window transitions (`isWindowActive: true` vs `false`) must switch `BorderColorRef` between active and inactive palettes in both dark and light themes and toggle `IsTitleActive`.
- Windows 11 when DWM attribute calls fail (`dwmAttributesSucceeded: false`) must fall back to `RootBorderThickness == 1.0` so the window never loses its border.
- `HitTestResizeBorder` must always return `HtClient` (`1`) when `isResizable == false` (`SettingsWindow` and `OnboardingWindow`) and return corner hit codes (`HtTopLeft`, `HtBottomRight`, etc.) at corners before edge hit codes when `isResizable == true` (`UpdateAvailableWindow`).
- `UpdateAvailableWindow.UpdateRelease(...)` must keep both `_appWindow.Title` and `TitleBar.TitleText` synchronized when toggling between `"Software Update"` and reader mode (`"Calibration Notes"`).

---

### Task 1: Pure `UwpWindowChromePolicy` & Unit Tests

**Files:**
- Create: `VRemindWin/src/VRemindWin.Core/Services/UwpWindowChromePolicy.cs`
- Create: `VRemindWin/tests/VRemindWin.Tests/UwpWindowChromeTests.cs`

**Interfaces:**
- Produces:
  - `VRemindWin.Core.Services.UwpWindowChromeLayout` record struct:
    ```csharp
    public readonly record struct UwpWindowChromeLayout(
        bool UseWin11DwmChrome,
        uint CornerPreferenceValue,
        uint BorderColorRef,
        double RootCornerRadius,
        double RootBorderThickness,
        bool IsTitleActive,
        uint CloseHoverBackgroundRgb,
        uint ClosePressedBackgroundRgb);
    ```
  - `VRemindWin.Core.Services.UwpWindowChromePolicy`:
    - Constants: `DwmWaUseImmersiveDarkMode = 20`, `DwmWaUseImmersiveDarkModeLegacy = 19`, `DwmWaWindowCornerPreference = 33`, `DwmWaBorderColor = 34`, `DwmWcpRound = 2u`, `DarkActiveBorderColorRef = 0x003E3E3Eu`, `DarkInactiveBorderColorRef = 0x002B2B2Bu`, `LightActiveBorderColorRef = 0x00D6D6D6u`, `LightInactiveBorderColorRef = 0x00E5E5E5u`, `CloseHoverBackgroundRgb = 0xE81123u`, `ClosePressedBackgroundRgb = 0xF1707Au`, `TitleBarHeightDip = 32.0`, `CloseButtonWidthDip = 46.0`, `ResizeBorderThicknessDip = 5.0`, `HtClient = 1`, `HtCaption = 2`, `HtLeft = 10`, `HtRight = 11`, `HtTop = 12`, `HtTopLeft = 13`, `HtTopRight = 14`, `HtBottom = 15`, `HtBottomLeft = 16`, `HtBottomRight = 17`.
    - `public static bool IsWindows11OrGreater(int windowsBuildNumber)`
    - `public static (long Style, long ExStyle) StripStandardWindowFrameStyles(long currentStyle, long currentExStyle)`
    - `public static UwpWindowChromeLayout ResolveChromeLayout(int windowsBuildNumber, bool dwmAttributesSucceeded, bool isDarkTheme, bool isWindowActive)`
    - `public static int HitTestResizeBorder(double x, double y, double width, double height, double gripThickness, bool isResizable)`

- [ ] **Step 1: Write failing unit tests in `VRemindWin/tests/VRemindWin.Tests/UwpWindowChromeTests.cs`**

```csharp
using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class UwpWindowChromeTests
{
    private const long WsPopup = 0x80000000L;
    private const long WsDlgFrame = 0x00400000L;
    private const long WsBorder = 0x00800000L;
    private const long WsCaption = WsBorder | WsDlgFrame;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsVisible = 0x10000000L;
    private const long WsClipSiblings = 0x04000000L;

    private const long WsExDlgModalFrame = 0x00000001L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;
    private const long WsExStaticEdge = 0x00020000L;
    private const long WsExAppWindow = 0x00040000L;

    [Fact]
    public void StripStandardWindowFrameStyles_PreservesSysMenuAndRemovesToolWindowAndFrameBits()
    {
        // Observed Win10 HWND flags after OverlappedPresenter.SetBorderAndTitleBar(false, false)
        const long observedStyle = WsVisible | WsClipSiblings | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox;
        const long observedExStyle = WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExToolWindow | WsExAppWindow;

        var (newStyle, newExStyle) = UwpWindowChromePolicy.StripStandardWindowFrameStyles(observedStyle, observedExStyle);

        Assert.Equal(0L, newStyle & (WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsPopup));
        Assert.NotEqual(0L, newStyle & WsSysMenu);
        Assert.NotEqual(0L, newStyle & WsVisible);
        Assert.NotEqual(0L, newStyle & WsClipSiblings);

        Assert.Equal(0L, newExStyle & (WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExToolWindow));
        Assert.NotEqual(0L, newExStyle & WsExAppWindow);
    }

    [Theory]
    [InlineData(17763, true, true, true, false, 1.0, UwpWindowChromePolicy.DarkActiveBorderColorRef)]
    [InlineData(19044, true, true, false, false, 1.0, UwpWindowChromePolicy.DarkInactiveBorderColorRef)]
    [InlineData(19045, true, false, true, false, 1.0, UwpWindowChromePolicy.LightActiveBorderColorRef)]
    [InlineData(19045, true, false, false, false, 1.0, UwpWindowChromePolicy.LightInactiveBorderColorRef)]
    [InlineData(22000, true, true, true, true, 0.0, UwpWindowChromePolicy.DarkActiveBorderColorRef)]
    [InlineData(26100, true, false, false, true, 0.0, UwpWindowChromePolicy.LightInactiveBorderColorRef)]
    [InlineData(22631, false, true, true, false, 1.0, UwpWindowChromePolicy.DarkActiveBorderColorRef)]
    public void ResolveChromeLayout_ReturnsExpectedBorderThicknessAndActiveInactiveColors(
        int osBuild,
        bool dwmSucceeded,
        bool isDarkTheme,
        bool isWindowActive,
        bool expectWin11DwmChrome,
        double expectedBorderThickness,
        uint expectedBorderColorRef)
    {
        var layout = UwpWindowChromePolicy.ResolveChromeLayout(osBuild, dwmSucceeded, isDarkTheme, isWindowActive);

        Assert.Equal(expectWin11DwmChrome, layout.UseWin11DwmChrome);
        Assert.Equal(expectedBorderThickness, layout.RootBorderThickness);
        Assert.Equal(0.0, layout.RootCornerRadius);
        Assert.Equal(expectedBorderColorRef, layout.BorderColorRef);
        Assert.Equal(isWindowActive, layout.IsTitleActive);
        Assert.Equal(UwpWindowChromePolicy.CloseHoverBackgroundRgb, layout.CloseHoverBackgroundRgb);
        Assert.Equal(UwpWindowChromePolicy.ClosePressedBackgroundRgb, layout.ClosePressedBackgroundRgb);
    }

    [Fact]
    public void HitTestResizeBorder_ReturnsHtClientWhenNotResizable()
    {
        Assert.Equal(UwpWindowChromePolicy.HtClient, UwpWindowChromePolicy.HitTestResizeBorder(0, 0, 480, 480, 5, isResizable: false));
        Assert.Equal(UwpWindowChromePolicy.HtClient, UwpWindowChromePolicy.HitTestResizeBorder(479, 479, 480, 480, 5, isResizable: false));
    }

    [Theory]
    [InlineData(2, 2, UwpWindowChromePolicy.HtTopLeft)]
    [InlineData(478, 2, UwpWindowChromePolicy.HtTopRight)]
    [InlineData(2, 418, UwpWindowChromePolicy.HtBottomLeft)]
    [InlineData(478, 418, UwpWindowChromePolicy.HtBottomRight)]
    [InlineData(240, 2, UwpWindowChromePolicy.HtTop)]
    [InlineData(240, 418, UwpWindowChromePolicy.HtBottom)]
    [InlineData(2, 210, UwpWindowChromePolicy.HtLeft)]
    [InlineData(478, 210, UwpWindowChromePolicy.HtRight)]
    [InlineData(240, 210, UwpWindowChromePolicy.HtClient)]
    public void HitTestResizeBorder_ReturnsExpectedEdgesAndCornersWhenResizable(double x, double y, int expectedHt)
    {
        int actual = UwpWindowChromePolicy.HitTestResizeBorder(x, y, 480, 420, 5, isResizable: true);
        Assert.Equal(expectedHt, actual);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj --filter "FullyQualifiedName~UwpWindowChromeTests"`
Expected: FAIL with compiler error (`UwpWindowChromePolicy` does not exist).

- [ ] **Step 3: Implement `UwpWindowChromePolicy` in `VRemindWin/src/VRemindWin.Core/Services/UwpWindowChromePolicy.cs`**

Implement `UwpWindowChromeLayout` and `UwpWindowChromePolicy` with the exact signatures and constants listed in **Interfaces** above. In `StripStandardWindowFrameStyles`, strip `WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox` from `currentStyle` (preserving `WsSysMenu = 0x00080000L`) and strip `WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExToolWindow` from `currentExStyle`. In `HitTestResizeBorder`, return `HtClient` when `!isResizable || width <= 0 || height <= 0 || gripThickness <= 0`, otherwise check top/bottom and left/right within `gripThickness` (and `gripThickness * 2` for corner diagonal reach) to return the matching `Ht*` constant.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj --filter "FullyQualifiedName~UwpWindowChromeTests"`
Expected: PASS (all tests in `UwpWindowChromeTests` pass).

- [ ] **Step 5: Commit Task 1**

```powershell
& "C:\Program Files\Git\cmd\git.exe" add VRemindWin/src/VRemindWin.Core/Services/UwpWindowChromePolicy.cs VRemindWin/tests/VRemindWin.Tests/UwpWindowChromeTests.cs
& "C:\Program Files\Git\cmd\git.exe" commit -m "feat: add UwpWindowChromePolicy and unit tests"
```

---

### Task 2: Reusable `UwpTitleBarControl`, `UwpWindowChromeController`, and Window Integration

**Files:**
- Create: `VRemindWin/src/VRemindWin/Views/Controls/UwpTitleBarControl.cs`
- Create: `VRemindWin/src/VRemindWin/Helpers/UwpWindowChromeController.cs`
- Modify: `VRemindWin/src/VRemindWin/Views/SettingsWindow.xaml`
- Modify: `VRemindWin/src/VRemindWin/Views/SettingsWindow.xaml.cs`
- Modify: `VRemindWin/src/VRemindWin/Views/OnboardingWindow.xaml`
- Modify: `VRemindWin/src/VRemindWin/Views/OnboardingWindow.xaml.cs`
- Modify: `VRemindWin/src/VRemindWin/Views/UpdateAvailableWindow.xaml`
- Modify: `VRemindWin/src/VRemindWin/Views/UpdateAvailableWindow.xaml.cs`

**Interfaces:**
- Consumes: `UwpWindowChromePolicy` and `UwpWindowChromeLayout` from Task 1.
- Produces:
  - `VRemindWin.Views.Controls.UwpTitleBarControl : Grid`:
    - `public string TitleText { get; set; }`
    - `public event Action? DragRequested;`
    - `public event Action? CloseRequested;`
    - `public void SetActiveState(bool isActive)`
  - `VRemindWin.Helpers.UwpWindowChromeController : IDisposable`:
    - `public UwpWindowChromeController(Window window, IntPtr hwnd, AppWindow appWindow, Grid chromeBorderGrid, UwpTitleBarControl titleBar, bool isResizable, int minWidthDip = 360, int minHeightDip = 300)`

- [ ] **Step 1: Create `UwpTitleBarControl` in `VRemindWin/src/VRemindWin/Views/Controls/UwpTitleBarControl.cs`**

Build a code-behind XAML `Grid` (`Height = UwpWindowChromePolicy.TitleBarHeightDip`, `Background = Transparent`) with two columns (`*` and `Auto`):
- Column 0 (`_dragRegion` `Grid`, `Background = Transparent`): hosts `_titleTextBlock` (`FontSize = 12`, `Margin = new Thickness(12, 0, 0, 0)`, `VerticalAlignment = VerticalAlignment.Center`, `TextTrimming = TextTrimming.CharacterEllipsis`, `IsHitTestVisible = false`). On `_dragRegion.PointerPressed`, if `e.GetCurrentPoint(_dragRegion).Properties.IsLeftButtonPressed`, mark `e.Handled = true` and invoke `DragRequested?.Invoke()`.
- Column 1 (`_closeButtonBorder` `Border`, `Width = UwpWindowChromePolicy.CloseButtonWidthDip`, `Height = UwpWindowChromePolicy.TitleBarHeightDip`, `Background = Transparent`): hosts `_closeGlyph` (`FontIcon` with `FontFamily = new FontFamily("Segoe MDL2 Assets")`, `Glyph = "\uE8BB"`, `FontSize = 10`, centered). Handle `PointerEntered`, `PointerExited`, `PointerPressed`, `PointerReleased`, `PointerCanceled`, and `ActualThemeChanged` so:
  - Idle: `Background = Transparent`, `_closeGlyph.Foreground` = active/inactive foreground brush (`TextFillColorPrimaryBrush` when `_isActive`, `TextFillColorSecondaryBrush` when inactive).
  - Hover: `Background = #FFE81123`, `_closeGlyph.Foreground = White`.
  - Pressed: `Background = #FFF1707A`, `_closeGlyph.Foreground = White`.
  - Releasing left button while pointer is over `_closeButtonBorder` invokes `CloseRequested?.Invoke()`.

- [ ] **Step 2: Create `UwpWindowChromeController` in `VRemindWin/src/VRemindWin/Helpers/UwpWindowChromeController.cs`**

Implement `UwpWindowChromeController`:
- Configure `appWindow.Presenter as OverlappedPresenter`: set `IsResizable = false`, `IsMaximizable = false`, `IsMinimizable = false`, and `SetBorderAndTitleBar(false, false)`.
- Attach `SetWindowSubclass` handling:
  - `WM_NCCALCSIZE` (`0x0083`) when `wParam != IntPtr.Zero`: return `IntPtr.Zero`.
  - `WM_ERASEBKGND` (`0x0014`): return `new IntPtr(1)`.
  - `WM_GETMINMAXINFO` (`0x0024`): when `_isResizable` is true, populate `MINMAXINFO.ptMinTrackSize` with DPI-scaled `(_minWidthDip, _minHeightDip)`.
  - `WM_NCDESTROY` (`0x0082`): detach subclass.
- Apply `UwpWindowChromePolicy.StripStandardWindowFrameStyles` via `GetWindowLongPtrW` / `SetWindowLongPtrW` and `SetWindowPos(..., SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED)`.
- On `chromeBorderGrid.Loaded`, `chromeBorderGrid.ActualThemeChanged`, and `window.Activated`, call `ApplyChromeForCurrentState()`:
  - Call `DwmExtendFrameIntoClientArea(_hwnd, ref shadowMargins)` (`{ 0, 0, 0, 1 }`).
  - Set `DWMWA_USE_IMMERSIVE_DARK_MODE` (`20`, fallback `19`) to `isDarkTheme ? 1u : 0u`.
  - If `UwpWindowChromePolicy.IsWindows11OrGreater(osBuild)`, set `DWMWA_WINDOW_CORNER_PREFERENCE` (`DWMWCP_ROUND`) and `DWMWA_BORDER_COLOR` (`candidate.BorderColorRef`).
  - Resolve layout via `UwpWindowChromePolicy.ResolveChromeLayout(osBuild, dwmApplied, isDarkTheme, _isWindowActive)` and apply `CornerRadius`, `BorderThickness`, and `BorderBrush` on `chromeBorderGrid`, plus `_titleBar.SetActiveState(layout.IsTitleActive)`.
- Wire `_titleBar.DragRequested` to `ReleaseCapture()` + `SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)UwpWindowChromePolicy.HtCaption, IntPtr.Zero)` and `_titleBar.CloseRequested` to `_window.Close()`.
- When `isResizable == true`, add 8 transparent resize-grip `Border` overlays (`5px` thick along the 4 edges and `10×10` at the 4 corners, `Canvas.ZIndex = 100`, leaving the `46×32` top-right close button unobstructed) on `chromeBorderGrid` (`Grid.RowSpan = 2`) that set the corresponding `InputSystemCursorShape` (`SizeWestEast`, `SizeNorthSouth`, `SizeNorthwestSoutheast`, `SizeNortheastSouthwest`) via recruitment of `ProtectedCursor` or Win32 `WM_SETCURSOR` / `SetCursor(LoadCursor(IntPtr.Zero, idc_*))` and on left `PointerPressed` call `ReleaseCapture()` + `SendMessage(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)htCode, IntPtr.Zero)`.

- [ ] **Step 3: Integrate UWP chrome into `SettingsWindow`, `OnboardingWindow`, and `UpdateAvailableWindow`**

- `SettingsWindow.xaml` & `SettingsWindow.xaml.cs`:
  - Wrap `RootGrid` inside an outer `Grid x:Name="WindowChromeRoot"` (`Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"`, `BackgroundSizing="OuterBorderEdge"`, with Row 0 = `Auto` for `<controls:UwpTitleBarControl x:Name="WindowTitleBar" TitleText="VRemind Settings" />` and Row 1 = `*` containing `RootGrid` with `Padding="18,6,18,14"`).
  - In `SettingsWindow.xaml.cs`, instantiate `_chromeController = new UwpWindowChromeController(this, _hwnd, _appWindow, WindowChromeRoot, WindowTitleBar, isResizable: false)` and dispose it on `Closed`.
- `OnboardingWindow.xaml` & `OnboardingWindow.xaml.cs`:
  - Wrap the content `Grid` inside `Grid x:Name="WindowChromeRoot"` (`Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"`, `BackgroundSizing="OuterBorderEdge"`, Row 0 = `<controls:UwpTitleBarControl x:Name="WindowTitleBar" TitleText="Welcome to VRemind" />`, Row 1 = `*` containing the existing content `Grid` with `Padding="24,8,24,20"`).
  - In `OnboardingWindow.xaml.cs`, instantiate `_chromeController = new UwpWindowChromeController(this, _hwnd, _appWindow, WindowChromeRoot, WindowTitleBar, isResizable: false)` and dispose it on `Closed`.
- `UpdateAvailableWindow.xaml` & `UpdateAvailableWindow.xaml.cs`:
  - Wrap the content `Grid` inside `Grid x:Name="WindowChromeRoot"` (`Background="{ThemeResource SolidBackgroundFillColorBaseBrush}"`, `BackgroundSizing="OuterBorderEdge"`, Row 0 = `<controls:UwpTitleBarControl x:Name="WindowTitleBar" TitleText="Software Update" />`, Row 1 = `*` containing the existing content `Grid` with `Padding="20,8,20,16"`).
  - In `UpdateAvailableWindow.xaml.cs`, instantiate `_chromeController = new UwpWindowChromeController(this, _hwnd, _appWindow, WindowChromeRoot, WindowTitleBar, isResizable: true)` and dispose it on `Closed`. In `UpdateRelease(...)`, set `WindowTitleBar.TitleText = chrome.WindowTitle;`.

- [ ] **Step 4: Build and run the full test suite**

Run:
```powershell
dotnet test VRemindWin\VRemindWin.sln -c Debug
dotnet build VRemindWin\VRemindWin.sln -c Debug -p:Platform=x64
```
Expected: All xUnit tests pass (`0 Failed`), and solution builds with `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 5: Commit Task 2**

```powershell
& "C:\Program Files\Git\cmd\git.exe" add VRemindWin/src/VRemindWin/
& "C:\Program Files\Git\cmd\git.exe" commit -m "feat: apply shared UWP window border and title bar across app windows"
```
