using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VRemindWin.Core.Services;
using Windows.Foundation;
using Windows.UI;

namespace VRemindWin.Helpers;

public static class UiBindingHelpers
{
    public static Visibility ToVisibility(bool value)
        => value ? Visibility.Visible : Visibility.Collapsed;

    public static bool Negate(bool value)
        => !value;
}

public static class RainbowGradientProvider
{
    private static LinearGradientBrush? _sharedBrush;
    private static GradientStop? _startStop;
    private static GradientStop? _endStop;

    public static LinearGradientBrush SharedBrush => _sharedBrush ??= CreateRainbowBrush();

    public static void UpdatePhase(double elapsedSeconds, bool reduceMotion)
    {
        _ = SharedBrush;
        var (start, end) = RainbowGradientMath.Evaluate(elapsedSeconds, reduceMotion);
        _startStop!.Color = Color.FromArgb(255, start.R, start.G, start.B);
        _endStop!.Color = Color.FromArgb(255, end.R, end.G, end.B);
    }

    private static LinearGradientBrush CreateRainbowBrush()
    {
        var (start, end) = RainbowGradientMath.Evaluate(0.0, false);
        _startStop = new GradientStop { Color = Color.FromArgb(255, start.R, start.G, start.B), Offset = 0.0 };
        _endStop = new GradientStop { Color = Color.FromArgb(255, end.R, end.G, end.B), Offset = 1.0 };

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(1.0, 1.0)
        };
        brush.GradientStops.Add(_startStop);
        brush.GradientStops.Add(_endStop);
        return brush;
    }
}

/// <summary>
/// Provides the static Miami Vice purple gradient brush for GTA 6 live indicators.
/// </summary>
public static class MiamiViceGradientProvider
{
    private static LinearGradientBrush? _sharedBrush;

    public static LinearGradientBrush SharedBrush => _sharedBrush ??= CreateMiamiViceBrush();

    private static LinearGradientBrush CreateMiamiViceBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.0, 0.0),
            EndPoint = new Point(1.0, 1.0)
        };
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 0x8B, 0x5C, 0xF6), Offset = 0.0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(255, 0xD9, 0x46, 0xEF), Offset = 1.0 });
        return brush;
    }
}

internal static class StatusVisualBrushHelper
{
    internal static Brush? ResolveSharedGradient(string? status)
    {
        if (string.Equals(status, "gta6", StringComparison.OrdinalIgnoreCase))
        {
            return MiamiViceGradientProvider.SharedBrush;
        }

        if (string.Equals(status, "celebration", StringComparison.OrdinalIgnoreCase))
        {
            return RainbowGradientProvider.SharedBrush;
        }

        return null;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;
        if (Invert)
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility v && (Invert ? v != Visibility.Visible : v == Visibility.Visible);
}

public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is bool b ? !b : false;
}

public sealed class HexToSolidColorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => new SolidColorBrush(ParseHex(value as string));

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();

    public static Color ParseHex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return Colors.Gray;
        }

        var span = hex.AsSpan().Trim();
        if (span.StartsWith("#"))
        {
            span = span[1..];
        }

        if (span.Length == 6 &&
            byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
            byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
            byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return Color.FromArgb(255, r, g, b);
        }

        if (span.Length == 8 &&
            byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) &&
            byte.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r8) &&
            byte.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g8) &&
            byte.TryParse(span.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b8))
        {
            return Color.FromArgb(a, r8, g8, b8);
        }

        return Colors.Gray;
    }
}

public sealed class StatusKindToTimeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as string;
        if (StatusVisualBrushHelper.ResolveSharedGradient(status) is { } shared)
        {
            return shared;
        }

        if (string.Equals(status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
        }

        if (string.Equals(status, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromArgb(255, 217, 119, 6));
        }

        return (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class StatusKindToAvatarRingBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as string;
        if (StatusVisualBrushHelper.ResolveSharedGradient(status) is { } shared)
        {
            return shared;
        }

        if (string.Equals(status, "live", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromArgb(255, 239, 68, 68));
        }

        if (string.Equals(status, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromArgb(255, 217, 119, 6));
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class StatusKindToBadgeBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as string;
        if (StatusVisualBrushHelper.ResolveSharedGradient(status) is { } shared)
        {
            return shared;
        }

        if (string.Equals(status, "upcoming", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidColorBrush(Color.FromArgb(255, 217, 119, 6));
        }

        return new SolidColorBrush(Color.FromArgb(255, 220, 38, 38));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class CelebrationUpcomingHeaderBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true
            ? RainbowGradientProvider.SharedBrush
            : new SolidColorBrush(Color.FromArgb(255, 217, 119, 6));

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

public sealed class AvatarUriConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is string url &&
            LinkSafetyValidator.TryValidateAllowedUri(url, out var uri) &&
            uri is not null &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                return new BitmapImage(uri);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
