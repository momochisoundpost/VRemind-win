using VRemindWin.Core.Models;
using Xunit;

namespace VRemindWin.Tests;

public sealed class Gta6ChannelItemTests
{
    private static CliChannelItem Item(string statusKind, string? title, bool isCelebration = false)
    {
        return new CliChannelItem(
            Id: "youtube:@bukancaitlyn",
            Platform: "youtube",
            PlatformDisplayName: "YouTube",
            Name: "Caitlyn Momochi",
            Handle: "@bukancaitlyn",
            FormattedHandle: "@bukancaitlyn",
            AvatarURL: null,
            StatusKind: statusKind,
            StreamTitle: title,
            TimeLabel: statusKind == "live" ? "Live for 5m" : "Offline",
            WatchURL: "https://www.youtube.com/@bukancaitlyn",
            IsCelebration: isCelebration,
            CelebrationType: isCelebration ? "birthday" : null
        );
    }

    [Fact]
    public void LiveWithGta6Title_IsGta6()
    {
        var i = Item("live", "GTA 6 early gameplay");
        Assert.True(i.IsGta6Live);
        Assert.Equal("gta6", i.EffectiveVisualKind);
    }

    [Fact]
    public void UpcomingWithGta6Title_IsNotGta6()
    {
        var i = Item("upcoming", "GTA 6 tomorrow");
        Assert.False(i.IsGta6Live);
        Assert.Equal("upcoming", i.EffectiveVisualKind);
    }

    [Fact]
    public void Gta6BeatsCelebration()
        => Assert.Equal("gta6", Item("live", "GTA 6 birthday stream", isCelebration: true).EffectiveVisualKind);

    [Fact]
    public void CelebrationWithoutGta6_Unchanged()
        => Assert.Equal("celebration", Item("live", "birthday stream", isCelebration: true).EffectiveVisualKind);

    [Fact]
    public void LiveWithNullTitle_IsPlainLive()
    {
        var i = Item("live", null);
        Assert.False(i.IsGta6Live);
        Assert.Equal("live", i.EffectiveVisualKind);
        Assert.Equal("LIVE", i.StatusBadgeText);
    }
}
