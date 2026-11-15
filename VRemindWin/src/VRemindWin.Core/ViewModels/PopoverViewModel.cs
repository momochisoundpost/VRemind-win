using System.ComponentModel;
using System.Runtime.CompilerServices;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;

namespace VRemindWin.Core.ViewModels;

public sealed class PopoverViewModel : INotifyPropertyChanged
{
    private readonly StreamMonitorCoordinator _coordinator;
    private string _selectedPlatform = "youtube";
    private string _newHandleInput = string.Empty;
    private string? _validationError;
    private bool _isAdding;

    public PopoverViewModel(StreamMonitorCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _coordinator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StreamMonitorCoordinator.CurrentState))
            {
                RaiseStatePropertiesChanged();
            }
            else if (e.PropertyName == nameof(StreamMonitorCoordinator.IsRefreshing))
            {
                RaisePropertyChanged(nameof(IsRefreshing));
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Func<SecretDialogPayload, Task<string>>? SecretPromptPresenter { get; set; }

    public string SelectedPlatform
    {
        get => _selectedPlatform;
        set
        {
            var normalized = string.Equals(value, "twitch", StringComparison.OrdinalIgnoreCase)
                ? "twitch"
                : "youtube";
            if (_selectedPlatform != normalized)
            {
                _selectedPlatform = normalized;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(InputPlaceholder));
                RaisePropertyChanged(nameof(SelectedPlatformDisplayName));
                RaisePropertyChanged(nameof(SelectedPlatformColorHex));
            }
        }
    }

    public string SelectedPlatformDisplayName => SelectedPlatform == "twitch" ? "Twitch" : "YouTube";

    public string SelectedPlatformColorHex => SelectedPlatform == "twitch" ? "#FF9146FF" : "#FFFF0000";

    public string InputPlaceholder => SelectedPlatform == "twitch"
        ? "Add Twitch username..."
        : "Add YouTube @handle...";

    public string NewHandleInput
    {
        get => _newHandleInput;
        set
        {
            if (_newHandleInput != value)
            {
                _newHandleInput = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(CanAddChannel));
            }
        }
    }

    public string? ValidationError
    {
        get => _validationError;
        private set
        {
            if (_validationError != value)
            {
                _validationError = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HasValidationError));
            }
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(_validationError);

    public bool CanAddChannel => !_isAdding && !string.IsNullOrWhiteSpace(_newHandleInput);

    public bool IsRefreshing => _coordinator.IsRefreshing;

    public int LiveCount => _coordinator.CurrentState.LiveCount;

    public bool HasLiveChannels => LiveCount > 0;

    public string LiveCountBadgeText => $"{LiveCount} LIVE";

    public bool IsPopoverCelebrating => _coordinator.CurrentState.IsPopoverCelebrating;

    public bool ReduceMotion => _coordinator.CurrentState.Settings.ReduceMotion;

    public IReadOnlyList<CliChannelItem> LiveChannels => _coordinator.CurrentState.Sections.Live;

    public IReadOnlyList<CliChannelItem> UpcomingChannels => _coordinator.CurrentState.Sections.Upcoming;

    public IReadOnlyList<CliChannelItem> OfflineChannels => _coordinator.CurrentState.Sections.Offline;

    public bool HasLiveSection => LiveChannels.Count > 0;

    public bool HasUpcomingSection => UpcomingChannels.Count > 0;

    public bool HasOfflineSection => OfflineChannels.Count > 0;

    public bool IsLiveSectionCelebrating => LiveChannels.Any(c => c.IsCelebration);

    public bool IsUpcomingSectionCelebrating => UpcomingChannels.Any(c => c.IsCelebration);

    public string LiveSectionHeader => $"LIVE NOW ({LiveChannels.Count})";

    public string UpcomingSectionHeader => $"UPCOMING ({UpcomingChannels.Count})";

    public string OfflineSectionHeader => $"OFFLINE ({OfflineChannels.Count})";

    public bool IsEmpty => !HasLiveSection && !HasUpcomingSection && !HasOfflineSection;

    public async Task AddChannelCommandAsync()
    {
        var trimmed = NewHandleInput.Trim();
        if (string.IsNullOrEmpty(trimmed) || _isAdding)
        {
            return;
        }

        _isAdding = true;
        RaisePropertyChanged(nameof(CanAddChannel));
        try
        {
            var response = await _coordinator.AddChannelAsync(SelectedPlatform, trimmed, ignoreSecret: false);

            if (string.Equals(response.Outcome, "validation_error", StringComparison.OrdinalIgnoreCase))
            {
                ValidationError = response.ErrorMessage ?? "Invalid channel handle or URL";
                return;
            }

            if (string.Equals(response.Outcome, "secret_prompt", StringComparison.OrdinalIgnoreCase) &&
                response.SecretDialog is not null)
            {
                var action = SecretPromptPresenter is not null
                    ? await SecretPromptPresenter(response.SecretDialog)
                    : "cancel";

                if (!string.Equals(action, "ignore", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                response = await _coordinator.AddChannelAsync(SelectedPlatform, trimmed, ignoreSecret: true);
                if (string.Equals(response.Outcome, "validation_error", StringComparison.OrdinalIgnoreCase))
                {
                    ValidationError = response.ErrorMessage ?? "Invalid channel handle or URL";
                    return;
                }
            }

            ValidationError = null;
            NewHandleInput = string.Empty;
            _ = _coordinator.RefreshAllAsync();
        }
        finally
        {
            _isAdding = false;
            RaisePropertyChanged(nameof(CanAddChannel));
        }
    }

    public Task RemoveChannelCommandAsync(string channelId)
        => _coordinator.RemoveChannelAsync(channelId);

    public Task RemoveChannelAsync(string channelId)
        => _coordinator.RemoveChannelAsync(channelId);

    public Task ReorderSectionAsync(string sectionKind, int fromIndex, int toIndex)
        => _coordinator.ReorderSectionAsync(sectionKind, fromIndex, toIndex);

    public Task RefreshCommandAsync()
        => _coordinator.RefreshAllAsync();

    public Task RefreshAsync()
        => _coordinator.RefreshAllAsync();

    private void RaiseStatePropertiesChanged()
    {
        RaisePropertyChanged(nameof(LiveCount));
        RaisePropertyChanged(nameof(HasLiveChannels));
        RaisePropertyChanged(nameof(LiveCountBadgeText));
        RaisePropertyChanged(nameof(IsPopoverCelebrating));
        RaisePropertyChanged(nameof(ReduceMotion));
        RaisePropertyChanged(nameof(LiveChannels));
        RaisePropertyChanged(nameof(UpcomingChannels));
        RaisePropertyChanged(nameof(OfflineChannels));
        RaisePropertyChanged(nameof(HasLiveSection));
        RaisePropertyChanged(nameof(HasUpcomingSection));
        RaisePropertyChanged(nameof(HasOfflineSection));
        RaisePropertyChanged(nameof(IsLiveSectionCelebrating));
        RaisePropertyChanged(nameof(IsUpcomingSectionCelebrating));
        RaisePropertyChanged(nameof(LiveSectionHeader));
        RaisePropertyChanged(nameof(UpcomingSectionHeader));
        RaisePropertyChanged(nameof(OfflineSectionHeader));
        RaisePropertyChanged(nameof(IsEmpty));
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
