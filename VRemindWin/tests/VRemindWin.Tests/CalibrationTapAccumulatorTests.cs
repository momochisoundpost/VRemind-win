using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class CalibrationTapAccumulatorTests
{
    [Fact]
    public void RegisterTap_EightTapsWithinWindow_TriggersOnceAndResetsCounter()
    {
        var acc = new CalibrationTapAccumulator();
        var t0 = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 7; i++)
        {
            Assert.False(acc.RegisterTap(t0.AddMilliseconds(i * 250)));
            Assert.Equal(i + 1, acc.CurrentCount);
        }

        Assert.True(acc.RegisterTap(t0.AddMilliseconds(1750)));
        Assert.Equal(0, acc.CurrentCount);
    }

    [Fact]
    public void RegisterTap_GapExceedingWindowOrNegativeSkew_RestartsAtOne()
    {
        var acc = new CalibrationTapAccumulator(requiredTaps: 8, maxGap: TimeSpan.FromSeconds(3));
        var t0 = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

        Assert.False(acc.RegisterTap(t0));
        Assert.False(acc.RegisterTap(t0.AddSeconds(1)));
        Assert.Equal(2, acc.CurrentCount);

        // > 3.0s gap resets streak so this tap starts a new count of 1.
        Assert.False(acc.RegisterTap(t0.AddSeconds(4.5)));
        Assert.Equal(1, acc.CurrentCount);

        // Negative clock jump also safely resets streak to 1.
        Assert.False(acc.RegisterTap(t0));
        Assert.Equal(1, acc.CurrentCount);
    }

    [Fact]
    public void Reset_ClearsProgressAndAllowsSubsequentStreak()
    {
        var acc = new CalibrationTapAccumulator(requiredTaps: 3);
        var t0 = DateTimeOffset.UtcNow;

        Assert.False(acc.RegisterTap(t0));
        Assert.False(acc.RegisterTap(t0.AddMilliseconds(100)));
        acc.Reset();
        Assert.Equal(0, acc.CurrentCount);

        Assert.False(acc.RegisterTap(t0.AddMilliseconds(200)));
        Assert.False(acc.RegisterTap(t0.AddMilliseconds(300)));
        Assert.True(acc.RegisterTap(t0.AddMilliseconds(400)));
        Assert.Equal(0, acc.CurrentCount);
    }
}
