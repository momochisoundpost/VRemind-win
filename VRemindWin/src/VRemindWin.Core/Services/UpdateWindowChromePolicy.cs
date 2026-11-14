using VRemindWin.Core.Models;

namespace VRemindWin.Core.Services;

public readonly record struct UpdateWindowChromeState(
    string WindowTitle,
    string HeaderTitle,
    bool ShowSubtitle,
    bool ShowSkipButton,
    bool ShowDownloadButton,
    string DismissButtonText);

/// <summary>
/// Resolves header and footer chrome visibility for update versus standalone Markdown reader windows.
/// </summary>
public static class UpdateWindowChromePolicy
{
    public static UpdateWindowChromeState Resolve(CliReleaseDto release)
    {
        ArgumentNullException.ThrowIfNull(release);

        if (release.IsReaderMode)
        {
            var readerTitle = !string.IsNullOrWhiteSpace(release.Name)
                ? release.Name.Trim()
                : "Notes";

            return new UpdateWindowChromeState(
                WindowTitle: readerTitle,
                HeaderTitle: readerTitle,
                ShowSubtitle: false,
                ShowSkipButton: false,
                ShowDownloadButton: false,
                DismissButtonText: "Close");
        }

        var headerTitle = !string.IsNullOrWhiteSpace(release.Name)
            ? $"{release.Name} is available!"
            : $"VRemind {release.TagName} is available!";

        return new UpdateWindowChromeState(
            WindowTitle: "Software Update",
            HeaderTitle: headerTitle,
            ShowSubtitle: true,
            ShowSkipButton: true,
            ShowDownloadButton: true,
            DismissButtonText: "Remind Me Later");
    }
}
