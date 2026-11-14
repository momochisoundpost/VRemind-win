namespace VRemindWin.Core.Services;

public readonly record struct RgbColor(byte R, byte G, byte B);

/// <summary>
/// Evaluates a smooth 2-stop shifting-hue gradient along the celebration palette
/// so celebrating badges, borders, and text show at most two neighboring hues at any instant.
/// </summary>
public static class RainbowGradientMath
{
    public const double NormalCycleSeconds = 6.0;
    public const double ReduceMotionCycleSeconds = 36.0;
    private const double HueSpanNormalized = 1.0 / 6.0;

    private static readonly RgbColor[] Palette =
    [
        new(255, 0, 85),    // #FF0055 Rose
        new(249, 115, 22),  // #F97316 Orange
        new(217, 155, 6),   // #D99B06 Warm Gold
        new(22, 163, 74),   // #16A34A Emerald Green
        new(2, 132, 199),   // #0284C7 Ocean Blue
        new(139, 92, 246),  // #8B5CF6 Violet
    ];

    public static (RgbColor Start, RgbColor End) Evaluate(double elapsedSeconds, bool reduceMotion)
    {
        double period = reduceMotion ? ReduceMotionCycleSeconds : NormalCycleSeconds;
        double phase = (elapsedSeconds % period) / period;
        if (phase < 0)
        {
            phase += 1.0;
        }

        return (SamplePalette(phase), SamplePalette(phase + HueSpanNormalized));
    }

    public static RgbColor SamplePalette(double normalizedPhase)
    {
        double wrapped = normalizedPhase % 1.0;
        if (wrapped < 0)
        {
            wrapped += 1.0;
        }

        double scaled = wrapped * Palette.Length;
        int index0 = (int)Math.Floor(scaled) % Palette.Length;
        int index1 = (index0 + 1) % Palette.Length;
        double t = scaled - Math.Floor(scaled);

        var c0 = Palette[index0];
        var c1 = Palette[index1];
        return new RgbColor(
            (byte)Math.Round(c0.R + (c1.R - c0.R) * t),
            (byte)Math.Round(c0.G + (c1.G - c0.G) * t),
            (byte)Math.Round(c0.B + (c1.B - c0.B) * t));
    }
}
