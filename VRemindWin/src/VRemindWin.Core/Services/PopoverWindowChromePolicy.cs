namespace VRemindWin.Core.Services;

public readonly record struct PopoverChromeLayout(
    bool UseWin11DwmChrome,
    uint CornerPreferenceValue,
    uint BorderColorRef,
    double RootCornerRadius,
    double RootBorderThickness);

/// <summary>
/// Pure Win32 style and DWM chrome configuration policy for the borderless tray flyout window.
/// </summary>
public static class PopoverWindowChromePolicy
{
    public const int DwmWaWindowCornerPreference = 33;
    public const int DwmWaBorderColor = 34;

    public const uint DwmWcpRound = 2;

    // Win32 COLORREF (0x00BBGGRR) matching WinUI CardStrokeColorDefaultBrush in Dark and Light themes.
    public const uint DarkBorderColorRef = 0x003E3E3Eu;
    public const uint LightBorderColorRef = 0x00D6D6D6u;

    private const long WsCaption = 0x00C00000L;      // WS_BORDER | WS_DLGFRAME
    private const long WsThickFrame = 0x00040000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;

    private const long WsExDlgModalFrame = 0x00000001L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;
    private const long WsExStaticEdge = 0x00020000L;
    private const long WsExAppWindow = 0x00040000L;

    private const long FrameStyleMask = WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox;
    private const long FrameExStyleMask = WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExAppWindow;

    public static bool IsWindows11OrGreater(int windowsBuildNumber)
        => windowsBuildNumber >= 22000;

    public static (long Style, long ExStyle) StripFrameStyles(long currentStyle, long currentExStyle)
    {
        long borderlessStyle = currentStyle & ~FrameStyleMask;
        long borderlessExStyle = (currentExStyle & ~FrameExStyleMask) | WsExToolWindow;
        return (borderlessStyle, borderlessExStyle);
    }

    public static PopoverChromeLayout ResolveChromeLayout(
        int windowsBuildNumber,
        bool dwmAttributesSucceeded,
        bool isDarkTheme)
    {
        bool useWin11DwmChrome = IsWindows11OrGreater(windowsBuildNumber) && dwmAttributesSucceeded;
        uint borderColor = isDarkTheme ? DarkBorderColorRef : LightBorderColorRef;

        return new PopoverChromeLayout(
            UseWin11DwmChrome: useWin11DwmChrome,
            CornerPreferenceValue: DwmWcpRound,
            BorderColorRef: borderColor,
            RootCornerRadius: 0.0,
            RootBorderThickness: useWin11DwmChrome ? 0.0 : 1.0);
    }
}
