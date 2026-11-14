using System.Xml;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class ToastAndLinkSafetyTests
{
    [Fact]
    public void BuildToastXml_EscapesSpecialXmlCharactersAndSetsSilentAudioFlag()
    {
        var item = new CliNotificationItem(
            Kind: "live",
            ChannelName: "Caitlyn & Friends <Live>",
            Title: "Caitlyn & Friends <Live>",
            Body: "🔴 LIVE: Rock & Roll > Everything \"Now\" 'Live'",
            TargetURL: "https://www.youtube.com/watch?v=abc&feature=live",
            PlaySound: false
        );

        var xml = WindowsToastService.BuildToastXml(item);
        var doc = new XmlDocument();
        doc.LoadXml(xml);

        var toastNode = doc.SelectSingleNode("/toast");
        Assert.NotNull(toastNode);
        var launchAttr = toastNode!.Attributes?["launch"]?.Value;
        Assert.True(LinkSafetyValidator.TryExtractTargetUrlFromToastArgs(launchAttr, out var parsedUri));
        Assert.Equal("https://www.youtube.com/watch?v=abc&feature=live", parsedUri!.AbsoluteUri);

        var audioNode = doc.SelectSingleNode("/toast/audio");
        Assert.NotNull(audioNode);
        Assert.Equal("true", audioNode!.Attributes?["silent"]?.Value);
    }

    [Theory]
    [InlineData("action=openUrl&url=https%3A%2F%2Fwww.youtube.com%2Fwatch%3Fv%3D123", true, "https://www.youtube.com/watch?v=123")]
    [InlineData("action=openUrl&url=http%3A%2F%2Fexample.com%2Flive", true, "http://example.com/live")]
    [InlineData("action=openUrl&url=mailto%3Ay2karyl%40disroot.org%3Fsubject%3DHappy%2520Birthday%2520Caitlyn!", true, "mailto:y2karyl@disroot.org?subject=Happy%20Birthday%20Caitlyn!")]
    [InlineData("action=openUrl&url=file%3A%2F%2F%2FC%3A%2FWindows%2FSystem32%2Fcmd.exe", false, null)]
    [InlineData("action=openUrl&url=javascript%3Aalert(1)", false, null)]
    [InlineData("action=openUrl&url=ms-settings%3Anotifications", false, null)]
    [InlineData("", false, null)]
    [InlineData("not-a-valid-query", false, null)]
    public void TryExtractTargetUrlFromToastArgs_BlocksUnsafeSchemesAndAllowsHttpHttpsMailto(
        string args,
        bool expectedOk,
        string? expectedUri)
    {
        var ok = LinkSafetyValidator.TryExtractTargetUrlFromToastArgs(args, out var uri);
        Assert.Equal(expectedOk, ok);
        if (expectedOk)
        {
            Assert.NotNull(uri);
            Assert.Equal(expectedUri, uri!.OriginalString);
        }
        else
        {
            Assert.Null(uri);
        }
    }
}
