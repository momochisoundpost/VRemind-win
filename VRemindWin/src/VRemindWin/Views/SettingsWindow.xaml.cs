using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using VRemindWin.Core.ViewModels;
using VRemindWin.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace VRemindWin.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly Action<Uri> _onOpenUri;
    private readonly Action<CliReleaseDto> _onOpenUpdateWindow;
    private readonly VRemindWin.Core.Services.CalibrationTapAccumulator _tapAccumulator = new();

    public SettingsWindow(
        SettingsViewModel viewModel,
        Action<Uri> onOpenUri,
        Action<CliReleaseDto> onOpenUpdateWindow)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _onOpenUri = onOpenUri ?? throw new ArgumentNullException(nameof(onOpenUri));
        _onOpenUpdateWindow = onOpenUpdateWindow ?? throw new ArgumentNullException(nameof(onOpenUpdateWindow));

        InitializeComponent();

        AttributionTextBlock.Text = SettingsViewModel.AttributionCopy;
        ForkCopyTextBlock.Text = SettingsViewModel.ForkCopy;

        ViewModel.OnShowUpdateWindow = _onOpenUpdateWindow;
        ViewModel.OnShowEasterEggAlertAsync = ShowEasterEggDialogAsync;

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "VRemind Settings";

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        var pngIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.png");
        if (File.Exists(pngIconPath))
        {
            AboutIconImage.Source = new BitmapImage(new Uri(pngIconPath));
        }

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        uint dpi = GetDpiForWindow(_hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        _appWindow.Resize(new SizeInt32(
            (int)Math.Round(480 * scale),
            (int)Math.Round(480 * scale)));

        Closed += (_, _) =>
        {
            _tapAccumulator.Reset();
            ViewModel.OnShowUpdateWindow = null;
            ViewModel.OnShowEasterEggAlertAsync = null;
            Bindings.StopTracking();
        };
    }

    public SettingsViewModel ViewModel { get; }

    public void ShowAndFocus()
    {
        _appWindow.Show(activateWindow: true);
        Activate();
        _ = SetForegroundWindow(_hwnd);
    }

    private void SelectTab(int index)
    {
        GeneralTabScroll.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsTabScroll.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        AboutTabScroll.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;

        ApplyTabButtonStyle(GeneralTabButton, index == 0);
        ApplyTabButtonStyle(NotificationsTabButton, index == 1);
        ApplyTabButtonStyle(AboutTabButton, index == 2);

        if (index != 2)
        {
            _tapAccumulator.Reset();
        }
    }

    private void ApplyTabButtonStyle(Button button, bool isSelected)
    {
        // Clear any local overrides so the selected/unselected Style setters always take effect cleanly.
        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.BorderBrushProperty);
        button.ClearValue(Control.BorderThicknessProperty);
        button.ClearValue(Control.ForegroundProperty);

        var key = isSelected ? "SelectedTabButtonStyle" : "UnselectedTabButtonStyle";
        if (RootGrid.Resources.TryGetValue(key, out var styleObj) && styleObj is Style style)
        {
            button.Style = style;
        }
    }

    private void GeneralTabButton_Click(object sender, RoutedEventArgs e) => SelectTab(0);

    private void NotificationsTabButton_Click(object sender, RoutedEventArgs e) => SelectTab(1);

    private void AboutTabButton_Click(object sender, RoutedEventArgs e) => SelectTab(2);

    private void AboutIconContainer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!_tapAccumulator.RegisterTap(DateTimeOffset.UtcNow))
        {
            return;
        }

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.png");
        if (VRemindWin.Core.Services.DisplayColorProfileReader.TryDecodeEmbeddedProfile(
                iconPath,
                out VRemindWin.Core.Services.DisplayProfileDocument profile))
        {
            ViewModel.OnShowUpdateWindow?.Invoke(
                new CliReleaseDto(string.Empty, profile.Title, profile.MarkdownBody, string.Empty, string.Empty, IsReaderMode: true));
        }
    }

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        _ = ViewModel.CheckForUpdatesManuallyAsync();
    }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var defaultName = SettingsViewModel.GetDefaultBackupFileName();
            var path = NativeFileDialogService.ShowSaveJsonDialog(_hwnd, defaultName);
            if (!string.IsNullOrWhiteSpace(path))
            {
                await ViewModel.ExportBackupToFileAsync(path);
            }
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("SettingsWindow.ExportBackup", ex);
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = NativeFileDialogService.ShowOpenJsonDialog(_hwnd);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var (isValid, channelCount) = await ViewModel.ValidateBackupFileAsync(path);
            if (!isValid)
            {
                var errorDialog = new ContentDialog
                {
                    XamlRoot = RootGrid.XamlRoot,
                    Title = "Invalid Backup File",
                    Content = ViewModel.BackupErrorMessage ?? "The selected file is not a valid VRemind backup.",
                    CloseButtonText = "OK",
                    DefaultButton = ContentDialogButton.Close
                };
                await errorDialog.ShowAsync();
                return;
            }

            var confirmDialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Restore Backup?",
                Content = $"This will replace your current {ViewModel.WatchedChannelCount} watched channel(s) and settings with {channelCount} channel(s) from \"{Path.GetFileName(path)}\". This action cannot be undone.",
                PrimaryButtonText = "Restore Backup",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            if (await confirmDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.RestoreBackupFromFileAsync(path);
            }
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("SettingsWindow.RestoreBackup", ex);
        }
    }

    private async void FactoryReset_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Reset All Data to Factory Defaults?",
                Content = "This will remove all watched channels, restore default settings, and re-seed the default starter channel (@bukancaitlyn). This action cannot be undone.",
                PrimaryButtonText = "Reset Everything",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await ViewModel.PerformFactoryResetAsync();
            }
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("SettingsWindow.FactoryReset", ex);
        }
    }

    private void SendTestNotification_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SendTestNotification();
    }

    private void OpenWindowsNotificationSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:notifications")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore shell URI errors on stripped OS editions
        }
    }

    private void YoutubeButton_Click(object sender, RoutedEventArgs e)
        => OpenUrlString(SettingsViewModel.YoutubeChannelUrl);

    private void TwitterButton_Click(object sender, RoutedEventArgs e)
        => OpenUrlString(SettingsViewModel.TwitterUrl);

    private void GithubButton_Click(object sender, RoutedEventArgs e)
        => OpenUrlString(SettingsViewModel.GithubUrl);

    private void InstagramButton_Click(object sender, RoutedEventArgs e)
        => OpenUrlString(SettingsViewModel.InstagramUrl);

    private void OpenUrlString(string rawUrl)
    {
        if (LinkSafetyValidator.TryValidateAllowedUri(rawUrl, out var safeUri) && safeUri is not null)
        {
            _onOpenUri(safeUri);
        }
    }

    private async Task ShowEasterEggDialogAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Did you steal Caitlyn's computer?",
            Content = $"Your version ({ViewModel.CurrentVersion}) is newer than what's available on GitHub.",
            PrimaryButtonText = "Yes",
            CloseButtonText = "No",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            OpenUrlString(SettingsViewModel.PoliceSirenUrl);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
