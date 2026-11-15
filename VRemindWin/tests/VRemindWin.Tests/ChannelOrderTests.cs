using System.Text.Json.Nodes;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using Xunit;

namespace VRemindWin.Tests;

public sealed class ChannelOrderTests
{
    private static CliChannelItem MakeItem(string id, string name, string status = "offline") => new(
        Id: id,
        Platform: "youtube",
        PlatformDisplayName: "YouTube",
        Name: name,
        Handle: id.Contains(':') ? id.Split(':')[1] : id,
        FormattedHandle: id.Contains(':') ? id.Split(':')[1] : id,
        AvatarURL: null,
        StatusKind: status,
        StreamTitle: null,
        TimeLabel: "Offline",
        WatchURL: $"https://www.youtube.com/{(id.Contains(':') ? id.Split(':')[1] : id)}");

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(4.9, false)]
    [InlineData(-4.9, false)]
    [InlineData(5.0, true)]
    [InlineData(-5.0, true)]
    [InlineData(18.5, true)]
    public void ExceedsDragThreshold_RequiresAtLeastFiveDip(double deltaY, bool expected)
    {
        Assert.Equal(expected, ChannelOrderPolicy.ExceedsDragThreshold(deltaY));
    }

    [Fact]
    public void CalculateDragLayout_ComputesTargetIndexClampingAndSiblingShifts()
    {
        double[] heights = [50.0, 50.0, 50.0];

        // Dragging item 0 down by 60px crosses midpoint of item 1, targeting index 1
        var stepDown = ChannelOrderPolicy.CalculateDragLayout(0, deltaY: 60.0, heights);
        Assert.Equal(1, stepDown.TargetIndex);
        Assert.Equal(60.0, stepDown.ClampedDragTranslateY);
        Assert.Equal([60.0, -50.0, 0.0], stepDown.ItemTranslateY);

        // Dragging item 0 down by 500px clamps to max travel (+100px) and target index 2
        var stepClamped = ChannelOrderPolicy.CalculateDragLayout(0, deltaY: 500.0, heights);
        Assert.Equal(2, stepClamped.TargetIndex);
        Assert.Equal(100.0, stepClamped.ClampedDragTranslateY);
        Assert.Equal([100.0, -50.0, -50.0], stepClamped.ItemTranslateY);

        // Dragging item 2 up by -60px crosses midpoint of item 1, targeting index 1
        var stepUp = ChannelOrderPolicy.CalculateDragLayout(2, deltaY: -60.0, heights);
        Assert.Equal(1, stepUp.TargetIndex);
        Assert.Equal(-60.0, stepUp.ClampedDragTranslateY);
        Assert.Equal([0.0, 50.0, -60.0], stepUp.ItemTranslateY);
    }

    [Fact]
    public void CalculateDragLayout_AllowsTallRowToReachBottomOfShorterSibling()
    {
        double[] heights = [70.0, 50.0];

        var step = ChannelOrderPolicy.CalculateDragLayout(0, deltaY: 50.0, heights, itemSpacing: 0.0);

        Assert.Equal(1, step.TargetIndex);
        Assert.Equal(50.0, step.ClampedDragTranslateY);
        Assert.Equal([50.0, -70.0], step.ItemTranslateY);
    }

    [Fact]
    public void ReorderSectionItems_MovesItemAndHandlesOutOfBoundsSafely()
    {
        var a = MakeItem("youtube:@a", "A");
        var b = MakeItem("youtube:@b", "B");
        var c = MakeItem("youtube:@c", "C");
        IReadOnlyList<CliChannelItem> list = [a, b, c];

        var moved = ChannelOrderPolicy.ReorderSectionItems(list, fromIndex: 2, toIndex: 0);
        Assert.Equal(["youtube:@c", "youtube:@a", "youtube:@b"], moved.Select(x => x.Id));

        Assert.Equal(list, ChannelOrderPolicy.ReorderSectionItems(list, 1, 1));
        Assert.Equal(list, ChannelOrderPolicy.ReorderSectionItems(list, -1, 1));
        Assert.Equal(list, ChannelOrderPolicy.ReorderSectionItems(list, 0, 99));
    }

    [Fact]
    public void ApplyCustomOrder_ReordersWithinEachSectionWithoutCrossSectionMoves()
    {
        var live1 = MakeItem("youtube:@l1", "L1", "live");
        var live2 = MakeItem("youtube:@l2", "L2", "live");
        var off1 = MakeItem("youtube:@o1", "O1", "offline");
        var off2 = MakeItem("youtube:@o2", "O2", "offline");
        var offNew = MakeItem("youtube:@o3", "O3", "offline");

        var state = new CliStateEnvelope(
            LiveCount: 2,
            Sections: new CliSections([live1, live2], [], [off1, off2, offNew]),
            Settings: AppSettingsDto.Default,
            Notifications: []);

        var projected = ChannelOrderPolicy.ApplyCustomOrder(
            state,
            ["youtube:@l2", "youtube:@o2", "youtube:@l1", "youtube:@o1"]);

        Assert.Equal(["youtube:@l2", "youtube:@l1"], projected.Sections.Live.Select(x => x.Id));
        Assert.Equal(["youtube:@o2", "youtube:@o1", "youtube:@o3"], projected.Sections.Offline.Select(x => x.Id));
    }

    [Fact]
    public void ReorderChannelsJson_PreservesInterleavedOtherSectionsAndLegacyHandlesAndExtraFields()
    {
        const string inputJson = """
        [
          {
            "id": "@legacy_offline_a",
            "platform": "youtube",
            "name": "Offline A",
            "handle": "@legacy_offline_a",
            "lastNotifiedVideoId": "vidA"
          },
          {
            "id": "twitch:live_b",
            "platform": "twitch",
            "name": "Live B",
            "handle": "live_b",
            "customExtraField": 42
          },
          {
            "id": "youtube:@offline_c",
            "platform": "youtube",
            "name": "Offline C",
            "handle": "@offline_c",
            "lastStreamedAt": "2026-11-14T00:00:00Z"
          }
        ]
        """;

        string outputJson = ChannelOrderPolicy.ReorderChannelsJson(
            inputJson,
            ["youtube:@offline_c", "youtube:@legacy_offline_a"]);

        var array = JsonNode.Parse(outputJson)!.AsArray();
        Assert.Equal(3, array.Count);
        Assert.Equal("youtube:@offline_c", array[0]!["id"]!.GetValue<string>());
        Assert.Equal("2026-11-14T00:00:00Z", array[0]!["lastStreamedAt"]!.GetValue<string>());
        Assert.Equal("twitch:live_b", array[1]!["id"]!.GetValue<string>());
        Assert.Equal(42, array[1]!["customExtraField"]!.GetValue<int>());
        Assert.Equal("@legacy_offline_a", array[2]!["id"]!.GetValue<string>());
        Assert.Equal("vidA", array[2]!["lastNotifiedVideoId"]!.GetValue<string>());
    }

    [Fact]
    public void ReorderChannelsJson_PreservesNonAsciiUtf8CharactersUnescaped()
    {
        const string inputJson = """
        [
          { "id": "youtube:@first", "platform": "youtube", "name": "兎田ぺこら 🎉", "handle": "@first" },
          { "id": "youtube:@second", "platform": "youtube", "name": "Caitlyn & Co", "handle": "@second" }
        ]
        """;

        string outputJson = ChannelOrderPolicy.ReorderChannelsJson(
            inputJson,
            ["youtube:@second", "youtube:@first"]);

        Assert.Contains("兎田ぺこら 🎉", outputJson);
        Assert.Contains("Caitlyn & Co", outputJson);
        Assert.DoesNotContain("\\u514E", outputJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\u0026", outputJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReorderChannelsJson_ReturnsOriginalJsonWhenInvalidOrNotArray()
    {
        Assert.Equal("not json", ChannelOrderPolicy.ReorderChannelsJson("not json", ["youtube:@a"]));
        Assert.Equal("{}", ChannelOrderPolicy.ReorderChannelsJson("{}", ["youtube:@a"]));
    }

    [Fact]
    public void PersistSectionOrder_WritesAtomicallyUnderLockAndIgnoresMissingOrMalformedFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "vremind-order-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Missing directory/file does not throw
            ChannelOrderPolicy.PersistSectionOrder(tempDir, ["youtube:@a", "youtube:@b"]);
            Assert.False(File.Exists(Path.Combine(tempDir, "channels.json")));

            Directory.CreateDirectory(tempDir);
            string channelsPath = Path.Combine(tempDir, "channels.json");

            // Invalid/empty file does not throw or overwrite
            File.WriteAllText(channelsPath, "");
            ChannelOrderPolicy.PersistSectionOrder(tempDir, ["youtube:@a", "youtube:@b"]);
            Assert.Equal("", File.ReadAllText(channelsPath));

            File.WriteAllText(channelsPath, """
            [
              { "id": "youtube:@first", "platform": "youtube", "name": "First", "handle": "@first" },
              { "id": "youtube:@second", "platform": "youtube", "name": "Second", "handle": "@second" }
            ]
            """);

            ChannelOrderPolicy.PersistSectionOrder(tempDir, ["youtube:@second", "youtube:@first"]);

            var reordered = JsonNode.Parse(File.ReadAllText(channelsPath))!.AsArray();
            Assert.Equal("youtube:@second", reordered[0]!["id"]!.GetValue<string>());
            Assert.Equal("youtube:@first", reordered[1]!["id"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PersistSectionOrder_WaitsAndRetriesWhenLockTemporarilyHeld()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "vremind-lock-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            string channelsPath = Path.Combine(tempDir, "channels.json");
            string lockPath = Path.Combine(tempDir, ".vremind.lock");

            File.WriteAllText(channelsPath, """
            [
              { "id": "youtube:@first", "platform": "youtube", "name": "First", "handle": "@first" },
              { "id": "youtube:@second", "platform": "youtube", "name": "Second", "handle": "@second" }
            ]
            """);

            using var holderStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            holderStream.Lock(0, 1);
            bool unlocked = false;
            try
            {
                var persistTask = Task.Run(() =>
                    ChannelOrderPolicy.PersistSectionOrder(tempDir, ["youtube:@second", "youtube:@first"]));

                await Task.Delay(80);
                Assert.False(persistTask.IsCompleted);

                holderStream.Unlock(0, 1);
                unlocked = true;

                await persistTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                if (!unlocked)
                {
                    try { holderStream.Unlock(0, 1); } catch { }
                }
            }

            var reordered = JsonNode.Parse(File.ReadAllText(channelsPath))!.AsArray();
            Assert.Equal("youtube:@second", reordered[0]!["id"]!.GetValue<string>());
            Assert.Equal("youtube:@first", reordered[1]!["id"]!.GetValue<string>());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
