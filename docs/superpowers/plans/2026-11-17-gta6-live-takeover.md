# GTA 6 Live Takeover Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a watched channel is live with a GTA 6 title, turn every live indicator in the Windows app (row, popover headers, tray icon) Miami Vice purple.

**Architecture:** Detection runs client-side in `VRemindWin.Core`. It adds a `"gta6"` value to the existing `EffectiveVisualKind` string, a `LiveHeaderVisualKind` on the view model, and a three-state tray policy. The WinUI layer maps `"gta6"` to a static purple gradient brush through the existing status-kind converters and picks a new purple tray `.ico`. The CLI JSON contract is untouched.

**Tech Stack:** .NET 9, WinUI 3 (Windows App SDK 1.6), xUnit 2.9, Win32 `Shell_NotifyIconW`, PowerShell for icon generation.

**Spec:** `docs/superpowers/specs/2026-11-17-gta6-live-takeover-design.md`

## Global Constraints

- Do not modify anything under `VRemindCore/`, `VRemindCoreCLI/`, `VRemind/` (macOS), or `artifacts/cli/vremind-core.exe`.
- Add no new JSON properties to `CliContracts.cs` records. New members are computed `=>` properties only.
- The trigger is live only. Upcoming or offline items with GTA 6 titles never produce `"gta6"`.
- Priority on shared indicators: `"gta6"` > `"celebration"` > base status.
- Gradient: `#8B5CF6` (offset 0) → `#D946EF` (offset 1), StartPoint (0,0) → EndPoint (1,1), static (not animated).
- Tray tooltip copy: `VRemind`, `VRemind (Live now)`, `VRemind (Live now - GTA 6)`.
- Tray purple (my call, recolor target): `#A855F7`.
- Test command base: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`
- App build command: `dotnet build VRemindWin\src\VRemindWin\VRemindWin.csproj -p:Platform=x64`

## Review Focus

1. Titles with hashtags or punctuation (`#GTA6`, `GTA 6!!`, `[GTA VI]`, `gta-6`) should match. → Task 1 test `Matches_WithPunctuationAndHashtags`.
2. Near misses (`GTA 60`, `GTA VII`, `GTA 5`, `MEGTA6`, `GTA 6000`) must not match. → Task 1 test `DoesNotMatch_NearMisses`.
3. Mixed live list (one GTA 6 stream plus one normal stream): the header is purple and the non-GTA row stays red. → Task 3 test `MixedLiveList_HeaderIsGta6_OtherRowStaysLive`.
4. A GTA 6 stream ends on the next refresh: the header and tray revert, and `PropertyChanged` fires for the new properties. → Task 3 test `Gta6StreamEnds_HeaderRevertsAndRaisesChange`, Task 4 test `Resolve_RevertsWhenGta6StreamEnds`.
5. A live item with a null or empty `StreamTitle` from the CLI shows normal live with no exception. → Task 2 test `LiveWithNullTitle_IsPlainLive`.

---

### Task 1: `Gta6Detector`

**Files:**
- Create: `VRemindWin/src/VRemindWin.Core/Services/Gta6Detector.cs`
- Test: `VRemindWin/tests/VRemindWin.Tests/Gta6DetectorTests.cs`

**Interfaces:**
- Produces: `public static class Gta6Detector { public static bool IsMatch(string? title); }` in namespace `VRemindWin.Core.Services`.

- [ ] **Step 1: Write failing tests** (xUnit `[Theory]` with `[InlineData]`)

```csharp
[Theory]
[InlineData("GTA 6 EARLY GAMEPLAY")] [InlineData("gta6 day one")] [InlineData("GTA VI hype")]
[InlineData("GTAVI first look")] [InlineData("Grand Theft Auto 6 stream")]
[InlineData("grand theft auto vi!!")] [InlineData("GTA  6")]
public void Matches_AllVariants(string title) => Assert.True(Gta6Detector.IsMatch(title));

[Theory]
[InlineData("#GTA6 LETS GO")] [InlineData("GTA 6!!")] [InlineData("[GTA VI] part 1")] [InlineData("gta-6 time")]
public void Matches_WithPunctuationAndHashtags(string title) => Assert.True(Gta6Detector.IsMatch(title));

[Theory]
[InlineData("GTA 60 fps")] [InlineData("GTA VII when")] [InlineData("GTA 5 RP")]
[InlineData("MEGTA6")] [InlineData("GTA 6000")] [InlineData("Grand Theft Auto V")]
public void DoesNotMatch_NearMisses(string title) => Assert.False(Gta6Detector.IsMatch(title));

[Theory] [InlineData(null)] [InlineData("")] [InlineData("   ")]
public void DoesNotMatch_EmptyOrNull(string? title) => Assert.False(Gta6Detector.IsMatch(title));
```

- [ ] **Step 2: Run, expect FAIL** (compile error: `Gta6Detector` not found)

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj --filter FullyQualifiedName~Gta6DetectorTests`

- [ ] **Step 3: Implement `Gta6Detector.IsMatch`**

Use one static compiled `Regex` with `RegexOptions.IgnoreCase | RegexOptions.CultureInvariant`. Suggested pattern: `\b(?:GTA|Grand\s+Theft\s+Auto)[\s-]*(?:6|VI)\b`. The trailing `\b` rejects `60`/`VII`, and the leading `\b` rejects `MEGTA6`. Return `false` for null or whitespace before matching.

- [ ] **Step 4: Run, expect PASS** (same command)

- [ ] **Step 5: Commit**

```bash
git add VRemindWin/src/VRemindWin.Core/Services/Gta6Detector.cs VRemindWin/tests/VRemindWin.Tests/Gta6DetectorTests.cs
git commit -m "feat: add GTA 6 stream title detector"
```

---

### Task 2: `"gta6"` visual kind on `CliChannelItem`

**Files:**
- Modify: `VRemindWin/src/VRemindWin.Core/Models/CliContracts.cs:21-23`
- Test: `VRemindWin/tests/VRemindWin.Tests/Gta6ChannelItemTests.cs`

**Interfaces:**
- Consumes: `Gta6Detector.IsMatch(string?)`
- Produces: `CliChannelItem.IsGta6Live` (bool), and `CliChannelItem.EffectiveVisualKind` now returns `"gta6"` | `"celebration"` | `StatusKind`.

- [ ] **Step 1: Write failing tests.** Add a local helper `Item(string statusKind, string? title, bool isCelebration = false)` that builds a `CliChannelItem` with the other fields as in `CoordinatorAndViewModelTests.MakeChannel`.

```csharp
[Fact] public void LiveWithGta6Title_IsGta6()
{ var i = Item("live", "GTA 6 early gameplay"); Assert.True(i.IsGta6Live); Assert.Equal("gta6", i.EffectiveVisualKind); }

[Fact] public void UpcomingWithGta6Title_IsNotGta6()
{ var i = Item("upcoming", "GTA 6 tomorrow"); Assert.False(i.IsGta6Live); Assert.Equal("upcoming", i.EffectiveVisualKind); }

[Fact] public void Gta6BeatsCelebration()
    => Assert.Equal("gta6", Item("live", "GTA 6 birthday stream", isCelebration: true).EffectiveVisualKind);

[Fact] public void CelebrationWithoutGta6_Unchanged()
    => Assert.Equal("celebration", Item("live", "birthday stream", isCelebration: true).EffectiveVisualKind);

[Fact] public void LiveWithNullTitle_IsPlainLive()
{ var i = Item("live", null); Assert.False(i.IsGta6Live); Assert.Equal("live", i.EffectiveVisualKind); Assert.Equal("LIVE", i.StatusBadgeText); }
```

- [ ] **Step 2: Run, expect FAIL** — `--filter FullyQualifiedName~Gta6ChannelItemTests`

- [ ] **Step 3: Implement.** Add `public bool IsGta6Live => IsLive && Gta6Detector.IsMatch(StreamTitle);` and change `EffectiveVisualKind` to `IsGta6Live ? "gta6" : IsCelebration ? "celebration" : StatusKind`. Add `using VRemindWin.Core.Services;`.

- [ ] **Step 4: Run the full test suite, expect PASS.** Existing JSON and celebration tests must stay green.

Run: `dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`

- [ ] **Step 5: Commit** — `git commit -m "feat: add gta6 visual kind to channel items"`

---

### Task 3: `PopoverViewModel` GTA 6 state

**Files:**
- Modify: `VRemindWin/src/VRemindWin.Core/ViewModels/PopoverViewModel.cs:103,119-121,197-216`
- Test: `VRemindWin/tests/VRemindWin.Tests/CoordinatorAndViewModelTests.cs` (add tests next to the existing celebration test around line 485; reuse `StubCoreCliBridge`, `FakeToastDispatcher`, `MakeOkResponse`)

**Interfaces:**
- Consumes: `CliChannelItem.IsGta6Live`
- Produces:
  - `public bool IsLiveSectionGta6 { get; }`
  - `public string LiveHeaderVisualKind { get; }` returns `"gta6"` | `"celebration"` | `"live"`
  - `IsPopoverCelebrating` becomes `coordinator flag || IsLiveSectionGta6`

- [ ] **Step 1: Write failing tests.** Build live items with `MakeChannel(...) with { StreamTitle = "GTA 6 early gameplay" }`.

```csharp
[Fact] public async Task Gta6Live_SetsHeaderKindAndCelebrates()
// state: live=[gta6Item], IsPopoverCelebrating=false
// Assert: vm.IsLiveSectionGta6, vm.LiveHeaderVisualKind == "gta6", vm.IsPopoverCelebrating == true

[Fact] public async Task MixedLiveList_HeaderIsGta6_OtherRowStaysLive()
// live=[gta6Item, plainLiveItem]
// Assert: vm.LiveHeaderVisualKind == "gta6"; vm.LiveChannels[1].EffectiveVisualKind == "live"

[Fact] public async Task CelebrationOnly_HeaderKindIsCelebration()
// live=[item with IsCelebration=true, title "birthday stream"] → LiveHeaderVisualKind == "celebration", IsLiveSectionGta6 == false

[Fact] public async Task NoGta6_HeaderKindIsLive_AndCelebratingFollowsCore()
// live=[plainLiveItem], IsPopoverCelebrating=false → "live", vm.IsPopoverCelebrating == false

[Fact] public async Task Gta6StreamEnds_HeaderRevertsAndRaisesChange()
// InitializeAsync with gta6 state; then OnRefresh returns plain live state; subscribe vm.PropertyChanged and collect names;
// await coordinator.RefreshAllAsync()
// Assert: vm.LiveHeaderVisualKind == "live"; names contains "LiveHeaderVisualKind", "IsLiveSectionGta6", "IsPopoverCelebrating"
```

- [ ] **Step 2: Run, expect FAIL** — `--filter FullyQualifiedName~CoordinatorAndViewModelTests`

- [ ] **Step 3: Implement** the three properties above. Add `RaisePropertyChanged(nameof(IsLiveSectionGta6))` and `RaisePropertyChanged(nameof(LiveHeaderVisualKind))` to `RaiseStatePropertiesChanged`.

- [ ] **Step 4: Run the full suite, expect PASS**

- [ ] **Step 5: Commit** — `git commit -m "feat: expose GTA 6 live header state on popover view model"`

---

### Task 4: Tray visual state policy (Core)

**Files:**
- Create: `VRemindWin/src/VRemindWin.Core/Services/TrayVisualStatePolicy.cs`
- Test: `VRemindWin/tests/VRemindWin.Tests/TrayVisualStatePolicyTests.cs`

**Interfaces:**
- Consumes: `CliChannelItem.IsGta6Live`, `CliStateEnvelope`
- Produces:
  - `public enum TrayVisualState { Normal, Live, Gta6Live }`
  - `public static class TrayVisualStatePolicy { public static TrayVisualState Resolve(CliStateEnvelope state); public static string TooltipFor(TrayVisualState state); }`

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public void Resolve_NoLive_IsNormal()        // CliStateEnvelope.Empty → Normal
[Fact] public void Resolve_PlainLive_IsLive()       // live=[plain] → Live
[Fact] public void Resolve_AnyGta6Live_IsGta6Live() // live=[plain, gta6] → Gta6Live
[Fact] public void Resolve_UpcomingGta6Only_IsNormal() // upcoming=[gta6 title], live=[] → Normal
[Fact] public void Resolve_RevertsWhenGta6StreamEnds()  // Resolve(gta6State)==Gta6Live, then Resolve(plainState)==Live
[Theory]
[InlineData(TrayVisualState.Normal, "VRemind")]
[InlineData(TrayVisualState.Live, "VRemind (Live now)")]
[InlineData(TrayVisualState.Gta6Live, "VRemind (Live now - GTA 6)")]
public void TooltipFor_ReturnsCopy(TrayVisualState s, string expected)
```

- [ ] **Step 2: Run, expect FAIL** — `--filter FullyQualifiedName~TrayVisualStatePolicyTests`

- [ ] **Step 3: Implement.** Return `Gta6Live` if any `Sections.Live` item has `IsGta6Live`, else `Live` if `LiveCount > 0`, else `Normal`.

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Commit** — `git commit -m "feat: add tray visual state policy with GTA 6 state"`

---

### Task 5: Miami Vice brush and popover bindings (WinUI)

No unit tests: the test project references only `VRemindWin.Core`. Verification is a successful build plus the manual check in Task 6.

**Files:**
- Modify: `VRemindWin/src/VRemindWin/Helpers/UiConverters.cs` (add a provider after `RainbowGradientProvider`, add a `"gta6"` case to three converters, delete `CelebrationBadgeBrushConverter` and `CelebrationLiveHeaderBrushConverter`)
- Modify: `VRemindWin/src/VRemindWin/App.xaml:20-21` (remove those two resource entries)
- Modify: `VRemindWin/src/VRemindWin/Views/PopoverView.xaml:176,277`
- Modify: `VRemindWin/src/VRemindWin/Views/PopoverView.xaml.cs:105-108`

**Interfaces:**
- Consumes: `PopoverViewModel.LiveHeaderVisualKind`, `IsLiveSectionGta6`; `CliChannelItem.EffectiveVisualKind == "gta6"`
- Produces: `public static class MiamiViceGradientProvider { public static LinearGradientBrush SharedBrush { get; } }` (lazy, single instance)

- [ ] **Step 1: Add `MiamiViceGradientProvider`** with the stops and points from Global Constraints.

- [ ] **Step 2: Add `if (status == "gta6") return MiamiViceGradientProvider.SharedBrush;`** (OrdinalIgnoreCase, checked first) to `StatusKindToTimeBrushConverter`, `StatusKindToAvatarRingBrushConverter`, and `StatusKindToBadgeBrushConverter`.

- [ ] **Step 3: Rebind the headers to the existing string converters.** They already map `"live"` to the right reds, and `"celebration"` to rainbow:
  - Line 176, `N LIVE` pill: `Background="{x:Bind ViewModel.LiveHeaderVisualKind, Mode=OneWay, Converter={StaticResource StatusBadgeBrushConverter}}"` (live → `#DC2626`)
  - Line 277, `LIVE NOW` header: `Foreground="{x:Bind ViewModel.LiveHeaderVisualKind, Mode=OneWay, Converter={StaticResource StatusTimeBrushConverter}}"` (live → `#EF4444`)
  - Delete the now-unused `CelebrationBadgeBrushConverter` and `CelebrationLiveHeaderBrushConverter` classes and their `App.xaml` entries. Keep `CelebrationUpcomingHeaderBrushConverter`.

- [ ] **Step 4: Add `or nameof(PopoverViewModel.IsLiveSectionGta6)`** to the filter in `OnViewModelPropertyChanged`. Leave `UpdateCelebrationAnimationState` logic as is. The rainbow ticking during a GTA 6–only takeover is harmless (spec: animation not required).

- [ ] **Step 5: Build, expect `Build succeeded` with 0 errors**

Run: `dotnet build VRemindWin\src\VRemindWin\VRemindWin.csproj -p:Platform=x64`

- [ ] **Step 6: Commit** — `git commit -m "feat: Miami Vice purple brush for GTA 6 live indicators"`

---

### Task 6: Purple tray icon and tray wiring

**Files:**
- Create: `VRemindWin/tools/New-Gta6TrayIcon.ps1`
- Create: `VRemindWin/src/VRemindWin/Assets/tray-gta6.ico` (generated)
- Modify: `VRemindWin/src/VRemindWin/VRemindWin.csproj` (add a `Content` item, `PreserveNewest`, mirroring `tray-live.ico`)
- Modify: `VRemindWin/src/VRemindWin/Services/TrayIconService.cs:61-101,182-200,321-331`
- Modify: `VRemindWin/src/VRemindWin/App.xaml.cs:70-76,147-155`

**Interfaces:**
- Consumes: `TrayVisualStatePolicy.Resolve`, `TrayVisualStatePolicy.TooltipFor`, `TrayVisualState`
- Produces: `TrayIconService(string normalIconPath, string liveIconPath, string gta6IconPath, Action onTogglePopover, Action onRefresh, Action onOpenSettings, Action onQuit)` and `public void SetTrayState(TrayVisualState state)`. This replaces `SetLiveState(bool)`.

- [ ] **Step 1: Write `New-Gta6TrayIcon.ps1`.** It reads `tray-live.ico` bytes and writes `tray-gta6.ico`. The file has 2 entries (16x16, 32x32), both 32bpp uncompressed DIB (BGRA after a 40-byte BITMAPINFOHEADER at each entry's offset). For every pixel where `R > G + 30 && R > B + 30` (red family), set `s = R / 239.0` and write `B,G,R = round(clamp(0xF7*s)), round(clamp(0x55*s)), round(clamp(0xA8*s))`. Keep alpha, the AND mask, and white pixels untouched.

- [ ] **Step 2: Run the script and verify**

Run: `powershell -File VRemindWin\tools\New-Gta6TrayIcon.ps1`
Expected: `tray-gta6.ico` is 5430 bytes. Loading it with `System.Drawing.Icon(path, 32, 32).ToBitmap()` gives a most frequent opaque color of `168,85,247` and still about 29 white pixels.

- [ ] **Step 3: Update `TrayIconService`.** Load a third handle `_gta6Icon`, replace `_isLive` with `_state`, and pick the icon `Gta6Live → _gta6Icon (fallback _liveIcon if zero)`, `Live → _liveIcon`, `Normal → _normalIcon`. `szTip = TrayVisualStatePolicy.TooltipFor(_state)`. Destroy `_gta6Icon` in `Dispose`.

- [ ] **Step 4: Update `App.xaml.cs`.** Pass `Path.Combine(assetsDir, "tray-gta6.ico")`, and have `UpdateTrayLiveState` call `_trayIconService.SetTrayState(TrayVisualStatePolicy.Resolve(_coordinator.CurrentState))`.

- [ ] **Step 5: Build and run the full suite, expect both green**

Run: `dotnet build VRemindWin\src\VRemindWin\VRemindWin.csproj -p:Platform=x64; dotnet test VRemindWin\tests\VRemindWin.Tests\VRemindWin.Tests.csproj`
Expected: `Build succeeded`, `Failed: 0`. The build output contains `Assets\tray-gta6.ico`.

- [ ] **Step 6: Manual smoke check.** Watch any channel that is currently live with "GTA 6" in its title (or temporarily point a test at a live stream you control and rename it). Confirm all of these:
  - the purple row ring, badge, and time text
  - the purple `N LIVE` pill and `LIVE NOW` header
  - confetti, unless Reduce Motion is on
  - the purple tray icon with tooltip `VRemind (Live now - GTA 6)`
  - after the stream ends and the next refresh runs, everything reverts to red

- [ ] **Step 7: Commit**

```bash
git add VRemindWin/tools/New-Gta6TrayIcon.ps1 VRemindWin/src/VRemindWin/Assets/tray-gta6.ico VRemindWin/src/VRemindWin/VRemindWin.csproj VRemindWin/src/VRemindWin/Services/TrayIconService.cs VRemindWin/src/VRemindWin/App.xaml.cs
git commit -m "feat: purple GTA 6 tray icon and three-state tray"
```
