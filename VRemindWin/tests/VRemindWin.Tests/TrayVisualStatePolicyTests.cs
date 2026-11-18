using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class TrayVisualStatePolicyTests
{
    private static CliChannelItem MakeItem(string id, string statusKind, string? streamTitle = null)
        => new(
            Id: id,
            Platform: "youtube",
            PlatformDisplayName: "YouTube",
            Name: id,
            Handle: "@test",
            FormattedHandle: "@test",
            AvatarURL: null,
            StatusKind: statusKind,
            StreamTitle: streamTitle,
            TimeLabel: statusKind == "live" ? "Live for 5m" : "Upcoming",
            WatchURL: "https://www.youtube.com/@test"
        );

    private static CliStateEnvelope MakeEnvelope(
        IReadOnlyList<CliChannelItem>? live = null,
        IReadOnlyList<CliChannelItem>? upcoming = null)
    {
        var liveList = live ?? [];
        var upcomingList = upcoming ?? [];
        return CliStateEnvelope.Empty with
        {
            LiveCount = liveList.Count,
            Sections = new CliSections(liveList, upcomingList, [])
        };
    }

    [Fact]
    public void Resolve_NoLive_IsNormal()
        => Assert.Equal(TrayVisualState.Normal, TrayVisualStatePolicy.Resolve(CliStateEnvelope.Empty));

    [Fact]
    public void Resolve_PlainLive_IsLive()
    {
        var state = MakeEnvelope(live: [MakeItem("ch1", "live", "Chill art stream")]);
        Assert.Equal(TrayVisualState.Live, TrayVisualStatePolicy.Resolve(state));
    }

    [Fact]
    public void Resolve_AnyGta6Live_IsGta6Live()
    {
        var state = MakeEnvelope(live:
        [
            MakeItem("ch1", "live", "Chill art stream"),
            MakeItem("ch2", "live", "GTA 6 early gameplay")
        ]);
        Assert.Equal(TrayVisualState.Gta6Live, TrayVisualStatePolicy.Resolve(state));
    }

    [Fact]
    public void Resolve_UpcomingGta6Only_IsNormal()
    {
        var state = MakeEnvelope(upcoming: [MakeItem("ch1", "upcoming", "GTA 6 waiting room")]);
        Assert.Equal(TrayVisualState.Normal, TrayVisualStatePolicy.Resolve(state));
    }

    [Fact]
    public void Resolve_RevertsWhenGta6StreamEnds()
    {
        var gta6State = MakeEnvelope(live:
        [
            MakeItem("ch1", "live", "GTA 6 early gameplay"),
            MakeItem("ch2", "live", "Just chatting")
        ]);
        var plainState = MakeEnvelope(live: [MakeItem("ch2", "live", "Just chatting")]);

        Assert.Equal(TrayVisualState.Gta6Live, TrayVisualStatePolicy.Resolve(gta6State));
        Assert.Equal(TrayVisualState.Live, TrayVisualStatePolicy.Resolve(plainState));
    }

    [Theory]
    [InlineData(TrayVisualState.Normal, "VRemind")]
    [InlineData(TrayVisualState.Live, "VRemind (Live now)")]
    [InlineData(TrayVisualState.Gta6Live, "VRemind (Live now - GTA 6)")]
    public void TooltipFor_ReturnsCopy(TrayVisualState s, string expected)
        => Assert.Equal(expected, TrayVisualStatePolicy.TooltipFor(s));
}
