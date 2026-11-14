using System.Text.Json;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class UpdateWindowChromePolicyTests
{
    [Fact]
    public void Resolve_UpdateModeVsReaderMode_ReturnsExpectedChromeAndAllowsRoundTripToggle()
    {
        var updateRelease = new CliReleaseDto("v1.3.2", "VRemind 1.3.2", "Notes", "https://example.com", "https://example.com/zip");
        var readerRelease = new CliReleaseDto("", "Calibration Notes", "# Body", "", "", IsReaderMode: true);
        var blankReader = new CliReleaseDto("", "   ", "# Body", "", "", IsReaderMode: true);

        var updateChrome = UpdateWindowChromePolicy.Resolve(updateRelease);
        Assert.Equal("Software Update", updateChrome.WindowTitle);
        Assert.Equal("VRemind 1.3.2 is available!", updateChrome.HeaderTitle);
        Assert.True(updateChrome.ShowSubtitle);
        Assert.True(updateChrome.ShowSkipButton);
        Assert.True(updateChrome.ShowDownloadButton);
        Assert.Equal("Remind Me Later", updateChrome.DismissButtonText);

        var readerChrome = UpdateWindowChromePolicy.Resolve(readerRelease);
        Assert.Equal("Calibration Notes", readerChrome.WindowTitle);
        Assert.Equal("Calibration Notes", readerChrome.HeaderTitle);
        Assert.False(readerChrome.ShowSubtitle);
        Assert.False(readerChrome.ShowSkipButton);
        Assert.False(readerChrome.ShowDownloadButton);
        Assert.Equal("Close", readerChrome.DismissButtonText);

        var blankChrome = UpdateWindowChromePolicy.Resolve(blankReader);
        Assert.Equal("Notes", blankChrome.WindowTitle);
        Assert.Equal("Notes", blankChrome.HeaderTitle);

        // JsonIgnore ensures wire JSON matches existing CLI contract.
        string json = JsonSerializer.Serialize(readerRelease);
        Assert.DoesNotContain("IsReaderMode", json, StringComparison.OrdinalIgnoreCase);
    }
}
