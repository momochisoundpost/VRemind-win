using System.Collections.Concurrent;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using VRemindWin.Core.ViewModels;
using Xunit;

namespace VRemindWin.Tests;

public sealed class CoordinatorAndViewModelTests
{
    private sealed class FakeToastDispatcher : IToastNotificationDispatcher
    {
        public List<CliNotificationItem> Dispatched { get; } = [];

        public void Dispatch(CliNotificationItem item)
        {
            Dispatched.Add(item);
        }
    }

    private sealed class StubCoreCliBridge : ICoreCliBridge
    {
        public Func<CancellationToken, Task<CliCommandResponse>>? OnGetState { get; set; }
        public Func<CancellationToken, Task<CliCommandResponse>>? OnRefresh { get; set; }
        public Func<string, string, bool, CancellationToken, Task<CliCommandResponse>>? OnAddChannel { get; set; }
        public Func<string, CancellationToken, Task<CliCommandResponse>>? OnRemoveChannel { get; set; }
        public Func<AppSettingsDto, CancellationToken, Task<CliCommandResponse>>? OnUpdateSettings { get; set; }
        public Func<string, string, string, CancellationToken, Task<CliCommandResponse>>? OnCompleteOnboarding { get; set; }
        public Func<CancellationToken, Task<CliCommandResponse>>? OnResetFactoryDefaults { get; set; }
        public Func<string, CancellationToken, Task<CliCommandResponse>>? OnExportBackup { get; set; }
        public Func<string, CancellationToken, Task<CliCommandResponse>>? OnValidateBackup { get; set; }
        public Func<string, CancellationToken, Task<CliCommandResponse>>? OnRestoreBackup { get; set; }
        public Func<bool, CancellationToken, Task<CliCommandResponse>>? OnCheckForUpdates { get; set; }

        public int RefreshCallCount { get; private set; }
        public int GetStateCallCount { get; private set; }
        private CliCommandResponse? _lastMutatedResponse;

        public Task<CliCommandResponse> GetStateAsync(CancellationToken ct = default)
        {
            GetStateCallCount++;
            return OnGetState is not null
                ? OnGetState(ct)
                : Task.FromResult(_lastMutatedResponse ?? MakeOkResponse());
        }

        public Task<CliCommandResponse> RefreshAsync(CancellationToken ct = default)
        {
            RefreshCallCount++;
            return OnRefresh is not null
                ? OnRefresh(ct)
                : Task.FromResult(_lastMutatedResponse ?? MakeOkResponse());
        }

        public async Task<CliCommandResponse> AddChannelAsync(string platform, string input, bool ignoreSecret = false, CancellationToken ct = default)
        {
            var resp = OnAddChannel is not null
                ? await OnAddChannel(platform, input, ignoreSecret, ct)
                : MakeOkResponse();
            if (resp.State is not null) _lastMutatedResponse = resp;
            return resp;
        }

        public async Task<CliCommandResponse> RemoveChannelAsync(string channelId, CancellationToken ct = default)
        {
            var resp = OnRemoveChannel is not null
                ? await OnRemoveChannel(channelId, ct)
                : MakeOkResponse();
            if (resp.State is not null) _lastMutatedResponse = resp;
            return resp;
        }

        public async Task<CliCommandResponse> UpdateSettingsAsync(AppSettingsDto settings, CancellationToken ct = default)
        {
            var resp = OnUpdateSettings is not null
                ? await OnUpdateSettings(settings, ct)
                : MakeOkResponse(settings: settings);
            if (resp.State is not null) _lastMutatedResponse = resp;
            return resp;
        }

        public async Task<CliCommandResponse> CompleteOnboardingAsync(string platform, string input, string updateConsent, CancellationToken ct = default)
        {
            var resp = OnCompleteOnboarding is not null
                ? await OnCompleteOnboarding(platform, input, updateConsent, ct)
                : MakeOkResponse();
            if (resp.State is not null) _lastMutatedResponse = resp;
            return resp;
        }

        public async Task<CliCommandResponse> ResetFactoryDefaultsAsync(CancellationToken ct = default)
        {
            var resp = OnResetFactoryDefaults is not null
                ? await OnResetFactoryDefaults(ct)
                : MakeOkResponse();
            if (resp.State is not null) _lastMutatedResponse = resp;
            return resp;
        }

        public Task<CliCommandResponse> ExportBackupAsync(string filePath, CancellationToken ct = default)
        {
            return OnExportBackup is not null
                ? OnExportBackup(filePath, ct)
                : Task.FromResult(_lastMutatedResponse ?? MakeOkResponse());
        }

        public Task<CliCommandResponse> ValidateBackupAsync(string filePath, CancellationToken ct = default)
        {
            return OnValidateBackup is not null
                ? OnValidateBackup(filePath, ct)
                : Task.FromResult(_lastMutatedResponse ?? MakeOkResponse());
        }

        public async Task<CliCommandResponse> RestoreBackupAsync(string filePath, CancellationToken ct = default)
        {
            var resp = OnRestoreBackup is not null
                ? await OnRestoreBackup(filePath, ct)
                : MakeOkResponse();
            if (resp.State is not null) _lastMutatedResponse = resp;
            return resp;
        }

        public Task<CliCommandResponse> CheckForUpdatesAsync(bool manual = false, CancellationToken ct = default)
        {
            return OnCheckForUpdates is not null
                ? OnCheckForUpdates(manual, ct)
                : Task.FromResult(MakeOkResponse());
        }
    }

    private static CliCommandResponse MakeOkResponse(
        IReadOnlyList<CliChannelItem>? live = null,
        IReadOnlyList<CliChannelItem>? upcoming = null,
        IReadOnlyList<CliChannelItem>? offline = null,
        IReadOnlyList<CliNotificationItem>? notifications = null,
        AppSettingsDto? settings = null)
    {
        var liveList = live ?? [];
        var upcomingList = upcoming ?? [];
        var offlineList = offline ?? [];
        return new CliCommandResponse(
            Outcome: "ok",
            ErrorMessage: null,
            SecretDialog: null,
            State: new CliStateEnvelope(
                LiveCount: liveList.Count,
                Sections: new CliSections(liveList, upcomingList, offlineList),
                Settings: settings ?? new AppSettingsDto(false, true, false, true, false, "undecided", null, null, false, null),
                Notifications: notifications ?? []
            )
        );
    }

    private static CliChannelItem MakeChannel(string id, string name, string statusKind = "offline")
    {
        return new CliChannelItem(
            Id: id,
            Platform: "youtube",
            PlatformDisplayName: "YouTube",
            Name: name,
            Handle: "@bukancaitlyn",
            FormattedHandle: "@bukancaitlyn",
            AvatarURL: null,
            StatusKind: statusKind,
            StreamTitle: statusKind == "live" ? "Live Stream" : null,
            TimeLabel: statusKind == "live" ? "Live for 5m" : "Offline",
            WatchURL: "https://www.youtube.com/@bukancaitlyn"
        );
    }

    [Fact]
    public async Task RefreshAllAsync_CoalescesConcurrentCallsIntoSingleFollowUpPass()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callNum = 0;
        var bridge = new StubCoreCliBridge
        {
            OnRefresh = async _ =>
            {
                var current = Interlocked.Increment(ref callNum);
                if (current == 1)
                {
                    await gate.Task;
                    return MakeOkResponse(notifications: [
                        new CliNotificationItem("live", "Caitlyn Momochi", "Caitlyn Momochi", "🔴 LIVE: Test", "https://www.youtube.com/watch?v=123", true)
                    ]);
                }
                return MakeOkResponse();
            }
        };
        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));

        var first = coordinator.RefreshAllAsync();
        var second = coordinator.RefreshAllAsync();
        var third = coordinator.RefreshAllAsync();

        Assert.True(coordinator.IsRefreshing);
        gate.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.False(coordinator.IsRefreshing);
        Assert.Equal(2, bridge.RefreshCallCount);
        Assert.Single(toasts.Dispatched);
        Assert.Equal("https://www.youtube.com/watch?v=123", toasts.Dispatched[0].TargetURL);
    }

    [Fact]
    public async Task RemoveChannelAsync_DuringInFlightRefresh_IncrementsEpochAndReloadsCleanState()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefreshFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleChannel = MakeChannel("youtube:@bukancaitlyn", "Caitlyn Momochi");

        var bridge = new StubCoreCliBridge
        {
            OnRefresh = async _ =>
            {
                refreshStarted.SetResult();
                await allowRefreshFinish.Task;
                return MakeOkResponse(offline: [staleChannel]);
            },
            OnRemoveChannel = (_, _) => Task.FromResult(MakeOkResponse(offline: [])),
            OnGetState = _ => Task.FromResult(MakeOkResponse(offline: []))
        };

        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));

        var refreshTask = coordinator.RefreshAllAsync();
        await refreshStarted.Task;

        await coordinator.RemoveChannelAsync("youtube:@bukancaitlyn");
        allowRefreshFinish.SetResult();
        await refreshTask;

        Assert.Empty(coordinator.CurrentState.Sections.Live);
        Assert.Empty(coordinator.CurrentState.Sections.Upcoming);
        Assert.Empty(coordinator.CurrentState.Sections.Offline);
        Assert.True(bridge.GetStateCallCount >= 1);
    }

    [Fact]
    public async Task PopoverViewModel_AddChannel_HandlesValidationSecretPromptAndSuccess()
    {
        var bridge = new StubCoreCliBridge();
        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));
        var vm = new PopoverViewModel(coordinator);

        // 1. Empty input does nothing
        vm.NewHandleInput = "   ";
        await vm.AddChannelCommandAsync();
        Assert.Null(vm.ValidationError);

        // 2. Validation error keeps input and sets ValidationError
        bridge.OnAddChannel = (_, _, _, _) => Task.FromResult(new CliCommandResponse(
            "validation_error",
            "Please enter a valid channel handle or YouTube URL",
            null,
            null
        ));
        vm.NewHandleInput = "bad handle";
        await vm.AddChannelCommandAsync();
        Assert.Equal("Please enter a valid channel handle or YouTube URL", vm.ValidationError);
        Assert.Equal("bad handle", vm.NewHandleInput);

        // 3. Secret prompt abort stops without calling ignoreSecret
        var secretCalls = new List<bool>();
        bridge.OnAddChannel = (_, _, ignoreSecret, _) =>
        {
            secretCalls.Add(ignoreSecret);
            if (!ignoreSecret)
            {
                return Task.FromResult(new CliCommandResponse(
                    "secret_prompt",
                    null,
                    new SecretDialogPayload("Title", "Message", "Abort", "Cancel", "Ignore"),
                    null
                ));
            }
            return Task.FromResult(new CliCommandResponse("added", null, null, MakeOkResponse().State));
        };

        vm.SecretPromptPresenter = _ => Task.FromResult("abort");
        vm.NewHandleInput = "@secret";
        await vm.AddChannelCommandAsync();
        Assert.Equal([false], secretCalls);

        // 4. Secret prompt ignore retries with ignoreSecret: true, clears input, and refreshes
        secretCalls.Clear();
        vm.SecretPromptPresenter = _ => Task.FromResult("ignore");
        vm.NewHandleInput = "@secret";
        await vm.AddChannelCommandAsync();
        Assert.Equal([false, true], secretCalls);
        Assert.Equal(string.Empty, vm.NewHandleInput);
        Assert.Null(vm.ValidationError);
        Assert.Equal(1, bridge.RefreshCallCount);
    }

    [Fact]
    public async Task PowerPauseAndResume_StopsPollingWhileAsleepAndRefreshesOnWake()
    {
        var bridge = new StubCoreCliBridge();
        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));

        coordinator.HandlePowerOrSessionPause();
        Assert.True(coordinator.IsPausedForSleep);

        await coordinator.HandlePowerOrSessionResumeAsync();
        Assert.False(coordinator.IsPausedForSleep);
        Assert.Equal(1, bridge.RefreshCallCount);
    }

    [Fact]
    public async Task PollingLoop_ContinuesAfterTransientBridgeAndToastFailures_AndStillUpdatesState()
    {
        var liveChannel = MakeChannel("youtube:@bukancaitlyn", "Caitlyn Momochi", "live");
        var attempt = 0;
        var secondPassDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdPassDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var bridge = new StubCoreCliBridge
        {
            OnRefresh = _ =>
            {
                var n = Interlocked.Increment(ref attempt);
                if (n == 1)
                {
                    throw new InvalidOperationException("Transient CLI error");
                }

                if (n == 2)
                {
                    secondPassDone.TrySetResult();
                    return Task.FromResult(MakeOkResponse(
                        live: [liveChannel],
                        notifications: [
                            new CliNotificationItem("live", "Caitlyn Momochi", "Caitlyn Momochi", "🔴 LIVE: Test", "https://www.youtube.com/watch?v=123", true)
                        ]));
                }

                thirdPassDone.TrySetResult();
                return Task.FromResult(MakeOkResponse(live: [liveChannel]));
            }
        };

        var throwingToast = new ThrowingToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, throwingToast, TimeSpan.FromMilliseconds(25));
        coordinator.StartPolling(refreshImmediately: true);

        await secondPassDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await thirdPassDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.StopPolling();

        Assert.True(bridge.RefreshCallCount >= 3);
        Assert.Equal(1, coordinator.CurrentState.LiveCount);
    }

    [Fact]
    public async Task RemoveChannelAsync_DuringInFlightRefresh_SuppressesStaleNotificationsForRemovedChannel()
    {
        var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRefreshFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleChannel = MakeChannel("youtube:@tobedeleted", "Removed Channel", "live");

        var bridge = new StubCoreCliBridge
        {
            OnRefresh = async _ =>
            {
                refreshStarted.SetResult();
                await allowRefreshFinish.Task;
                return MakeOkResponse(
                    live: [staleChannel],
                    notifications: [
                        new CliNotificationItem("live", "Removed Channel", "Removed Channel", "🔴 LIVE: Deleted", "https://www.youtube.com/@bukancaitlyn", true)
                    ]);
            },
            OnRemoveChannel = (_, _) => Task.FromResult(MakeOkResponse(live: [], offline: [])),
            OnGetState = _ => Task.FromResult(MakeOkResponse(live: [], offline: []))
        };

        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));

        var refreshTask = coordinator.RefreshAllAsync();
        await refreshStarted.Task;

        await coordinator.RemoveChannelAsync("youtube:@tobedeleted");
        allowRefreshFinish.SetResult();
        await refreshTask;

        Assert.Empty(toasts.Dispatched);
        Assert.Equal(0, coordinator.CurrentState.LiveCount);
    }

    [Fact]
    public async Task SettingsViewModel_RapidConcurrentTogglesPreserveBothSettings()
    {
        var firstCallEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var savedSnapshots = new ConcurrentQueue<AppSettingsDto>();
        AppSettingsDto latestState = AppSettingsDto.Default;

        var bridge = new StubCoreCliBridge
        {
            OnGetState = _ => Task.FromResult(MakeOkResponse(settings: latestState)),
            OnUpdateSettings = async (settings, _) =>
            {
                var idx = Interlocked.Increment(ref callCount);
                if (idx == 1)
                {
                    firstCallEntered.SetResult();
                    await releaseFirstCall.Task;
                }

                latestState = settings;
                savedSnapshots.Enqueue(settings);
                return MakeOkResponse(settings: latestState);
            }
        };

        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));
        await coordinator.InitializeAsync();
        var vm = new SettingsViewModel(coordinator, new StartupRegistryService(), @"C:\Temp\VRemindWin.exe");

        // Rapidly toggle NotifyOnLive -> false and NotifyOnUpcoming -> true
        vm.NotifyOnLive = false;
        await firstCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        vm.NotifyOnUpcoming = true;

        releaseFirstCall.SetResult();
        await vm.FlushPendingUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(coordinator.CurrentState.Settings.NotifyOnLive);
        Assert.True(coordinator.CurrentState.Settings.NotifyOnUpcoming);
        Assert.False(vm.NotifyOnLive);
        Assert.True(vm.NotifyOnUpcoming);
    }

    private sealed class ThrowingToastDispatcher : IToastNotificationDispatcher
    {
        public void Dispatch(CliNotificationItem item) => throw new InvalidOperationException("Toast disabled by policy");
    }

    [Fact]
    public async Task AddChannelAsync_DispatchesNotifications_WhenDevBirthdayReturned()
    {
        var bdayToast = new CliNotificationItem(
            "developerBirthday",
            null,
            "🎉 Happy Birthday to the Developer!",
            "Today is Caitlyn Momochi's birthday! Click to send a birthday wish.",
            "mailto:y2karyl@disroot.org?subject=Happy%20Birthday%20Caitlyn!",
            true);

        var bridge = new StubCoreCliBridge
        {
            OnAddChannel = (_, _, _, _) => Task.FromResult(MakeOkResponse(notifications: [bdayToast]))
        };
        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));

        await coordinator.AddChannelAsync("youtube", "@bukancaitlyn");

        Assert.Single(toasts.Dispatched);
        Assert.Equal("developerBirthday", toasts.Dispatched[0].Kind);
    }

    [Fact]
    public async Task PopoverViewModel_ExposesCelebrationAndReduceMotionFlags()
    {
        var celebratingLive = new CliChannelItem(
            Id: "youtube:@bukancaitlyn",
            Platform: "youtube",
            PlatformDisplayName: "YouTube",
            Name: "Caitlyn Momochi",
            Handle: "@bukancaitlyn",
            FormattedHandle: "@bukancaitlyn",
            AvatarURL: null,
            StatusKind: "live",
            StreamTitle: "Birthday Stream!",
            TimeLabel: "Live for 10m",
            WatchURL: "https://www.youtube.com/watch?v=123",
            IsCelebration: true,
            CelebrationType: "birthday");

        var settings = AppSettingsDto.Default with { ReduceMotion = true };
        var state = new CliStateEnvelope(
            LiveCount: 1,
            Sections: new CliSections([celebratingLive], [], []),
            Settings: settings,
            Notifications: [],
            IsPopoverCelebrating: true,
            AppVersion: "1.3.1");

        var bridge = new StubCoreCliBridge
        {
            OnGetState = _ => Task.FromResult(new CliCommandResponse("ok", null, null, state))
        };
        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));
        var vm = new PopoverViewModel(coordinator);

        await coordinator.InitializeAsync();

        Assert.True(vm.IsPopoverCelebrating);
        Assert.True(vm.ReduceMotion);
        Assert.True(vm.IsLiveSectionCelebrating);
        Assert.False(vm.IsUpcomingSectionCelebrating);
    }

    [Fact]
    public async Task SettingsViewModel_CheckForUpdatesManually_WhenPrivateRepo404_SendsTestToastAndOpensPreview()
    {
        var bridge = new StubCoreCliBridge
        {
            OnCheckForUpdates = (manual, _) =>
            {
                Assert.True(manual);
                return Task.FromResult(new CliCommandResponse(
                    Outcome: "ok",
                    ErrorMessage: null,
                    SecretDialog: null,
                    State: MakeOkResponse().State,
                    UpdateResult: new CliUpdateCheckResult(
                        Status: "failed",
                        LatestVersion: null,
                        RemoteVersion: null,
                        ErrorMessage: "GitHub API request failed with status 404",
                        HttpStatusCode: 404,
                        Release: null)));
            }
        };

        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));
        var vm = new SettingsViewModel(coordinator, new StartupRegistryService(), @"C:\Temp\VRemindWin.exe");

        CliReleaseDto? shownRelease = null;
        vm.OnShowUpdateWindow = release => shownRelease = release;

        await vm.CheckForUpdatesManuallyAsync();

        Assert.Single(toasts.Dispatched);
        Assert.Contains("private", vm.UpdateStatusText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(shownRelease);
        Assert.Equal("v1.3.1", shownRelease!.TagName);
        Assert.Contains("<video", shownRelease.Body ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsViewModel_CheckForUpdatesManually_WhenLocalNewer_TriggersEasterEggAlert()
    {
        var bridge = new StubCoreCliBridge
        {
            OnCheckForUpdates = (_, _) => Task.FromResult(new CliCommandResponse(
                Outcome: "ok",
                ErrorMessage: null,
                SecretDialog: null,
                State: MakeOkResponse().State,
                UpdateResult: new CliUpdateCheckResult(
                    Status: "localVersionNewer",
                    LatestVersion: null,
                    RemoteVersion: "v1.0.0",
                    ErrorMessage: null,
                    HttpStatusCode: 200,
                    Release: null)))
        };

        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));
        var vm = new SettingsViewModel(coordinator, new StartupRegistryService(), @"C:\Temp\VRemindWin.exe");

        var easterEggShown = false;
        vm.OnShowEasterEggAlertAsync = () =>
        {
            easterEggShown = true;
            return Task.CompletedTask;
        };

        await vm.CheckForUpdatesManuallyAsync();

        Assert.True(easterEggShown);
        Assert.Contains("newer than GitHub", vm.UpdateStatusText ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SettingsViewModel_FactoryResetAndBackupRestore_UpdateStateAndTriggerRefresh()
    {
        var bridge = new StubCoreCliBridge
        {
            OnResetFactoryDefaults = _ => Task.FromResult(new CliCommandResponse(
                "reset",
                null,
                null,
                MakeOkResponse(
                    offline: [MakeChannel("youtube:@bukancaitlyn", "Caitlyn Momochi")],
                    settings: AppSettingsDto.Default with { HasCompletedOnboarding = true }).State)),
            OnRestoreBackup = (_, _) => Task.FromResult(new CliCommandResponse(
                "restored",
                null,
                null,
                MakeOkResponse(
                    offline: [
                        MakeChannel("youtube:@bukancaitlyn", "Caitlyn Momochi"),
                        MakeChannel("twitch:ironmouse", "ironmouse")
                    ],
                    settings: AppSettingsDto.Default with { HasCompletedOnboarding = true, ReduceMotion = true }).State,
                BackupChannelCount: 2))
        };

        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));
        var vm = new SettingsViewModel(coordinator, new StartupRegistryService(), @"C:\Temp\VRemindWin.exe");

        await vm.PerformFactoryResetAsync();
        Assert.Single(coordinator.CurrentState.Sections.Offline);
        Assert.True( bridge.RefreshCallCount >= 1);

        var prevRefreshes = bridge.RefreshCallCount;
        var restored = await vm.RestoreBackupFromFileAsync(@"C:\Temp\backup.json");
        Assert.True(restored);
        Assert.Equal(2, coordinator.CurrentState.Sections.Offline.Count);
        Assert.True(vm.ReduceMotion);
        Assert.True(bridge.RefreshCallCount > prevRefreshes);
    }

    [Fact]
    public async Task OnboardingViewModel_CompletesOnboardingAndHandlesMutualExclusion()
    {
        var bridge = new StubCoreCliBridge
        {
            OnCompleteOnboarding = (platform, input, consent, _) =>
            {
                if (input == "bad handle")
                {
                    return Task.FromResult(new CliCommandResponse(
                        "validation_error",
                        "Please enter a valid YouTube handle (e.g. @channel)",
                        null,
                        null));
                }

                return Task.FromResult(new CliCommandResponse(
                    "completed",
                    null,
                    null,
                    MakeOkResponse(
                        settings: AppSettingsDto.Default with
                        {
                            HasCompletedOnboarding = true,
                            UpdateConsent = consent
                        }).State));
            }
        };

        var toasts = new FakeToastDispatcher();
        using var coordinator = new StreamMonitorCoordinator(bridge, toasts, TimeSpan.FromMinutes(3));
        var vm = new OnboardingViewModel(coordinator);

        // Mutual exclusion of update checkboxes
        Assert.True(vm.EnableUpdateChecks);
        Assert.False(vm.NeverAskUpdatesAgain);

        vm.NeverAskUpdatesAgain = true;
        Assert.False(vm.EnableUpdateChecks);

        vm.EnableUpdateChecks = true;
        Assert.False(vm.NeverAskUpdatesAgain);

        // Test notification
        vm.SendTestNotification();
        Assert.True(vm.HasSentTestNotification);
        Assert.Single(toasts.Dispatched);

        // Invalid handle fails onboarding
        vm.CustomHandle = "bad handle";
        var failed = await vm.CompleteOnboardingAsync();
        Assert.False(failed);
        Assert.True(vm.HasValidationError);

        // Valid handle completes onboarding and triggers refresh
        vm.CustomHandle = "@bukancaitlyn";
        var ok = await vm.CompleteOnboardingAsync();
        Assert.True(ok);
        Assert.False(vm.HasValidationError);
        Assert.True(coordinator.CurrentState.Settings.HasCompletedOnboarding);
        Assert.True(bridge.RefreshCallCount >= 1);
    }
}
