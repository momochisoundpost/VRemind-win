using System.ComponentModel;
using System.Runtime.CompilerServices;
using VRemindWin.Core.Models;

namespace VRemindWin.Core.Services;

public sealed class StreamMonitorCoordinator : INotifyPropertyChanged, IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(180);

    private readonly ICoreCliBridge _bridge;
    private readonly IToastNotificationDispatcher _toastDispatcher;
    private readonly TimeSpan _pollInterval;
    private readonly Action<Action>? _uiDispatcher;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _syncLock = new();

    private PeriodicTimer? _pollTimer;
    private CancellationTokenSource? _pollCts;
    private Task? _pollLoopTask;
    private Task? _inFlightRefreshTask;
    private bool _needsAnotherRefresh;
    private long _statusEpoch;
    private DateOnly? _dailyOrderDate;
    private List<string>? _dailyOrderedIds;
    private bool _disposed;

    private CliStateEnvelope _currentState = CliStateEnvelope.Empty;
    private bool _isRefreshing;
    private bool _isPausedForSleep;

    public StreamMonitorCoordinator(
        ICoreCliBridge bridge,
        IToastNotificationDispatcher toastDispatcher,
        TimeSpan? pollInterval = null,
        Action<Action>? uiDispatcher = null,
        Func<DateTimeOffset>? clock = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _toastDispatcher = toastDispatcher ?? throw new ArgumentNullException(nameof(toastDispatcher));
        _pollInterval = pollInterval ?? DefaultPollInterval;
        _uiDispatcher = uiDispatcher;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CliStateEnvelope CurrentState
    {
        get => _currentState;
        private set
        {
            if (!Equals(_currentState, value))
            {
                _currentState = value;
                RaisePropertyChanged();
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing != value)
            {
                _isRefreshing = value;
                RaisePropertyChanged();
            }
        }
    }

    public bool IsPausedForSleep
    {
        get => _isPausedForSleep;
        private set
        {
            if (_isPausedForSleep != value)
            {
                _isPausedForSleep = value;
                RaisePropertyChanged();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var response = await _bridge.GetStateAsync(ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            CurrentState = ProjectIncomingState(response.State);
        }
    }

    public void StartPolling(bool refreshImmediately = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_syncLock)
        {
            StopPollingCore();
            if (IsPausedForSleep)
            {
                return;
            }

            _pollCts = new CancellationTokenSource();
            var ct = _pollCts.Token;
            _pollTimer = new PeriodicTimer(_pollInterval);
            _pollLoopTask = RunPollingLoopAsync(_pollTimer, refreshImmediately, ct);
        }
    }

    public void StopPolling()
    {
        lock (_syncLock)
        {
            StopPollingCore();
        }
    }

    public Task RefreshAllAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_syncLock)
        {
            if (_inFlightRefreshTask is not null && !_inFlightRefreshTask.IsCompleted)
            {
                _needsAnotherRefresh = true;
                return _inFlightRefreshTask;
            }

            IsRefreshing = true;
            _needsAnotherRefresh = false;
            _inFlightRefreshTask = RunSingleFlightRefreshLoopAsync(ct);
            return _inFlightRefreshTask;
        }
    }

    public async Task<CliCommandResponse> AddChannelAsync(
        string platform,
        string input,
        bool ignoreSecret = false,
        CancellationToken ct = default)
    {
        var response = await _bridge.AddChannelAsync(platform, input, ignoreSecret, ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            Interlocked.Increment(ref _statusEpoch);
            CurrentState = ProjectIncomingState(response.State);
            DispatchNotificationsSafely(response.State.Notifications);
        }
        return response;
    }

    public async Task RemoveChannelAsync(string channelId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _statusEpoch);
        var response = await _bridge.RemoveChannelAsync(channelId, ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            CurrentState = ProjectIncomingState(response.State);
        }
    }

    public async Task ReorderSectionAsync(
        string sectionKind,
        int fromIndex,
        int toIndex,
        CancellationToken ct = default)
    {
        IReadOnlyList<CliChannelItem> reorderedSection;
        lock (_syncLock)
        {
            var section = GetSectionItems(CurrentState, sectionKind);
            if (fromIndex < 0 || fromIndex >= section.Count ||
                toIndex < 0 || toIndex >= section.Count ||
                fromIndex == toIndex)
            {
                return;
            }

            reorderedSection = ChannelOrderPolicy.ReorderSectionItems(section, fromIndex, toIndex);
            var nextState = ChannelOrderPolicy.ApplySectionOrder(CurrentState, sectionKind, reorderedSection);
            _dailyOrderDate = DateOnly.FromDateTime(_clock().LocalDateTime);
            _dailyOrderedIds = CollectOrderedIds(nextState);
            Interlocked.Increment(ref _statusEpoch);
            CurrentState = nextState;
        }

        await _bridge.ReorderChannelsAsync(
            reorderedSection.Select(c => c.Id).ToList(),
            ct).ConfigureAwait(false);
    }

    public async Task UpdateSettingsAsync(AppSettingsDto settings, CancellationToken ct = default)
    {
        var response = await _bridge.UpdateSettingsAsync(settings, ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            CurrentState = ProjectIncomingState(response.State);
        }
    }

    public async Task<CliCommandResponse> CompleteOnboardingAsync(
        string platform,
        string input,
        string updateConsent,
        CancellationToken ct = default)
    {
        var response = await _bridge.CompleteOnboardingAsync(platform, input, updateConsent, ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            Interlocked.Increment(ref _statusEpoch);
            CurrentState = ProjectIncomingState(response.State);
        }
        return response;
    }

    public async Task<CliCommandResponse> ResetFactoryDefaultsAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _statusEpoch);
        ClearDailyOrderProjection();
        var response = await _bridge.ResetFactoryDefaultsAsync(ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            CurrentState = ProjectIncomingState(response.State);
        }
        return response;
    }

    public async Task<CliCommandResponse> ExportBackupAsync(string filePath, CancellationToken ct = default)
    {
        var response = await _bridge.ExportBackupAsync(filePath, ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            CurrentState = ProjectIncomingState(response.State);
        }
        return response;
    }

    public Task<CliCommandResponse> ValidateBackupAsync(string filePath, CancellationToken ct = default)
        => _bridge.ValidateBackupAsync(filePath, ct);

    public async Task<CliCommandResponse> RestoreBackupAsync(string filePath, CancellationToken ct = default)
    {
        Interlocked.Increment(ref _statusEpoch);
        ClearDailyOrderProjection();
        var response = await _bridge.RestoreBackupAsync(filePath, ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            CurrentState = ProjectIncomingState(response.State);
        }
        return response;
    }

    public async Task<CliCommandResponse> CheckForUpdatesAsync(bool manual = false, CancellationToken ct = default)
    {
        var response = await _bridge.CheckForUpdatesAsync(manual, ct).ConfigureAwait(false);
        if (response.State is not null)
        {
            CurrentState = ProjectIncomingState(response.State);
        }
        return response;
    }

    public void SendCustomNotification(CliNotificationItem notification)
    {
        DispatchNotificationsSafely([notification]);
    }

    public void SendTestNotification()
    {
        SendCustomNotification(new CliNotificationItem(
            Kind: "test",
            ChannelName: "Caitlyn Momochi",
            Title: "🎉 VRemind Test Notification",
            Body: "Notifications are working! Click to open @bukancaitlyn.",
            TargetURL: "https://www.youtube.com/@bukancaitlyn",
            PlaySound: CurrentState.Settings.PlayNotificationSound));
    }

    public void HandlePowerOrSessionPause()
    {
        lock (_syncLock)
        {
            if (_isPausedForSleep)
            {
                return;
            }
            IsPausedForSleep = true;
            StopPollingCore();
        }
    }

    public async Task HandlePowerOrSessionResumeAsync()
    {
        lock (_syncLock)
        {
            if (!_isPausedForSleep)
            {
                return;
            }
            IsPausedForSleep = false;
        }

        await RefreshAllAsync().ConfigureAwait(false);
        StartPolling(refreshImmediately: false);
    }

    private async Task RunSingleFlightRefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                long passEpoch = Interlocked.Read(ref _statusEpoch);
                var response = await _bridge.RefreshAsync(ct).ConfigureAwait(false);

                if (response.State is not null)
                {
                    if (Interlocked.Read(ref _statusEpoch) == passEpoch)
                    {
                        CurrentState = ProjectIncomingState(response.State);
                        DispatchNotificationsSafely(response.State.Notifications);
                    }
                    else
                    {
                        var fresh = await _bridge.GetStateAsync(ct).ConfigureAwait(false);
                        if (fresh.State is not null)
                        {
                            CurrentState = ProjectIncomingState(fresh.State);
                            var activeUrls = CollectActiveWatchUrls(fresh.State);
                            var filteredNotifications = response.State.Notifications
                                .Where(n => IsNotificationStillRelevant(n, activeUrls))
                                .ToList();
                            DispatchNotificationsSafely(filteredNotifications);
                        }
                    }
                }

                lock (_syncLock)
                {
                    if (!_needsAnotherRefresh)
                    {
                        return;
                    }
                    _needsAnotherRefresh = false;
                }
            }
        }
        finally
        {
            lock (_syncLock)
            {
                IsRefreshing = false;
                _inFlightRefreshTask = null;
            }
        }
    }

    private void DispatchNotificationsSafely(IEnumerable<CliNotificationItem> notifications)
    {
        foreach (var notification in notifications)
        {
            try
            {
                _toastDispatcher.Dispatch(notification);
            }
            catch
            {
                // Notification delivery failures must never discard state updates or stop polling.
            }
        }
    }

    private static HashSet<string> CollectActiveWatchUrls(CliStateEnvelope state)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ch in state.Sections.Live) urls.Add(ch.WatchURL);
        foreach (var ch in state.Sections.Upcoming) urls.Add(ch.WatchURL);
        foreach (var ch in state.Sections.Offline) urls.Add(ch.WatchURL);
        return urls;
    }

    private static bool IsNotificationStillRelevant(CliNotificationItem item, HashSet<string> activeWatchUrls)
    {
        if (string.Equals(item.Kind, "developerBirthday", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (activeWatchUrls.Contains(item.TargetURL))
        {
            return true;
        }

        return false;
    }

    private async Task RunPollingLoopAsync(PeriodicTimer timer, bool refreshImmediately, CancellationToken ct)
    {
        try
        {
            if (refreshImmediately)
            {
                try
                {
                    await RefreshAllAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Keep polling alive even if initial refresh fails (e.g., offline / network down)
                }
            }

            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await RefreshAllAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // Keep polling alive on transient errors
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
    }

    private void StopPollingCore()
    {
        try
        {
            _pollCts?.Cancel();
        }
        catch
        {
            // Ignore
        }
        _pollCts?.Dispose();
        _pollCts = null;

        _pollTimer?.Dispose();
        _pollTimer = null;
        _pollLoopTask = null;
    }

    private CliStateEnvelope ProjectIncomingState(CliStateEnvelope state)
    {
        IReadOnlyList<string>? customIdsSnapshot = null;
        lock (_syncLock)
        {
            if (_dailyOrderDate is { } orderDate && _dailyOrderedIds is { Count: > 0 })
            {
                var today = DateOnly.FromDateTime(_clock().LocalDateTime);
                if (today == orderDate)
                {
                    customIdsSnapshot = _dailyOrderedIds.ToList();
                }
                else
                {
                    _dailyOrderDate = null;
                    _dailyOrderedIds = null;
                }
            }
        }

        return customIdsSnapshot is not null
            ? ChannelOrderPolicy.ApplyCustomOrder(state, customIdsSnapshot)
            : state;
    }

    private void ClearDailyOrderProjection()
    {
        lock (_syncLock)
        {
            _dailyOrderDate = null;
            _dailyOrderedIds = null;
        }
    }

    private static IReadOnlyList<CliChannelItem> GetSectionItems(CliStateEnvelope state, string sectionKind)
        => sectionKind.ToLowerInvariant() switch
        {
            "live" => state.Sections.Live,
            "upcoming" => state.Sections.Upcoming,
            "offline" => state.Sections.Offline,
            _ => []
        };

    private static List<string> CollectOrderedIds(CliStateEnvelope state)
    {
        var ids = new List<string>(
            state.Sections.Live.Count + state.Sections.Upcoming.Count + state.Sections.Offline.Count);
        foreach (var c in state.Sections.Live) ids.Add(c.Id);
        foreach (var c in state.Sections.Upcoming) ids.Add(c.Id);
        foreach (var c in state.Sections.Offline) ids.Add(c.Id);
        return ids;
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        var handler = PropertyChanged;
        if (handler is null)
        {
            return;
        }

        if (_uiDispatcher is not null)
        {
            _uiDispatcher(() => handler(this, new PropertyChangedEventArgs(propertyName)));
        }
        else
        {
            handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        StopPolling();
    }
}
