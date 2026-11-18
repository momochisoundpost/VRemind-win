using System.Text.RegularExpressions;

namespace VRemindWin.Core.Services;

/// <summary>
/// Detects GTA 6 stream titles across common shorthand and Roman numeral variants.
/// </summary>
public static class Gta6Detector
{
    private static readonly Regex Gta6Pattern = new(
        @"\b(?:GTA|Grand\s+Theft\s+Auto)[\s-]*(?:6|VI)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsMatch(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        return Gta6Pattern.IsMatch(title);
    }
}
