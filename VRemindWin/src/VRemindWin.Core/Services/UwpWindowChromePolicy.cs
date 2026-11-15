namespace VRemindWin.Core.Services;

public readonly record struct UwpWindowChromeLayout(
    bool UseWin11DwmChrome,
    uint CornerPreferenceValue,
    uint BorderColorRef,
    double RootCornerRadius,
    double RootBorderThickness,
    bool IsTitleActive,
    uint CloseHoverBackgroundRgb,
    uint ClosePressedBackgroundRgb);

/// <summary>
/// Pure Win32 style normalization, DWM/XAML border layout, and resize hit-testing policy
/// for standard application windows styled with native UWP chrome.
/// </summary>
public static class UwpWindowChromePolicy
{
    public const int DwmWaUseImmersiveDarkMode = 20;
    public const int DwmWaUseImmersiveDarkModeLegacy = 19;
    public const int DwmWaWindowCornerPreference = 33;
    public const int DwmWaBorderColor = 34;

    public const uint DwmWcpRound = 2u;

    // Win32 COLORREF (0x00BBGGRR) matching UWP active/inactive 1px frame borders in Dark and Light themes.
    public const uint DarkActiveBorderColorRef = 0x003E3E3Eu;
    public const uint DarkInactiveBorderColorRef = 0x002B2B2Bu;
    public const uint LightActiveBorderColorRef = 0x00D6D6D6u;
    public const uint LightInactiveBorderColorRef = 0x00E5E5E5u;

    public const uint CloseHoverBackgroundRgb = 0xE81123u;
    public const uint ClosePressedBackgroundRgb = 0xF1707Au;

    public const double TitleBarHeightDip = 32.0;
    public const double CloseButtonWidthDip = 46.0;
    public const double ResizeBorderThicknessDip = 5.0;

    public const int HtClient = 1;
    public const int HtCaption = 2;
    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;

    private const long WsCaption = 0x00C00000L;      // WS_BORDER | WS_DLGFRAME
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;

    private const long WsExDlgModalFrame = 0x00000001L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;
    private const long WsExStaticEdge = 0x00020000L;

    private const long FrameStyleMask = WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox;
    private const long FrameExStyleMask = WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExToolWindow;

    public static bool IsWindows11OrGreater(int windowsBuildNumber)
        => windowsBuildNumber >= 22000;

    public static (long Style, long ExStyle) StripStandardWindowFrameStyles(
        long currentStyle,
        long currentExStyle,
        bool preserveResizeFrame = false)
    {
        // Preserve WS_SYSMENU and avoid adding WS_EX_TOOLWINDOW so standard windows remain in taskbar/Alt+Tab.
        // Resizable windows retain WS_THICKFRAME (with WM_NCCALCSIZE = 0 hiding the visual frame) so DefWindowProc
        // allows native WM_NCLBUTTONDOWN edge/corner resizing.
        long styleMask = preserveResizeFrame ? FrameStyleMask & ~WsThickFrame : FrameStyleMask;
        long normalizedStyle = preserveResizeFrame
            ? (currentStyle & ~styleMask) | WsThickFrame
            : currentStyle & ~styleMask;
        long normalizedExStyle = currentExStyle & ~FrameExStyleMask;
        return (normalizedStyle, normalizedExStyle);
    }

    public static UwpWindowChromeLayout ResolveChromeLayout(
        int windowsBuildNumber,
        bool dwmAttributesSucceeded,
        bool isDarkTheme,
        bool isWindowActive)
    {
        bool useWin11DwmChrome = IsWindows11OrGreater(windowsBuildNumber) && dwmAttributesSucceeded;
        uint borderColor = (isDarkTheme, isWindowActive) switch
        {
            (true, true) => DarkActiveBorderColorRef,
            (true, false) => DarkInactiveBorderColorRef,
            (false, true) => LightActiveBorderColorRef,
            (false, false) => LightInactiveBorderColorRef
        };

        return new UwpWindowChromeLayout(
            UseWin11DwmChrome: useWin11DwmChrome,
            CornerPreferenceValue: DwmWcpRound,
            BorderColorRef: borderColor,
            RootCornerRadius: 0.0,
            RootBorderThickness: useWin11DwmChrome ? 0.0 : 1.0,
            IsTitleActive: isWindowActive,
            CloseHoverBackgroundRgb: CloseHoverBackgroundRgb,
            ClosePressedBackgroundRgb: ClosePressedBackgroundRgb);
    }

    public static int HitTestResizeBorder(
        double x,
        double y,
        double width,
        double height,
        double gripThickness,
        bool isResizable)
    {
        if (!isResizable || width <= 0 || height <= 0 || gripThickness <= 0)
        {
            return HtClient;
        }

        if (x < 0 || y < 0 || x > width || y > height)
        {
            return HtClient;
        }

        bool onLeft = x < gripThickness;
        bool onRight = x >= width - gripThickness;
        bool onTop = y < gripThickness;
        bool onBottom = y >= height - gripThickness;

        if (!onLeft && !onRight && !onTop && !onBottom)
        {
            return HtClient;
        }

        double cornerReach = gripThickness * 2.0;
        bool nearLeftCorner = x < cornerReach;
        bool nearRightCorner = x >= width - cornerReach;
        bool nearTopCorner = y < cornerReach;
        bool nearBottomCorner = y >= height - cornerReach;

        if (onTop)
        {
            if (nearLeftCorner) return HtTopLeft;
            if (nearRightCorner) return HtTopRight;
            return HtTop;
        }

        if (onBottom)
        {
            if (nearLeftCorner) return HtBottomLeft;
            if (nearRightCorner) return HtBottomRight;
            return HtBottom;
        }

        if (onLeft)
        {
            if (nearTopCorner) return HtTopLeft;
            if (nearBottomCorner) return HtBottomLeft;
            return HtLeft;
        }

        if (nearTopCorner) return HtTopRight;
        if (nearBottomCorner) return HtBottomRight;
        return HtRight;
    }
}
