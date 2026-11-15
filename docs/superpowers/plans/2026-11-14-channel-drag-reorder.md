# Channel Drag-to-Reorder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enable users to drag and reorder watched channels vertically within each status section (`LIVE NOW`, `UPCOMING`, `OFFLINE`) in the tray popover, persisting the new order directly from C# into `channels.json` while keeping `VRemindCore` and `VRemindCoreCLI` untouched.

**Architecture:** `ChannelOrderPolicy` in `VRemindWin.Core` encapsulates pure drag threshold detection, target slot and sibling translation math, optimistic section reordering, same-day custom order projection across CLI refreshes, and `.vremind.lock`-synchronized slot-preserving `JsonArray` reordering on `channels.json`. `StreamMonitorCoordinator` and `PopoverViewModel` coordinate immediate in-memory state updates with `ICoreCliBridge.ReorderChannelsAsync`, while `PopoverView` handles pointer-captured vertical row dragging (`CapturePointer` + `TranslateTransform`) inside each section's `ItemsControl`.

**Tech Stack:** C# / .NET 9 (`net9.0-windows10.0.19041.0`), `System.Text.Json.Nodes`, WinUI 3 (`Microsoft.WindowsAppSDK` 1.6), xUnit (`VRemindWin.Tests`).

**Spec:** `docs/superpowers/specs/2026-11-14-channel-drag-reorder-design.md`

## Global Constraints

- `VRemindCore/` and `VRemindCoreCLI/` must have zero modifications.
- Drag activation threshold is `5.0` DIP vertically (`DragActivationThresholdDip = 5.0`); movements below `5.0` DIP remain normal clicks that open the stream URL and dismiss the popover.
- Reordering is strictly intra-section (`live`, `upcoming`, or `offline`); cross-section dragging is disallowed.
- Rewriting `channels.json` from C# must preserve every JSON property of every channel object verbatim using `JsonNode` / `JsonArray` and synchronize via `<dataDir>\.vremind.lock`.
- A manual reorder stays projected onto incoming CLI state/refresh envelopes for the remainder of the current local calendar day (`DateOnly`), expiring automatically when the local date rolls over to the next day.

## Review Focus

- Reordering one section (e.g., `offline`) when `channels.json` contains interleaved channels from other sections (`[offlineA, liveB, offlineC]`) must only swap the `offline` slot indices (`0` and `2` -> `[offlineC, liveB, offlineA]`) without disturbing `liveB` at index `1`.
- Legacy YouTube channel entries in `channels.json` whose `"id"` was stored without a platform prefix (e.g., `"@handle"` while the runtime `CliChannelItem.Id` is `"youtube:@handle"`) must still match and reorder cleanly.
- If `channels.json` does not exist yet or contains invalid/empty JSON when `PersistSectionOrder` is called, `PersistSectionOrder` must no-op gracefully without throwing or creating a corrupt file.
- When a background CLI `RefreshAsync` returns a section in default CLI order on the same calendar day after a manual reorder, `StreamMonitorCoordinator` must keep the user's custom relative order; once the calendar day advances to the next day, the coordinator must fall back to the CLI's returned section order.
- Out-of-bounds or no-op reorder indices (`fromIndex == toIndex`, negative indices, or indices `>= Count`) must return the original list unchanged without throwing.

---

### Task 1: Pure `ChannelOrderPolicy`, `CoreCliBridge` JSON Persistence, Coordinator/ViewModel Wiring & Unit Tests

**Files:**
- Create: `VRemindWin/src/VRemindWin.Core/Services/ChannelOrderPolicy.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/Services/ICoreCliBridge.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/Services/CoreCliBridge.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/Services/StreamMonitorCoordinator.cs`
- Modify: `VRemindWin/src/VRemindWin.Core/ViewModels/PopoverViewModel.cs`
- Create: `VRemindWin/tests/VRemindWin.Tests/ChannelOrderTests.cs`
- Modify: `VRemindWin/tests/VRemindWin.Tests/CoordinatorAndViewModelTests.cs`

**Interfaces:**
- Produces:
  - `VRemindWin.Core.Services.DragLayoutStep`:
    ```csharp
    public readonly record struct DragLayoutStep(
        int TargetIndex,
        double ClampedDragTranslateY,
        IReadOnlyList<double> ItemTranslateY);
    ```
  - `VRemindWin.Core.Services.ChannelOrderPolicy`:
    - `public const double DragActivationThresholdDip = 5.0;`
    - `public static bool ExceedsDragThreshold(double deltaY)`
    - `public static DragLayoutStep CalculateDragLayout(int draggedIndex, double deltaY, IReadOnlyList<double> itemHeights, double itemSpacing = 0.0)`
    - `public static IReadOnlyList<CliChannelItem> ReorderSectionItems(IReadOnlyList<CliChannelItem> items, int fromIndex, int toIndex)`
    - `public static CliStateEnvelope ApplySectionOrder(CliStateEnvelope state, string sectionKind, IReadOnlyList<CliChannelItem> reorderedSection)`
    - `public static CliStateEnvelope ApplyCustomOrder(CliStateEnvelope state, IReadOnlyList<string> orderedIds)`
    - `public static string ReorderChannelsJson(string jsonContent, IReadOnlyList<string> orderedSectionIds)`
    - `public static void PersistSectionOrder(string dataDir, IReadOnlyList<string> orderedSectionIds)`
    - `public static string ResolveDefaultDataDirectory(string? configuredDataDir)`
  - `ICoreCliBridge`:
    - `Task ReorderChannelsAsync(IReadOnlyList<string> orderedSectionIds, CancellationToken ct = default);`
  - `StreamMonitorCoordinator`:
    - Optional constructor parameter `Func<DateTimeOffset>? clock = null`
    - `public async Task ReorderSectionAsync(string sectionKind, int fromIndex, int toIndex, CancellationToken ct = default)`
  - `PopoverViewModel`:
    - `public Task ReorderSectionAsync(string sectionKind, int fromIndex, int toIndex)`

- [ ] **Step 1: Write failing unit tests in `VRemindWin/tests/VRemindWin.Tests/ChannelOrderTests.cs` and `CoordinatorAndViewModelTests.cs`**

Create `VRemindWin/tests/VRemindWin.Tests/ChannelOrderTests.cs`:

```csharp
using System.Text.Json.Nodes;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using VRemindWin.Core.ViewModels;
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

        // Dragging item 0 down by 60px crosses midpoint of item 1 (at +50px), targeting index 1
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
    public void PersistSectionOrder_WritesAtomicallyUnderLockAndIgnoresMissingFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "vremind-order-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Missing directory/file does not throw
            ChannelOrderPolicy.PersistSectionOrder(tempDir, ["youtube:@a", "youtube:@b"]);
            Assert.False(File.Exists(Path.Combine(tempDir, "channels.json")));

            Directory.CreateDirectory(tempDir);
            string channelsPath = Path.Combine(tempDir, "channels.json");
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
}
```

And in `VRemindWin/tests/VRemindWin.Tests/CoordinatorAndViewModelTests.cs`:
- Add `public Func<IReadOnlyList<string>, CancellationToken, Task>? OnReorderChannels { get; set; }` and `public Task ReorderChannelsAsync(IReadOnlyList<string> orderedSectionIds, CancellationToken ct = default)` to `StubCoreCliBridge`.
- Add test `ReorderSectionAsync_PersistsOrderAndMaintainsDailyProjectionUntilNextDay`:
  - Sets up a `StreamMonitorCoordinator` with an adjustable `DateTimeOffset currentNow` clock and 3 offline channels `[A, B, C]`.
  - Calls `await vm.ReorderSectionAsync("offline", fromIndex: 2, toIndex: 0)` -> verifies `vm.OfflineChannels` immediately becomes `[C, A, B]` and `bridge` received `["youtube:@c", "youtube:@a", "youtube:@b"]`.
  - Simulates a background `RefreshAllAsync()` on the **same day** where the CLI returns `[A, B, C]` -> verifies `vm.OfflineChannels` stays `[C, A, B]`.
  - Advances `currentNow` by 1 day (`currentNow = currentNow.AddDays(1)`) and runs `RefreshAllAsync()` -> verifies `vm.OfflineChannels` now reflects the CLI's `[A, B, C]`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj --filter "FullyQualifiedName~ChannelOrderTests"`
Expected: FAIL with compiler error (`ChannelOrderPolicy` does not exist).

- [ ] **Step 3: Implement `ChannelOrderPolicy`, `ICoreCliBridge`, `CoreCliBridge`, `StreamMonitorCoordinator`, and `PopoverViewModel`**

- Create `VRemindWin/src/VRemindWin.Core/Services/ChannelOrderPolicy.cs` implementing all members in **Interfaces**.
  - In `CalculateDragLayout`, compute each item's rest center `centerY[i]`, clamp `deltaY` to `[-(centerY[draggedIndex] - centerY[0]), centerY[^1] - centerY[draggedIndex]]`, find `targetIndex` from `draggedCenter = centerY[draggedIndex] + clampedDeltaY` crossing sibling midpoints, and populate `ItemTranslateY`.
  - In `ReorderChannelsJson`, match each channel node's `"id"` (case-insensitive, plus fallback matching `"youtube:" + id` when `id` has no `:` prefix) against `orderedSectionIds`, and write the matched nodes into their original array slot indices in the new order.
  - In `PersistSectionOrder`, return early if `channels.json` does not exist; otherwise acquire `.vremind.lock` with `FileShare.ReadWrite`, read and transform `channels.json`, and atomically overwrite `channels.json` via a temp file (`File.Move(tempPath, channelsPath, overwrite: true)`).
- Update `ICoreCliBridge.cs` and `CoreCliBridge.cs` to add `ReorderChannelsAsync(IReadOnlyList<string> orderedSectionIds, CancellationToken ct = default)` calling `ChannelOrderPolicy.PersistSectionOrder(ChannelOrderPolicy.ResolveDefaultDataDirectory(_dataDir), orderedSectionIds)`.
- Update `StreamMonitorCoordinator.cs` to accept `Func<DateTimeOffset>? clock = null` (defaulting to `() => DateTimeOffset.Now`), store `_customOrderDate` (`DateOnly?`) and `_customOrderIds` (`List<string>`), apply `ChannelOrderPolicy.ApplyCustomOrder` when setting `CurrentState` on the same calendar day (clearing `_customOrderIds` when the date changes or on `ResetFactoryDefaultsAsync` / `RestoreBackupAsync`), and implement `ReorderSectionAsync(string sectionKind, int fromIndex, int toIndex, CancellationToken ct = default)`.
- Update `PopoverViewModel.cs` to expose `public Task ReorderSectionAsync(string sectionKind, int fromIndex, int toIndex) => _coordinator.ReorderSectionAsync(sectionKind, fromIndex, toIndex);`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj --filter "FullyQualifiedName~ChannelOrderTests|FullyQualifiedName~CoordinatorAndViewModelTests"`
Expected: PASS (all tests in `ChannelOrderTests` and `CoordinatorAndViewModelTests` pass).

- [ ] **Step 5: Commit Task 1**

```powershell
& "C:\Program Files\Git\cmd\git.exe" add VRemindWin/src/VRemindWin.Core/ VRemindWin/tests/VRemindWin.Tests/
& "C:\Program Files\Git\cmd\git.exe" commit -m "feat: add ChannelOrderPolicy and coordinator section reorder support"
```

---

### Task 2: Pointer-Captured Intra-Section Drag UI in `PopoverView`

**Files:**
- Modify: `VRemindWin/src/VRemindWin/Views/PopoverView.xaml`
- Modify: `VRemindWin/src/VRemindWin/Views/PopoverView.xaml.cs`

**Interfaces:**
- Consumes:
  - `ChannelOrderPolicy.ExceedsDragThreshold`, `ChannelOrderPolicy.CalculateDragLayout`
  - `PopoverViewModel.ReorderSectionAsync(string sectionKind, int fromIndex, int toIndex)`

- [ ] **Step 1: Wire pointer events and section `ItemsControl` names in `VRemindWin/src/VRemindWin/Views/PopoverView.xaml`**

- On the root `Grid` inside `ChannelRowTemplate` (lines 20–28), add:
  - `PointerPressed="ChannelRow_PointerPressed"`
  - `PointerMoved="ChannelRow_PointerMoved"`
  - `PointerReleased="ChannelRow_PointerReleased"`
  - `PointerCanceled="ChannelRow_PointerCanceled"`
  - `PointerCaptureLost="ChannelRow_PointerCaptureLost"`
- Add `x:Name` and `Tag` to the three section `ItemsControl` elements:
  - `x:Name="LiveItemsControl" Tag="live"` (around line 273)
  - `x:Name="UpcomingItemsControl" Tag="upcoming"` (around line 288)
  - `x:Name="OfflineItemsControl" Tag="offline"` (around line 303)

- [ ] **Step 2: Implement pointer-captured vertical drag in `VRemindWin/src/VRemindWin/Views/PopoverView.xaml.cs`**

- Add drag state tracking in `PopoverView`:
  - When `ChannelRow_PointerPressed` fires with left mouse button or primary touch/pen contact (and `e.OriginalSource` is not inside `DeleteButton`), locate the owning section `ItemsControl` (`LiveItemsControl`, `UpcomingItemsControl`, or `OfflineItemsControl`), resolve the row's index and all sibling row `Grid`s in that `ItemsControl`, and record the initial pointer Y relative to the section `ItemsControl`.
  - In `ChannelRow_PointerMoved`, if the pressed row belongs to a section with `>= 2` rows and `ChannelOrderPolicy.ExceedsDragThreshold(deltaY)` is met, enter active drag (`_isDragging = true`, `_suppressNextTap = true`, `rowGrid.CapturePointer(e.Pointer)`, hide `DeleteButton`, set `Canvas.SetZIndex(rowPart, 10)` and `rowGrid.Opacity = 0.92`).
  - While `_isDragging`, compute `ChannelOrderPolicy.CalculateDragLayout(_draggedIndex, deltaY, rowHeights)` and set each sibling row's `RenderTransform = new TranslateTransform { Y = step.ItemTranslateY[i] }`.
  - In `ChannelRow_PointerReleased`, `ChannelRow_PointerCanceled`, `ChannelRow_PointerCaptureLost`, and `SetPopoverVisible(false)`:
    - Reset `RenderTransform`, `Opacity = 1.0`, and `Canvas.SetZIndex(..., 0)` on all rows in the active section, release pointer capture if held, and when ending via `PointerReleased` with `targetIndex != _draggedIndex`, invoke `_ = RunSafelyAsync(() => ViewModel.ReorderSectionAsync(sectionKind, fromIndex, targetIndex))`.
  - In `ChannelRow_Tapped`: if `_suppressNextTap` is set, clear it, mark `e.Handled = true`, and return without opening the URL.

- [ ] **Step 3: Build and run the full test suite**

Run:
```powershell
dotnet build VRemindWin\VRemindWin.sln -c Debug -p:Platform=x64
dotnet test VRemindWin\VRemindWin.sln -c Debug --no-build
```
Expected: Solution builds with `0 Warning(s), 0 Error(s)`, and all xUnit tests pass (`0 Failed`).

- [ ] **Step 4: Commit Task 2**

```powershell
& "C:\Program Files\Git\cmd\git.exe" add VRemindWin/src/VRemindWin/Views/PopoverView.xaml VRemindWin/src/VRemindWin/Views/PopoverView.xaml.cs
& "C:\Program Files\Git\cmd\git.exe" commit -m "feat(ui): add pointer-captured drag reordering to PopoverView"
```
