using System.ComponentModel;
using System.Runtime.CompilerServices;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;

namespace VRemindWin.Core.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public const string AttributionCopy = "VRemind by Caitlyn Momochi, initially vibecoded for their Hackintosh video";
    public const string ForkCopy = "You don't like what I did? Go fork yourself, it's free and open source.";
    public const string PoliceSirenUrl = "https://www.youtube.com/watch?v=HKieGUH9pzg";
    public const string YoutubeChannelUrl = "https://www.youtube.com/@bukancaitlyn";
    public const string TwitterUrl = "https://twitter.com/Y2Karyl";
    public const string GithubUrl = "https://github.com/momochisoundpost";
    public const string InstagramUrl = "https://www.instagram.com/bukancaitlyn";

    private readonly StreamMonitorCoordinator _coordinator;
    private readonly StartupRegistryService _startupRegistry;
    private readonly string _appExePath;
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly object _stateSync = new();

    private AppSettingsDto _desiredSettings;
    private long _desiredVersion;
    private long _flushedVersion;
    private Task _flushTask = Task.CompletedTask;

    private bool _isCheckingUpdates;
    private string? _updateStatusText;
    private string? _backupStatusText;
    private string? _backupErrorMessage;

    public SettingsViewModel(
        StreamMonitorCoordinator coordinator,
        StartupRegistryService startupRegistry,
        string appExePath)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _startupRegistry = startupRegistry ?? throw new ArgumentNullException(nameof(startupRegistry));
        _appExePath = appExePath ?? throw new ArgumentNullException(nameof(appExePath));
        _desiredSettings = _coordinator.CurrentState.Settings;

        _coordinator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StreamMonitorCoordinator.CurrentState))
            {
                bool shouldSyncFromCoordinator;
                lock (_stateSync)
                {
                    shouldSyncFromCoordinator = _flushedVersion == _desiredVersion;
                    if (shouldSyncFromCoordinator)
                    {
                        _desiredSettings = _coordinator.CurrentState.Settings;
                    }
                }

                if (shouldSyncFromCoordinator)
                {
                    RaiseAllChanged();
                }
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Action<CliReleaseDto>? OnShowUpdateWindow { get; set; }

    public Func<Task>? OnShowEasterEggAlertAsync { get; set; }

    private AppSettingsDto Current
    {
        get
        {
            lock (_stateSync)
            {
                return _desiredSettings;
            }
        }
    }

    public string CurrentVersion => string.IsNullOrWhiteSpace(_coordinator.CurrentState.AppVersion)
        ? "1.3.6"
        : _coordinator.CurrentState.AppVersion;

    public string CurrentVersionText => $"Current Version: {CurrentVersion}";

    public int WatchedChannelCount
        => _coordinator.CurrentState.Sections.Live.Count +
           _coordinator.CurrentState.Sections.Upcoming.Count +
           _coordinator.CurrentState.Sections.Offline.Count;

    public bool NotifyOnLive
    {
        get => Current.NotifyOnLive;
        set => ApplySettingChange(
            current => current.NotifyOnLive != value,
            current => current with { NotifyOnLive = value });
    }

    public bool NotifyOnUpcoming
    {
        get => Current.NotifyOnUpcoming;
        set => ApplySettingChange(
            current => current.NotifyOnUpcoming != value,
            current => current with { NotifyOnUpcoming = value });
    }

    public bool PlayNotificationSound
    {
        get => Current.PlayNotificationSound;
        set => ApplySettingChange(
            current => current.PlayNotificationSound != value,
            current => current with { PlayNotificationSound = value });
    }

    public bool ReduceMotion
    {
        get => Current.ReduceMotion;
        set => ApplySettingChange(
            current => current.ReduceMotion != value,
            current => current with { ReduceMotion = value });
    }

    public bool IsAutoUpdateEnabled
    {
        get => string.Equals(Current.UpdateConsent, "allowed", StringComparison.OrdinalIgnoreCase);
        set
        {
            var nextConsent = value ? "allowed" : "neverAsk";
            ApplySettingChange(
                current => !string.Equals(current.UpdateConsent, nextConsent, StringComparison.OrdinalIgnoreCase),
                current => current with { UpdateConsent = nextConsent });
        }
    }

    public bool LaunchAtLogin
    {
        get => Current.LaunchAtLogin;
        set
        {
            bool changed;
            lock (_stateSync)
            {
                changed = _desiredSettings.LaunchAtLogin != value;
            }
            if (changed)
            {
                _startupRegistry.SetLaunchAtLogin(value, _appExePath);
                ApplySettingChange(
                    current => current.LaunchAtLogin != value,
                    current => current with { LaunchAtLogin = value });
            }
        }
    }

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set
        {
            if (_isCheckingUpdates != value)
            {
                _isCheckingUpdates = value;
                RaisePropertyChanged();
            }
        }
    }

    public string? UpdateStatusText
    {
        get => _updateStatusText;
        private set
        {
            if (_updateStatusText != value)
            {
                _updateStatusText = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasUpdateStatusText));
            }
        }
    }

    public bool HasUpdateStatusText => !string.IsNullOrWhiteSpace(_updateStatusText);

    public string? BackupStatusText
    {
        get => _backupStatusText;
        private set
        {
            if (_backupStatusText != value)
            {
                _backupStatusText = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasBackupStatusText));
            }
        }
    }

    public bool HasBackupStatusText => !string.IsNullOrWhiteSpace(_backupStatusText);

    public string? BackupErrorMessage
    {
        get => _backupErrorMessage;
        set
        {
            if (_backupErrorMessage != value)
            {
                _backupErrorMessage = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasBackupError));
            }
        }
    }

    public bool HasBackupError => !string.IsNullOrWhiteSpace(_backupErrorMessage);

    public static string GetDefaultBackupFileName(DateTimeOffset? now = null)
    {
        var date = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        return $"vremind-backup-{date:yyyy-MM-dd}.json";
    }

    public async Task CheckForUpdatesManuallyAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        UpdateStatusText = "Checking for updates...";
        try
        {
            var response = await _coordinator.CheckForUpdatesAsync(manual: true);
            var result = response.UpdateResult;
            if (result is null)
            {
                UpdateStatusText = "Update check failed.";
                return;
            }

            switch (result.Status)
            {
                case "updateAvailable" when result.Release is not null:
                    UpdateStatusText = $"Version {result.Release.TagName} is available.";
                    OnShowUpdateWindow?.Invoke(result.Release);
                    break;

                case "upToDate":
                    UpdateStatusText = $"VRemind is up to date ({result.LatestVersion ?? CurrentVersion}).";
                    break;

                case "localVersionNewer":
                    UpdateStatusText = $"Local version is newer than GitHub ({result.RemoteVersion ?? "unknown"}).";
                    if (OnShowEasterEggAlertAsync is not null)
                    {
                        await OnShowEasterEggAlertAsync();
                    }
                    break;

                case "skippedIgnored":
                    UpdateStatusText = $"Version {result.LatestVersion} is available (ignored).";
                    break;

                case "skippedNotConsented":
                    UpdateStatusText = "Automatic updates are disabled.";
                    break;

                case "failed":
                default:
                    if (result.HttpStatusCode == 404)
                    {
                        _coordinator.SendCustomNotification(new CliNotificationItem(
                            Kind: "updateTest",
                            ChannelName: "VRemind",
                            Title: "🔔 VRemind Update & Toast Check",
                            Body: "Repository is currently private — notifications and updater UI are ready!",
                            TargetURL: YoutubeChannelUrl,
                            PlaySound: Current.PlayNotificationSound));
                        UpdateStatusText = "Repo is currently private (test notification sent).";
                        ShowPreviewUpdateWindow();
                    }
                    else
                    {
                        UpdateStatusText = $"Update check failed: {result.ErrorMessage ?? "Unknown error"}";
                    }
                    break;
            }
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    public void ShowPreviewUpdateWindow()
    {
        OnShowUpdateWindow?.Invoke(BuildPreviewRelease());
    }

    public static CliReleaseDto BuildPreviewRelease()
    {
        const string markdown = """
            ### Release Notes
            **It's either a global holiday or another plague according to HR. Either way, I'm going on holiday.**

            After 13 years of waiting, GTA 6 early gameplay streams are finally here — which legally qualifies as a global holiday on my calendar, even if HR keeps calling it "another plague." Since I'm mentally already out of office with streams open everywhere, `v1.3.6` turns VRemind Miami Vice purple so the tray celebrates while I pretend I'm unreachable.

            #### ✨ What's New
            - [x] **Miami Vice GTA 6 Live Takeover:** Live streams matching `GTA 6`, `GTA6`, `GTA VI`, `GTAVI`, or `Grand Theft Auto 6/VI` light up in `#8B5CF6` → `#D946EF` across row rings, badges, headers, and confetti (respecting Reduce Motion).
            - [x] **Purple System Tray & Tooltip:** Switches to a `#A855F7` tray icon with `VRemind (Live now - GTA 6)` whenever a GTA 6 stream is live, then reverts cleanly when the stream ends.

            <video src="https://github.com/user-attachments/assets/vremind-win-parity-demo.mp4"></video>
            """;

        return new CliReleaseDto(
            TagName: "v1.3.6",
            Name: "It's either a global holiday or another plague according to HR.",
            Body: markdown,
            HtmlURL: "https://github.com/momochisoundpost/VRemind-win",
            DownloadURL: "https://github.com/momochisoundpost/VRemind-win/releases");
    }

    public async Task SkipUpdateVersionAsync(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return;
        }

        var updated = Current with { IgnoredUpdateTag = tagName.Trim() };
        await SaveUpdatedSettingsAsync(updated);
    }

    public async Task<bool> ExportBackupToFileAsync(string filePath)
    {
        BackupErrorMessage = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var response = await _coordinator.ExportBackupAsync(filePath);
        if (string.Equals(response.Outcome, "exported", StringComparison.OrdinalIgnoreCase))
        {
            BackupStatusText = $"Backup exported to {Path.GetFileName(filePath)}.";
            return true;
        }

        BackupErrorMessage = response.ErrorMessage ?? "Failed to export backup.";
        return false;
    }

    public async Task<(bool IsValid, int ChannelCount)> ValidateBackupFileAsync(string filePath)
    {
        BackupErrorMessage = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return (false, 0);
        }

        var response = await _coordinator.ValidateBackupAsync(filePath);
        if (string.Equals(response.Outcome, "valid_backup", StringComparison.OrdinalIgnoreCase))
        {
            return (true, response.BackupChannelCount ?? 0);
        }

        BackupErrorMessage = response.ErrorMessage ?? "The selected file is not a valid VRemind backup.";
        return (false, 0);
    }

    public async Task<bool> RestoreBackupFromFileAsync(string filePath)
    {
        BackupErrorMessage = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var response = await _coordinator.RestoreBackupAsync(filePath);
        if (!string.Equals(response.Outcome, "restored", StringComparison.OrdinalIgnoreCase) || response.State is null)
        {
            BackupErrorMessage = response.ErrorMessage ?? "Failed to restore backup.";
            return false;
        }

        lock (_stateSync)
        {
            _desiredSettings = response.State.Settings;
            _flushedVersion = _desiredVersion;
        }

        _startupRegistry.SetLaunchAtLogin(response.State.Settings.LaunchAtLogin, _appExePath);
        BackupStatusText = $"Restored {response.BackupChannelCount ?? WatchedChannelCount} channel(s) from {Path.GetFileName(filePath)}.";
        RaiseAllChanged();
        _ = _coordinator.RefreshAllAsync();
        return true;
    }

    public async Task PerformFactoryResetAsync()
    {
        var response = await _coordinator.ResetFactoryDefaultsAsync();
        if (response.State is not null)
        {
            lock (_stateSync)
            {
                _desiredSettings = response.State.Settings;
                _flushedVersion = _desiredVersion;
            }
        }

        _startupRegistry.SetLaunchAtLogin(false, _appExePath);
        BackupStatusText = null;
        BackupErrorMessage = null;
        RaiseAllChanged();
        _ = _coordinator.RefreshAllAsync();
    }

    public void SendTestNotification()
    {
        _coordinator.SendTestNotification();
    }

    public Task SaveUpdatedSettingsAsync(AppSettingsDto updated)
    {
        lock (_stateSync)
        {
            _desiredSettings = updated;
            _desiredVersion++;
            _flushTask = FlushLoopAsync();
        }
        RaiseAllChanged();
        return _flushTask;
    }

    public Task FlushPendingUpdatesAsync()
    {
        lock (_stateSync)
        {
            return _flushTask;
        }
    }

    private void ApplySettingChange(
        Func<AppSettingsDto, bool> isChanged,
        Func<AppSettingsDto, AppSettingsDto> mutator,
        [CallerMemberName] string? propertyName = null)
    {
        lock (_stateSync)
        {
            if (!isChanged(_desiredSettings))
            {
                return;
            }
            _desiredSettings = mutator(_desiredSettings);
            _desiredVersion++;
            _flushTask = FlushLoopAsync();
        }

        RaisePropertyChanged(propertyName);
    }

    private async Task FlushLoopAsync()
    {
        await _flushLock.WaitAsync().ConfigureAwait(false);
        try
        {
            while (true)
            {
                AppSettingsDto snapshot;
                long versionToFlush;
                lock (_stateSync)
                {
                    if (_flushedVersion == _desiredVersion)
                    {
                        return;
                    }
                    snapshot = _desiredSettings;
                    versionToFlush = _desiredVersion;
                }

                await _coordinator.UpdateSettingsAsync(snapshot).ConfigureAwait(false);

                lock (_stateSync)
                {
                    _flushedVersion = versionToFlush;
                    if (_flushedVersion == _desiredVersion)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private void RaiseAllChanged()
    {
        RaisePropertyChanged(nameof(NotifyOnLive));
        RaisePropertyChanged(nameof(NotifyOnUpcoming));
        RaisePropertyChanged(nameof(PlayNotificationSound));
        RaisePropertyChanged(nameof(LaunchAtLogin));
        RaisePropertyChanged(nameof(ReduceMotion));
        RaisePropertyChanged(nameof(IsAutoUpdateEnabled));
        RaisePropertyChanged(nameof(CurrentVersion));
        RaisePropertyChanged(nameof(CurrentVersionText));
        RaisePropertyChanged(nameof(WatchedChannelCount));
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
