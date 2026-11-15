# VRemind for Windows (`VRemind-win`) Channel Drag-to-Reorder Design

- **Date:** 2026-11-14
- **Scope:** Small Change (Intra-section drag-to-reorder for watched channels in `PopoverView`, persisted purely from C# to `channels.json` with `VRemindCore` and `VRemindCoreCLI` untouched)

---

## 1. Intent and Why
Watched channels in `VRemind-win` currently stay in the order they were added. Letting users drag channels up and down inside the tray popover (`LIVE NOW`, `UPCOMING`, and `OFFLINE` sections) lets them prioritize favorite streamers at the top of each section—making the Windows build more capable than the original macOS version while keeping the shared `VRemindCore` backend completely untouched.

---

## 2. Goals, Non-Goals, and Anti-Goals

### Goals
- **Direct Intra-Section Drag Reordering:** Users can press and drag any channel row vertically within its current status section (`LIVE NOW`, `UPCOMING`, or `OFFLINE`) to reorder it.
- **Seamless Click vs. Drag Disambiguation:** A normal click/tap (`< 5px` vertical pointer movement) still opens the channel's stream URL and dismisses the popover; clicking the hover trash button still removes the channel.
- **Live Visual Feedback During Drag:** The dragged row follows the pointer vertically within its section bounds (with elevated `ZIndex` and subtle opacity), while sibling rows in that section shift up/down (`TranslateTransform`) to preview the drop slot.
- **Persistent Watchlist Order (Zero Backend Changes):** Dropping a row updates the in-memory state immediately (zero UI flicker), persists the new relative order directly into `%APPDATA%\VRemind\channels.json` from C#, and maintains a daily client-side order projection so refreshes throughout the current day preserve the exact dragged order inside each section.

### Non-Goals
- **Cross-Section Dragging:** Channels are grouped by live status (`LIVE NOW`, `UPCOMING`, `OFFLINE`), so dragging a row from `OFFLINE` into `LIVE NOW` is not allowed.
- **Modifying `VRemindCore` or `VRemindCoreCLI`:** Both Swift packages remain 100% untouched.

### Anti-Goals
- **Flyout Auto-Dismiss During Drag:** Dragging a channel must never cause `PopoverWindow` to lose activation and auto-hide mid-drag.
- **Data Loss in `channels.json`:** Reordering `channels.json` from C# must preserve every JSON field of every channel object verbatim and synchronize with `.vremind.lock` so concurrent `vremind-core.exe` refreshes cannot corrupt or overwrite the file.

---

## 3. Constraints
- **Backend Immutability:** `VRemindCore/` (and `VRemindCoreCLI/`) must have zero modifications.
- **Testability:** Pure reorder math (drag threshold, target index calculation, section item reordering, daily order projection, and slot-preserving `channels.json` array reordering) must live in `VRemindWin.Core` and be covered by xUnit tests in `VRemindWin.Tests`.

---

## 4. Approaches Considered

1. **Pointer-Captured Drag on Existing `ItemsControl`s + C# Slot-Preserving `channels.json` Persistence *(Selected)*:** Keep the lightweight `ItemsControl` per section in `PopoverView.xaml`, handle vertical pointer drag (`CapturePointer` + `TranslateTransform`) directly on row elements, update `StreamMonitorCoordinator.CurrentState` optimistically, and rewrite the reordered section slots directly in `channels.json` under `.vremind.lock` using `System.Text.Json.Nodes.JsonArray`. *Why chosen:* Touches zero files in `VRemindCore` / `VRemindCoreCLI`, preserves exact row layout inside the outer `ScrollViewer`, and avoids OLE drag focus loss on `PopoverWindow`.
2. **WinUI `ListView` (`CanReorderItems="True"`) per Section:** Replace the three `ItemsControl`s with `ListView`s. *Trade-off:* Nested `ListView`s inside an outer `ScrollViewer` fight over scrolling, inject unwanted `ListViewItem` chrome, and use OLE drag-and-drop which can trigger `PopoverWindow` deactivation.
3. **Dedicated Hover Grip Handle (`⋮⋮`) Only:** Only start drags from a hover grip icon. *Trade-off:* Smaller hit target and extra visual clutter on compact 36px rows compared to dragging anywhere on the row with a `5px` movement threshold.

---

## 5. Decided Design & Builder Calls

- **Pure Reorder Policy & JSON Store (`VRemindWin.Core/Services/ChannelOrderPolicy.cs`) *(Builder call)*:**
  - `DragActivationThresholdDip = 5.0`: Vertical pointer distance required before a row press transitions from a potential tap into an active drag.
  - `CalculateTargetIndex(int draggedIndex, double deltaY, IReadOnlyList<double> itemHeights)`: Computes the clamped destination index within `[0, itemHeights.Count - 1]` as the dragged row's vertical center crosses sibling row midpoints, and computes each sibling row's vertical shift offset (`+draggedHeight`, `-draggedHeight`, or `0`).
  - `ReorderSectionItems(IReadOnlyList<CliChannelItem> sectionItems, int fromIndex, int toIndex)` & `ApplySectionOrder(CliStateEnvelope state, string sectionKind, IReadOnlyList<CliChannelItem> reorderedSection)`: Returns the updated section list and `CliStateEnvelope`.
  - `ReorderChannelsJson(string jsonContent, IReadOnlyList<string> orderedSectionIds)`: Parses `channels.json` via `JsonNode.Parse` (`JsonArray`), locates the array indices whose `"id"` (or legacy YouTube `"handle"`) matches the section's channel IDs, and places the reordered `JsonNode` objects into those exact index slots while preserving all JSON properties and formatting (`WriteIndented = true`).
  - `PersistSectionOrder(string dataDir, IReadOnlyList<string> orderedSectionIds)`: Acquires an exclusive lock on `<dataDir>\.vremind.lock` (matching `CliRunner.withDataDirLock`), reorders `<dataDir>\channels.json` via `ReorderChannelsJson`, and writes atomically.
- **Coordinator & ViewModel Wiring (`ICoreCliBridge.cs`, `CoreCliBridge.cs`, `StreamMonitorCoordinator.cs`, `PopoverViewModel.cs`) *(Builder call)*:**
  - Add `Task ReorderChannelsAsync(IReadOnlyList<string> orderedSectionIds, CancellationToken ct = default)` on `ICoreCliBridge` (implemented in `CoreCliBridge` via `ChannelOrderPolicy.PersistSectionOrder` against the configured data directory, with zero `vremind-core.exe` changes).
  - Add `Task ReorderSectionAsync(string sectionKind, int fromIndex, int toIndex, CancellationToken ct = default)` on `StreamMonitorCoordinator` (and exposed on `PopoverViewModel`), which increments `_statusEpoch`, updates `CurrentState` immediately with the reordered section, records the active calendar-day order projection so subsequent CLI state/refresh envelopes on the same day preserve that exact relative order, and awaits `_bridge.ReorderChannelsAsync(...)`.
- **Pointer-Captured Drag UI (`PopoverView.xaml` & `PopoverView.xaml.cs`) *(User + Builder call)*:**
  - Name the three section `ItemsControl`s (`LiveItemsControl`, `UpcomingItemsControl`, `OfflineItemsControl`) and attach `PointerPressed`, `PointerMoved`, `PointerReleased`, and `PointerCaptureLost` handlers to the root `Grid` in `ChannelRowTemplate`.
  - When a left-button / touch drag exceeds `5px` vertically (and the section has at least 2 items), capture the pointer, suppress `ChannelRow_Tapped`, elevate the dragged row (`Canvas.ZIndex = 10`, `Opacity = 0.92`, `TranslateTransform.Y`), shift intermediate sibling rows via `TranslateTransform.Y`, and commit the new order on pointer release.
