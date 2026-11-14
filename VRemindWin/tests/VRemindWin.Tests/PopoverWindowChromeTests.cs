using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public class PopoverWindowChromeTests
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
    private const long WsExTopMost = 0x00000008L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExWindowEdge = 0x00000100L;
    private const long WsExClientEdge = 0x00000200L;
    private const long WsExStaticEdge = 0x00020000L;
    private const long WsExAppWindow = 0x00040000L;

    [Fact]
    public void StripFrameStyles_RemovesDialogAndWindowEdgeBitsWithoutForcingWsPopup()
    {
        // Exact observed runtime HWND flags after OverlappedPresenter.SetBorderAndTitleBar(false, false):
        // Style = 0x14480000 (WS_VISIBLE | WS_CLIPSIBLINGS | WS_DLGFRAME | WS_SYSMENU)
        // ExStyle = 0x00000188 (WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST)
        const long observedStyle = WsVisible | WsClipSiblings | WsDlgFrame | WsSysMenu;
        const long observedExStyle = WsExWindowEdge | WsExToolWindow | WsExTopMost;

        var (newStyle, newExStyle) = PopoverWindowChromePolicy.StripFrameStyles(observedStyle, observedExStyle);

        Assert.Equal(0L, newStyle & (WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox));
        Assert.Equal(0L, newStyle & WsPopup);
        Assert.NotEqual(0L, newStyle & WsClipSiblings);
        Assert.NotEqual(0L, newStyle & WsVisible);

        Assert.Equal(0L, newExStyle & (WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExAppWindow));
        Assert.NotEqual(0L, newExStyle & WsExToolWindow);
        Assert.NotEqual(0L, newExStyle & WsExTopMost);
    }

    [Fact]
    public void StripFrameStyles_ClearsAllBorderAndAppWindowBitsEvenWhenFullyOverlapped()
    {
        const long rawStyle = WsVisible | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox;
        const long rawExStyle = WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExAppWindow;

        var (newStyle, newExStyle) = PopoverWindowChromePolicy.StripFrameStyles(rawStyle, rawExStyle);

        Assert.Equal(WsVisible, newStyle);
        Assert.Equal(WsExToolWindow, newExStyle);
    }

    [Theory]
    [InlineData(17763, true, false, 1.0)]  // Windows 10 1809 LTSC
    [InlineData(19044, true, false, 1.0)]  // Windows 10 21H2 IoT LTSC
    [InlineData(19045, false, false, 1.0)] // Windows 10 22H2 Light
    [InlineData(22000, true, true, 0.0)]   // Windows 11 21H2 Dark
    [InlineData(26100, false, true, 0.0)]  // Windows 11 24H2 Light
    public void ResolveChromeLayout_SelectsOsAppropriateFrameAndBorderThickness(
        int osBuild,
        bool isDarkTheme,
        bool expectDwmRoundedFrame,
        double expectedXamlBorderThickness)
    {
        var layout = PopoverWindowChromePolicy.ResolveChromeLayout(
            windowsBuildNumber: osBuild,
            dwmAttributesSucceeded: expectDwmRoundedFrame,
            isDarkTheme: isDarkTheme);

        Assert.Equal(expectDwmRoundedFrame, layout.UseWin11DwmChrome);
        Assert.Equal(0.0, layout.RootCornerRadius);
        Assert.Equal(expectedXamlBorderThickness, layout.RootBorderThickness);
        Assert.Equal(PopoverWindowChromePolicy.DwmWcpRound, layout.CornerPreferenceValue);
        Assert.Equal(
            isDarkTheme ? PopoverWindowChromePolicy.DarkBorderColorRef : PopoverWindowChromePolicy.LightBorderColorRef,
            layout.BorderColorRef);
    }

    [Fact]
    public void ResolveChromeLayout_FallsBackToXamlBorderWhenWin11DwmCallFails()
    {
        var layout = PopoverWindowChromePolicy.ResolveChromeLayout(
            windowsBuildNumber: 22631,
            dwmAttributesSucceeded: false,
            isDarkTheme: true);

        Assert.False(layout.UseWin11DwmChrome);
        Assert.Equal(1.0, layout.RootBorderThickness);
        Assert.Equal(0.0, layout.RootCornerRadius);
    }
}
