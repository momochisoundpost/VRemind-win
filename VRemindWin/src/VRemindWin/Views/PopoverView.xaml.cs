using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using VRemindWin.Core.ViewModels;
using VRemindWin.Helpers;
using Windows.System;

namespace VRemindWin.Views;

public sealed partial class PopoverView : UserControl
{
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    private readonly Action _onOpenSettings;
    private readonly Action _onQuit;
    private readonly Action<Uri> _onOpenUri;
    private readonly Action _onRequestDismiss;
    private readonly Action<bool> _onModalStateChanged;
    private readonly Stopwatch _rainbowClock = new();
    private bool _isPopoverVisible;
    private bool _isRainbowSubscribed;

    public PopoverView(
        PopoverViewModel viewModel,
        Action onOpenSettings,
        Action onQuit,
        Action<Uri> onOpenUri,
        Action onRequestDismiss,
        Action<bool> onModalStateChanged)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _onOpenSettings = onOpenSettings ?? throw new ArgumentNullException(nameof(onOpenSettings));
        _onQuit = onQuit ?? throw new ArgumentNullException(nameof(onQuit));
        _onOpenUri = onOpenUri ?? throw new ArgumentNullException(nameof(onOpenUri));
        _onRequestDismiss = onRequestDismiss ?? throw new ArgumentNullException(nameof(onRequestDismiss));
        _onModalStateChanged = onModalStateChanged ?? throw new ArgumentNullException(nameof(onModalStateChanged));

        InitializeComponent();
        ViewModel.SecretPromptPresenter = ShowSecretDialogAsync;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += (_, _) => UpdateCelebrationAnimationState();
        Unloaded += (_, _) => StopCelebrationAnimations();
    }

    public PopoverViewModel ViewModel { get; }

    public void FocusInput() => HandleTextBox.Focus(FocusState.Programmatic);

    public void SetPopoverVisible(bool isVisible)
    {
        _isPopoverVisible = isVisible;
        UpdateCelebrationAnimationState();
    }

    public void ApplyChromeAppearance(double cornerRadius, double borderThickness)
    {
        RootGrid.CornerRadius = new CornerRadius(cornerRadius);
        RootGrid.BorderThickness = new Thickness(borderThickness);
        double innerBottomRadius = Math.Max(0.0, cornerRadius - borderThickness);
        BottomAddBar.CornerRadius = new CornerRadius(0, 0, innerBottomRadius, innerBottomRadius);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(PopoverViewModel.IsPopoverCelebrating)
                or nameof(PopoverViewModel.ReduceMotion)
                or nameof(PopoverViewModel.IsLiveSectionCelebrating)
                or nameof(PopoverViewModel.IsUpcomingSectionCelebrating))
        {
            UpdateCelebrationAnimationState();
        }
    }

    private void UpdateCelebrationAnimationState()
    {
        bool isVisible = _isPopoverVisible && IsLoaded;
        bool showConfetti = isVisible && ViewModel.IsPopoverCelebrating && !ViewModel.ReduceMotion;
        ConfettiOverlay.SetActive(showConfetti);

        bool animateRainbow = isVisible &&
            (ViewModel.IsPopoverCelebrating ||
             ViewModel.IsLiveSectionCelebrating ||
             ViewModel.IsUpcomingSectionCelebrating);

        if (animateRainbow)
        {
            RainbowGradientProvider.UpdatePhase(_rainbowClock.Elapsed.TotalSeconds, ViewModel.ReduceMotion);
            if (!_isRainbowSubscribed)
            {
                _rainbowClock.Restart();
                CompositionTarget.Rendering += OnRainbowRendering;
                _isRainbowSubscribed = true;
            }
        }
        else
        {
            UnsubscribeRainbowRendering();
        }
    }

    private void StopCelebrationAnimations()
    {
        ConfettiOverlay.SetActive(false);
        UnsubscribeRainbowRendering();
    }

    private void UnsubscribeRainbowRendering()
    {
        if (_isRainbowSubscribed)
        {
            CompositionTarget.Rendering -= OnRainbowRendering;
            _isRainbowSubscribed = false;
            _rainbowClock.Stop();
        }
    }

    private void OnRainbowRendering(object? sender, object e)
    {
        if (!_isPopoverVisible || !IsLoaded)
        {
            UnsubscribeRainbowRendering();
            return;
        }

        double elapsedSeconds = e is RenderingEventArgs args
            ? args.RenderingTime.TotalSeconds
            : _rainbowClock.Elapsed.TotalSeconds;

        RainbowGradientProvider.UpdatePhase(elapsedSeconds, ViewModel.ReduceMotion);
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            _onRequestDismiss();
        }
    }

    private void PlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlatformComboBox.SelectedItem is ComboBoxItem item && item.Tag is string platform)
        {
            ViewModel.SelectedPlatform = platform;
        }
    }

    private void HandleTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = ViewModel.AddChannelCommandAsync();
        }
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
        => _ = ViewModel.AddChannelCommandAsync();

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
        => _ = ViewModel.RefreshAsync();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => _onOpenSettings();

    private void QuitButton_Click(object sender, RoutedEventArgs e)
        => _onQuit();

    private void ChannelRow_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out var brush) && brush is Brush b)
            {
                grid.Background = b;
            }

            if (FindDeleteButton(grid) is { } btn)
            {
                btn.Visibility = Visibility.Visible;
            }
        }
    }

    private void ChannelRow_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
        {
            grid.Background = TransparentBrush;

            if (FindDeleteButton(grid) is { } btn)
            {
                btn.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ChannelRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CliChannelItem item } &&
            LinkSafetyValidator.TryValidateAllowedUri(item.WatchURL, out var safeUri) &&
            safeUri is not null)
        {
            _onOpenUri(safeUri);
            _onRequestDismiss();
        }
    }

    private void DeleteButton_Tapped(object sender, TappedRoutedEventArgs e)
        => e.Handled = true;

    private void DeleteChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string channelId } && !string.IsNullOrWhiteSpace(channelId))
        {
            _ = RunSafelyAsync(() => ViewModel.RemoveChannelAsync(channelId));
        }
    }

    private static Button? FindDeleteButton(Grid rowGrid)
    {
        foreach (var child in rowGrid.Children)
        {
            if (child is Button button && button.Name == "DeleteButton")
            {
                return button;
            }

            if (child is Panel panel)
            {
                foreach (var inner in panel.Children)
                {
                    if (inner is Button innerButton && innerButton.Name == "DeleteButton")
                    {
                        return innerButton;
                    }
                }
            }
        }

        return null;
    }

    private static async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("PopoverView.AsyncAction", ex);
        }
    }

    private async Task<string> ShowSecretDialogAsync(SecretDialogPayload payload)
    {
        _onModalStateChanged(true);
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = payload.Title,
                Content = payload.Message,
                PrimaryButtonText = payload.Ignore,
                SecondaryButtonText = payload.Abort,
                CloseButtonText = payload.Cancel,
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => "ignore",
                ContentDialogResult.Secondary => "abort",
                _ => "cancel"
            };
        }
        finally
        {
            _onModalStateChanged(false);
        }
    }
}
