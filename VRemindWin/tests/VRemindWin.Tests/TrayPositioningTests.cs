using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public class TrayPositioningTests
{
    [Fact]
    public void CalculateFlyoutBounds_BottomTaskbar_CentersHorizontallyAndClampsToRightWorkAreaEdge()
    {
        var bounds = TrayFlyoutPositioner.CalculateFlyoutBounds(
            iconRect: (1850, 1040, 1874, 1080),
            workArea: (0, 0, 1920, 1040),
            dpi: 96);

        Assert.Equal((1552, 592, 360, 440), bounds);
    }

    [Fact]
    public void CalculateFlyoutBounds_ScalesDimensionsAt150PercentDpi()
    {
        var bounds = TrayFlyoutPositioner.CalculateFlyoutBounds(
            iconRect: (1000, 1040, 1036, 1080),
            workArea: (0, 0, 1920, 1040),
            dpi: 144);

        Assert.Equal(540, bounds.Width);
        Assert.Equal(660, bounds.Height);
        // Bottom margin is 8 * 1.5 = 12, so Y = 1040 - 660 - 12 = 368.
        // Centered on iconRect center (1018): 1018 - 270 = 748.
        Assert.Equal(748, bounds.X);
        Assert.Equal(368, bounds.Y);
    }

    [Fact]
    public void CalculateFlyoutBounds_TopTaskbar_PositionsBelowWorkAreaTopEdge()
    {
        var bounds = TrayFlyoutPositioner.CalculateFlyoutBounds(
            iconRect: (900, 0, 924, 40),
            workArea: (0, 40, 1920, 1080),
            dpi: 96);

        Assert.Equal((732, 48, 360, 440), bounds);
    }

    [Fact]
    public void CalculateFlyoutBounds_RightTaskbar_PositionsLeftOfWorkAreaRightEdge()
    {
        var bounds = TrayFlyoutPositioner.CalculateFlyoutBounds(
            iconRect: (1858, 900, 1920, 924),
            workArea: (0, 0, 1858, 1080),
            dpi: 96);

        Assert.Equal((1490, 632, 360, 440), bounds);
    }

    [Fact]
    public void CalculateFlyoutBounds_LeftTaskbar_PositionsRightOfWorkAreaLeftEdge()
    {
        var bounds = TrayFlyoutPositioner.CalculateFlyoutBounds(
            iconRect: (0, 900, 62, 924),
            workArea: (62, 0, 1920, 1080),
            dpi: 96);

        Assert.Equal((70, 632, 360, 440), bounds);
    }

    [Fact]
    public void CalculateFlyoutBounds_TinyWorkArea_ClampsDimensionsToWorkArea()
    {
        var bounds = TrayFlyoutPositioner.CalculateFlyoutBounds(
            iconRect: (100, 600, 124, 640),
            workArea: (0, 0, 320, 400),
            dpi: 96);

        Assert.InRange(bounds.Width, 1, 320);
        Assert.InRange(bounds.Height, 1, 400);
        Assert.True(bounds.X >= 0 && bounds.X + bounds.Width <= 320);
        Assert.True(bounds.Y >= 0 && bounds.Y + bounds.Height <= 400);
    }

    [Fact]
    public void ShouldSuppressTrayReopen_ReturnsTrueOnlyWithinCooldownWindow()
    {
        var t0 = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

        Assert.False(TrayFlyoutPositioner.ShouldSuppressTrayReopen(null, t0));
        Assert.True(TrayFlyoutPositioner.ShouldSuppressTrayReopen(t0, t0.AddMilliseconds(80)));
        Assert.False(TrayFlyoutPositioner.ShouldSuppressTrayReopen(t0, t0.AddMilliseconds(400)));
    }
}
