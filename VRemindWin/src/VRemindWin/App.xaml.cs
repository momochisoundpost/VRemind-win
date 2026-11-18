using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using VRemindWin.Core.ViewModels;
using VRemindWin.Services;
using VRemindWin.Views;

namespace VRemindWin;

public partial class App : Application
{
    private readonly SingleInstanceService _singleInstance;
    private DispatcherQueue? _dispatcherQueue;
    private WindowsToastService? _toastService;
    private StreamMonitorCoordinator? _coordinator;
    private PopoverViewModel? _popoverViewModel;
    private SettingsViewModel? _settingsViewModel;
    private TrayIconService? _trayIconService;
    private PopoverWindow? _popoverWindow;
    private SettingsWindow? _settingsWindow;
    private OnboardingWindow? _onboardingWindow;
    private UpdateAvailableWindow? _updateAvailableWindow;
    private bool _isExiting;

    public App(SingleInstanceService singleInstance)
    {
        _singleInstance = singleInstance ?? throw new ArgumentNullException(nameof(singleInstance));
        UnhandledException += (_, e) =>
        {
            Program.WriteCrashLog("Application.UnhandledException", e.Exception, e.Message);
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var appExePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "VRemindWin.exe");
        var cliExePath = ResolveCliExecutablePath();

        _toastService = new WindowsToastService();
        _toastService.EnsureRegistered(appExePath, OpenValidatedUri);

        var bridge = new CoreCliBridge(cliExePath);
        var startupRegistry = new StartupRegistryService();

        _coordinator = new StreamMonitorCoordinator(
            bridge: bridge,
            toastDispatcher: _toastService,
            pollInterval: TimeSpan.FromSeconds(180),
            uiDispatcher: action => _dispatcherQueue?.TryEnqueue(() => action()));

        _coordinator.PropertyChanged += Coordinator_PropertyChanged;

        _popoverViewModel = new PopoverViewModel(_coordinator);
        _settingsViewModel = new SettingsViewModel(_coordinator, startupRegistry, appExePath);

        _popoverWindow = new PopoverWindow(
            viewModel: _popoverViewModel,
            onOpenSettings: OpenSettingsWindow,
            onQuit: QuitApplication,
            onOpenUri: OpenValidatedUri);

        var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        _trayIconService = new TrayIconService(
            normalIconPath: Path.Combine(assetsDir, "tray-normal.ico"),
            liveIconPath: Path.Combine(assetsDir, "tray-live.ico"),
            gta6IconPath: Path.Combine(assetsDir, "tray-gta6.ico"),
            onTogglePopover: TogglePopoverFromTray,
            onRefresh: () => _ = RefreshSafelyAsync(_coordinator),
            onOpenSettings: OpenSettingsWindow,
            onQuit: QuitApplication);

        _singleInstance.StartActivationPipeServer(() => _dispatcherQueue?.TryEnqueue(ShowPopoverNearTray));

        SystemEvents.PowerModeChanged += SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;

        _ = StartMonitoringAsync(startupRegistry, appExePath);
    }

    private async Task StartMonitoringAsync(StartupRegistryService startupRegistry, string appExePath)
    {
        if (_coordinator is null || _isExiting)
        {
            return;
        }

        try
        {
            await _coordinator.InitializeAsync();
            if (_coordinator.CurrentState.Settings.LaunchAtLogin && startupRegistry.IsLaunchAtLoginEnabled())
            {
                startupRegistry.SetLaunchAtLogin(true, appExePath);
            }
            UpdateTrayLiveState();

            if (!_coordinator.CurrentState.Settings.HasCompletedOnboarding)
            {
                _dispatcherQueue?.TryEnqueue(OpenOnboardingWindow);
            }
            else if (string.Equals(_coordinator.CurrentState.Settings.UpdateConsent, "allowed", StringComparison.OrdinalIgnoreCase))
            {
                _ = CheckForStartupUpdatesSafelyAsync();
            }
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("App.InitializeAsync", ex);
        }

        if (!_isExiting)
        {
            _coordinator.StartPolling(refreshImmediately: true);
        }
    }

    private static async Task RefreshSafelyAsync(StreamMonitorCoordinator? coordinator)
    {
        if (coordinator is null)
        {
            return;
        }

        try
        {
            await coordinator.RefreshAllAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("App.Refresh", ex);
        }
    }

    private void Coordinator_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StreamMonitorCoordinator.CurrentState))
        {
            UpdateTrayLiveState();
        }
    }

    private void UpdateTrayLiveState()
    {
        if (_coordinator is null || _trayIconService is null)
        {
            return;
        }

        _trayIconService.SetTrayState(TrayVisualStatePolicy.Resolve(_coordinator.CurrentState));
    }

    private void TogglePopoverFromTray()
    {
        if (_popoverWindow is null || _trayIconService is null)
        {
            return;
        }

        if (_popoverWindow.IsFlyoutVisible)
        {
            _popoverWindow.HideFlyout();
            return;
        }

        if (_popoverWindow.ConsumeRecentDeactivation())
        {
            return;
        }

        ShowPopoverNearTray();
    }

    private void ShowPopoverNearTray()
    {
        if (_popoverWindow is null || _trayIconService is null)
        {
            return;
        }

        var bounds = _trayIconService.GetFlyoutBounds();
        _popoverWindow.ShowAt(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private void OpenSettingsWindow()
    {
        if (_settingsViewModel is null)
        {
            return;
        }

        _popoverWindow?.HideFlyout();

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settingsViewModel, OpenValidatedUri, OpenUpdateAvailableWindow);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.ShowAndFocus();
    }

    private void OpenOnboardingWindow()
    {
        if (_coordinator is null || _isExiting)
        {
            return;
        }

        if (_onboardingWindow is null)
        {
            var onboardingVm = new OnboardingViewModel(_coordinator);
            _onboardingWindow = new OnboardingWindow(onboardingVm);
            _onboardingWindow.Closed += (_, _) => _onboardingWindow = null;
        }

        _onboardingWindow.ShowAndFocus();
    }

    private void OpenUpdateAvailableWindow(CliReleaseDto release)
    {
        _dispatcherQueue?.TryEnqueue(() =>
        {
            if (_isExiting)
            {
                return;
            }

            var currentVersion = _settingsViewModel?.CurrentVersion ?? "1.3.3";
            if (_updateAvailableWindow is null)
            {
                _updateAvailableWindow = new UpdateAvailableWindow(
                    release: release,
                    currentVersion: currentVersion,
                    onOpenUri: OpenValidatedUri,
                    onSkipVersion: tag => _settingsViewModel is not null
                        ? _settingsViewModel.SkipUpdateVersionAsync(tag)
                        : Task.CompletedTask);
                _updateAvailableWindow.Closed += (_, _) => _updateAvailableWindow = null;
            }
            else
            {
                _updateAvailableWindow.UpdateRelease(release, currentVersion);
            }

            _updateAvailableWindow.ShowAndFocus();
        });
    }

    private async Task CheckForStartupUpdatesSafelyAsync()
    {
        if (_coordinator is null || _isExiting)
        {
            return;
        }

        try
        {
            var response = await _coordinator.CheckForUpdatesAsync(manual: false).ConfigureAwait(false);
            if (response.UpdateResult is { Status: "updateAvailable", Release: not null } update)
            {
                OpenUpdateAvailableWindow(update.Release);
            }
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("App.StartupUpdateCheck", ex);
        }
    }

    private static void OpenValidatedUri(Uri uri)
    {
        if (!LinkSafetyValidator.TryValidateAllowedUri(uri.AbsoluteUri, out var safeUri) || safeUri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(safeUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell launch errors if no browser is registered
        }
    }

    private void SystemEvents_PowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        var coordinator = _coordinator;
        if (_isExiting || coordinator is null)
        {
            return;
        }

        if (e.Mode == PowerModes.Suspend)
        {
            coordinator.HandlePowerOrSessionPause();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isExiting)
                {
                    _ = ResumeCoordinatorSafelyAsync(coordinator);
                }
            });
        }
    }

    private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        var coordinator = _coordinator;
        if (_isExiting || coordinator is null)
        {
            return;
        }

        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            coordinator.HandlePowerOrSessionPause();
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _dispatcherQueue?.TryEnqueue(() =>
            {
                if (!_isExiting)
                {
                    _ = ResumeCoordinatorSafelyAsync(coordinator);
                }
            });
        }
    }

    private static async Task ResumeCoordinatorSafelyAsync(StreamMonitorCoordinator coordinator)
    {
        try
        {
            await coordinator.HandlePowerOrSessionResumeAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Coordinator disposed during shutdown
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("App.PowerResume", ex);
        }
    }

    private void QuitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;

        SystemEvents.PowerModeChanged -= SystemEvents_PowerModeChanged;
        SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;

        _onboardingWindow?.Close();
        _onboardingWindow = null;

        _updateAvailableWindow?.Close();
        _updateAvailableWindow = null;

        _settingsWindow?.Close();
        _settingsWindow = null;

        _popoverWindow?.ForceClose();
        _popoverWindow = null;

        _trayIconService?.Dispose();
        _trayIconService = null;

        _coordinator?.Dispose();
        _coordinator = null;

        _toastService?.Dispose();
        _toastService = null;

        Exit();
    }

    private static string ResolveCliExecutablePath()
    {
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "vremind-core.exe");
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && current is not null; i++, current = current.Parent)
        {
            var devPath = Path.Combine(current.FullName, "artifacts", "cli", "vremind-core.exe");
            if (File.Exists(devPath))
            {
                return devPath;
            }
        }

        return bundledPath;
    }
}
