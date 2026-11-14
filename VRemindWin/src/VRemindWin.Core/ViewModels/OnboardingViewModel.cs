using System.ComponentModel;
using System.Runtime.CompilerServices;
using VRemindWin.Core.Services;

namespace VRemindWin.Core.ViewModels;

public sealed class OnboardingViewModel : INotifyPropertyChanged
{
    private readonly StreamMonitorCoordinator _coordinator;
    private string _selectedPlatform = "youtube";
    private string _customHandle = "@bukancaitlyn";
    private string? _validationError;
    private bool _enableUpdateChecks = true;
    private bool _neverAskUpdatesAgain;
    private bool _hasSentTestNotification;
    private bool _isSubmitting;

    public OnboardingViewModel(StreamMonitorCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

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
            }
        }
    }

    public string InputPlaceholder => SelectedPlatform == "twitch"
        ? "Enter Twitch username (e.g. ironmouse)"
        : "Enter YouTube @handle (e.g. @bukancaitlyn)";

    public string CustomHandle
    {
        get => _customHandle;
        set
        {
            if (_customHandle != value)
            {
                _customHandle = value;
                RaisePropertyChanged();
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

    public bool EnableUpdateChecks
    {
        get => _enableUpdateChecks;
        set
        {
            if (_enableUpdateChecks != value)
            {
                _enableUpdateChecks = value;
                if (value && _neverAskUpdatesAgain)
                {
                    _neverAskUpdatesAgain = false;
                    RaisePropertyChanged(nameof(NeverAskUpdatesAgain));
                }
                RaisePropertyChanged();
            }
        }
    }

    public bool NeverAskUpdatesAgain
    {
        get => _neverAskUpdatesAgain;
        set
        {
            if (_neverAskUpdatesAgain != value)
            {
                _neverAskUpdatesAgain = value;
                if (value && _enableUpdateChecks)
                {
                    _enableUpdateChecks = false;
                    RaisePropertyChanged(nameof(EnableUpdateChecks));
                }
                RaisePropertyChanged();
            }
        }
    }

    public bool HasSentTestNotification
    {
        get => _hasSentTestNotification;
        private set
        {
            if (_hasSentTestNotification != value)
            {
                _hasSentTestNotification = value;
                RaisePropertyChanged();
            }
        }
    }

    public void SendTestNotification()
    {
        _coordinator.SendTestNotification();
        HasSentTestNotification = true;
    }

    public async Task<bool> CompleteOnboardingAsync()
    {
        if (_isSubmitting)
        {
            return false;
        }

        _isSubmitting = true;
        try
        {
            var consent = EnableUpdateChecks
                ? "allowed"
                : (NeverAskUpdatesAgain ? "neverAsk" : "undecided");

            var response = await _coordinator.CompleteOnboardingAsync(
                SelectedPlatform,
                CustomHandle.Trim(),
                consent);

            if (string.Equals(response.Outcome, "validation_error", StringComparison.OrdinalIgnoreCase))
            {
                ValidationError = response.ErrorMessage ?? "Please enter a valid channel handle or username.";
                return false;
            }

            ValidationError = null;
            _ = _coordinator.RefreshAllAsync();
            return true;
        }
        finally
        {
            _isSubmitting = false;
        }
    }

    private void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
