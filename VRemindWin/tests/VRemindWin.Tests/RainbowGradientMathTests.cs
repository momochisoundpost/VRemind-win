using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class RainbowGradientMathTests
{
    [Fact]
    public void Evaluate_ProducesTwoDistinctAdjacentHuesThatShiftOverTime()
    {
        var t0 = RainbowGradientMath.Evaluate(elapsedSeconds: 0.0, reduceMotion: false);
        var tMid = RainbowGradientMath.Evaluate(elapsedSeconds: 2.0, reduceMotion: false);
        var tNotWrappedAt3s = RainbowGradientMath.Evaluate(elapsedSeconds: 3.0, reduceMotion: false);
        var tWrap = RainbowGradientMath.Evaluate(elapsedSeconds: 6.0, reduceMotion: false);

        // At any instant, Start and End are two distinct colors forming a 2-color gradient.
        Assert.NotEqual(t0.Start, t0.End);
        Assert.NotEqual(tMid.Start, tMid.End);

        // Hues shift smoothly across the 6.0s cycle and wrap back cleanly at 6.0s.
        Assert.NotEqual(t0.Start, tMid.Start);
        Assert.NotEqual(t0.Start, tNotWrappedAt3s.Start);
        Assert.Equal(t0.Start, tWrap.Start);
        Assert.Equal(t0.End, tWrap.End);
    }

    [Fact]
    public void Evaluate_RespectsReduceMotionCycle()
    {
        var fastHalf = RainbowGradientMath.Evaluate(elapsedSeconds: 3.0, reduceMotion: false);
        var slowAtThreeSeconds = RainbowGradientMath.Evaluate(elapsedSeconds: 3.0, reduceMotion: true);
        var slowHalf = RainbowGradientMath.Evaluate(elapsedSeconds: 18.0, reduceMotion: true);

        Assert.NotEqual(fastHalf.Start, slowAtThreeSeconds.Start);
        Assert.Equal(fastHalf.Start, slowHalf.Start);
        Assert.Equal(fastHalf.End, slowHalf.End);
    }

    [Fact]
    public void SamplePalette_IsContinuousAcrossWrapBoundary()
    {
        var beforeWrap = RainbowGradientMath.SamplePalette(0.9999);
        var atZero = RainbowGradientMath.SamplePalette(0.0);

        Assert.InRange(Math.Abs(beforeWrap.R - atZero.R), 0, 2);
        Assert.InRange(Math.Abs(beforeWrap.G - atZero.G), 0, 2);
        Assert.InRange(Math.Abs(beforeWrap.B - atZero.B), 0, 2);
    }
}
