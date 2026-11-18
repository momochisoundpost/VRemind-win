using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class Gta6DetectorTests
{
    [Theory]
    [InlineData("GTA 6 EARLY GAMEPLAY")]
    [InlineData("gta6 day one")]
    [InlineData("GTA VI hype")]
    [InlineData("GTAVI first look")]
    [InlineData("Grand Theft Auto 6 stream")]
    [InlineData("grand theft auto vi!!")]
    [InlineData("GTA  6")]
    public void Matches_AllVariants(string title) => Assert.True(Gta6Detector.IsMatch(title));

    [Theory]
    [InlineData("#GTA6 LETS GO")]
    [InlineData("GTA 6!!")]
    [InlineData("[GTA VI] part 1")]
    [InlineData("gta-6 time")]
    public void Matches_WithPunctuationAndHashtags(string title) => Assert.True(Gta6Detector.IsMatch(title));

    [Theory]
    [InlineData("GTA 60 fps")]
    [InlineData("GTA VII when")]
    [InlineData("GTA 5 RP")]
    [InlineData("MEGTA6")]
    [InlineData("GTA 6000")]
    [InlineData("Grand Theft Auto V")]
    public void DoesNotMatch_NearMisses(string title) => Assert.False(Gta6Detector.IsMatch(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DoesNotMatch_EmptyOrNull(string? title) => Assert.False(Gta6Detector.IsMatch(title));
}
