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

    [Fact]
    public void StripStandardWindowFrameStyles_PreservesThickFrameWhenResizable()
    {
        const long observedStyle = WsVisible | WsClipSiblings | WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox;
        const long observedExStyle = WsExDlgModalFrame | WsExWindowEdge | WsExClientEdge | WsExStaticEdge | WsExToolWindow | WsExAppWindow;

        var (newStyle, newExStyle) = UwpWindowChromePolicy.StripStandardWindowFrameStyles(
            observedStyle,
            observedExStyle,
            preserveResizeFrame: true);

        Assert.Equal(0L, newStyle & (WsCaption | WsMinimizeBox | WsMaximizeBox | WsPopup));
        Assert.NotEqual(0L, newStyle & WsThickFrame);
        Assert.NotEqual(0L, newStyle & WsSysMenu);
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
        Assert.Equal(UwpWindowChromePolicy.DwmWcpRound, layout.CornerPreferenceValue);
    }

    [Fact]
    public void HitTestResizeBorder_ReturnsHtClientWhenNotResizableOrInvalidGeometry()
    {
        Assert.Equal(UwpWindowChromePolicy.HtClient, UwpWindowChromePolicy.HitTestResizeBorder(0, 0, 480, 480, 5, isResizable: false));
        Assert.Equal(UwpWindowChromePolicy.HtClient, UwpWindowChromePolicy.HitTestResizeBorder(479, 479, 480, 480, 5, isResizable: false));
        Assert.Equal(UwpWindowChromePolicy.HtClient, UwpWindowChromePolicy.HitTestResizeBorder(2, 2, 0, 480, 5, isResizable: true));
        Assert.Equal(UwpWindowChromePolicy.HtClient, UwpWindowChromePolicy.HitTestResizeBorder(2, 2, 480, 0, 5, isResizable: true));
        Assert.Equal(UwpWindowChromePolicy.HtClient, UwpWindowChromePolicy.HitTestResizeBorder(2, 2, 480, 480, 0, isResizable: true));
    }

    [Theory]
    [InlineData(2, 2, UwpWindowChromePolicy.HtTopLeft)]
    [InlineData(8, 2, UwpWindowChromePolicy.HtTopLeft)]
    [InlineData(478, 2, UwpWindowChromePolicy.HtTopRight)]
    [InlineData(472, 2, UwpWindowChromePolicy.HtTopRight)]
    [InlineData(2, 418, UwpWindowChromePolicy.HtBottomLeft)]
    [InlineData(8, 418, UwpWindowChromePolicy.HtBottomLeft)]
    [InlineData(478, 418, UwpWindowChromePolicy.HtBottomRight)]
    [InlineData(472, 418, UwpWindowChromePolicy.HtBottomRight)]
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
