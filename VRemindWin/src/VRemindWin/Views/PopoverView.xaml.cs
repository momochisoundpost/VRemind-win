using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Input;
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
    private RowDragSession? _dragSession;
    private long _suppressTapUntilTickMs;

    private sealed class RowDragSession
    {
        public required ItemsControl SectionControl { get; init; }
        public required string SectionKind { get; init; }
        public required uint PointerId { get; init; }
        public required int FromIndex { get; init; }
        public int TargetIndex { get; set; }
        public required double StartPointerY { get; init; }
        public required Grid DraggedRowGrid { get; init; }
        public required List<UIElement?> Containers { get; init; }
        public required List<Grid?> RowGrids { get; init; }
        public required double[] RowHeights { get; init; }
        public bool IsDragging { get; set; }
        public bool HasCapturedPointer { get; set; }
    }

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
        if (!isVisible)
        {
            CancelActiveDrag();
        }
        UpdateCelebrationAnimationState();
    }

    public void ApplyChromeAppearance(double cornerRadius, double borderThickness, uint borderColorRef)
    {
        RootGrid.CornerRadius = new CornerRadius(cornerRadius);
        RootGrid.BorderThickness = new Thickness(borderThickness);
        if (borderThickness > 0)
        {
            var borderColor = Windows.UI.Color.FromArgb(
                255,
                (byte)(borderColorRef & 0xFF),
                (byte)((borderColorRef >> 8) & 0xFF),
                (byte)((borderColorRef >> 16) & 0xFF));
            RootGrid.BorderBrush = new SolidColorBrush(borderColor);
        }

        double innerBottomRadius = Math.Max(0.0, cornerRadius - borderThickness);
        BottomAddBar.CornerRadius = new CornerRadius(0, 0, innerBottomRadius, innerBottomRadius);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) ||
            e.PropertyName is nameof(PopoverViewModel.IsPopoverCelebrating)
                or nameof(PopoverViewModel.ReduceMotion)
                or nameof(PopoverViewModel.IsLiveSectionCelebrating)
                or nameof(PopoverViewModel.IsLiveSectionGta6)
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
        if (_dragSession is { IsDragging: true })
        {
            return;
        }

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
        if (_dragSession is { IsDragging: true } session && ReferenceEquals(sender, session.DraggedRowGrid))
        {
            return;
        }

        if (sender is Grid grid)
        {
            grid.Background = TransparentBrush;

            if (FindDeleteButton(grid) is { } btn)
            {
                btn.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void ChannelRow_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _suppressTapUntilTickMs = 0;

        if (sender is not Grid rowGrid)
        {
            return;
        }

        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !e.GetCurrentPoint(rowGrid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (IsOriginatingFromDeleteButton(e.OriginalSource as DependencyObject, rowGrid))
        {
            return;
        }

        if (!TryResolveSectionContext(rowGrid, out var sectionControl, out var sectionKind, out int fromIndex, out var containers, out var rowGrids))
        {
            return;
        }

        if (rowGrids.Count < 2)
        {
            return;
        }

        CancelActiveDrag();

        var heights = new double[rowGrids.Count];
        for (int i = 0; i < rowGrids.Count; i++)
        {
            heights[i] = rowGrids[i]?.ActualHeight > 0 ? rowGrids[i]!.ActualHeight : 50.0;
        }

        double startY = e.GetCurrentPoint(sectionControl).Position.Y;
        _dragSession = new RowDragSession
        {
            SectionControl = sectionControl,
            SectionKind = sectionKind,
            PointerId = e.Pointer.PointerId,
            FromIndex = fromIndex,
            TargetIndex = fromIndex,
            StartPointerY = startY,
            DraggedRowGrid = rowGrid,
            Containers = containers,
            RowGrids = rowGrids,
            RowHeights = heights
        };
    }

    private void ChannelRow_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragSession is not { } session ||
            !ReferenceEquals(sender, session.DraggedRowGrid) ||
            e.Pointer.PointerId != session.PointerId)
        {
            return;
        }

        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse &&
            !e.GetCurrentPoint(session.DraggedRowGrid).Properties.IsLeftButtonPressed)
        {
            CancelActiveDrag();
            return;
        }

        double currentY = e.GetCurrentPoint(session.SectionControl).Position.Y;
        double deltaY = currentY - session.StartPointerY;

        if (!session.IsDragging)
        {
            if (!ChannelOrderPolicy.ExceedsDragThreshold(deltaY))
            {
                return;
            }

            session.IsDragging = true;
            _suppressTapUntilTickMs = Environment.TickCount64 + 250;
            session.HasCapturedPointer = session.DraggedRowGrid.CapturePointer(e.Pointer);

            if (session.FromIndex >= 0 && session.FromIndex < session.Containers.Count &&
                session.Containers[session.FromIndex] is { } draggedContainer)
            {
                Canvas.SetZIndex(draggedContainer, 10);
            }
            Canvas.SetZIndex(session.DraggedRowGrid, 10);
            session.DraggedRowGrid.Opacity = 0.92;

            foreach (var r in session.RowGrids)
            {
                if (r is not null && FindDeleteButton(r) is { } btn)
                {
                    btn.Visibility = Visibility.Collapsed;
                }
            }
        }

        var step = ChannelOrderPolicy.CalculateDragLayout(session.FromIndex, deltaY, session.RowHeights);
        session.TargetIndex = step.TargetIndex;

        for (int i = 0; i < session.RowGrids.Count && i < step.ItemTranslateY.Count; i++)
        {
            if (session.RowGrids[i] is { } row)
            {
                double offset = step.ItemTranslateY[i];
                row.RenderTransform = Math.Abs(offset) > 0.01
                    ? new TranslateTransform { Y = offset }
                    : null;
            }
        }

        e.Handled = true;
    }

    private void ChannelRow_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragSession is not { } session ||
            !ReferenceEquals(sender, session.DraggedRowGrid) ||
            e.Pointer.PointerId != session.PointerId)
        {
            return;
        }

        bool wasDragging = session.IsDragging;
        string sectionKind = session.SectionKind;
        int fromIndex = session.FromIndex;
        int targetIndex = session.TargetIndex;

        CleanupDragVisualsAndRelease(session, e.Pointer);
        _dragSession = null;

        if (wasDragging)
        {
            _suppressTapUntilTickMs = Environment.TickCount64 + 250;
            e.Handled = true;
            if (targetIndex != fromIndex)
            {
                _ = RunSafelyAsync(() => ViewModel.ReorderSectionAsync(sectionKind, fromIndex, targetIndex));
            }
        }
    }

    private void ChannelRow_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (_dragSession is { } session && e.Pointer.PointerId == session.PointerId)
        {
            if (session.IsDragging)
            {
                _suppressTapUntilTickMs = Environment.TickCount64 + 250;
            }
            CleanupDragVisualsAndRelease(session, e.Pointer);
            _dragSession = null;
        }
    }

    private void ChannelRow_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_dragSession is { } session && e.Pointer.PointerId == session.PointerId)
        {
            if (session.IsDragging)
            {
                _suppressTapUntilTickMs = Environment.TickCount64 + 250;
            }
            CleanupDragVisualsAndRelease(session, null);
            _dragSession = null;
        }
    }

    private void ChannelRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (_suppressTapUntilTickMs > 0 && Environment.TickCount64 <= _suppressTapUntilTickMs)
        {
            _suppressTapUntilTickMs = 0;
            e.Handled = true;
            return;
        }
        _suppressTapUntilTickMs = 0;

        if (sender is FrameworkElement { DataContext: CliChannelItem item } &&
            LinkSafetyValidator.TryValidateAllowedUri(item.WatchURL, out var safeUri) &&
            safeUri is not null)
        {
            _onOpenUri(safeUri);
            _onRequestDismiss();
        }
    }

    private void CancelActiveDrag()
    {
        if (_dragSession is { } session)
        {
            CleanupDragVisualsAndRelease(session, null);
            _dragSession = null;
        }
    }

    private static void CleanupDragVisualsAndRelease(RowDragSession session, Pointer? pointer)
    {
        for (int i = 0; i < session.Containers.Count; i++)
        {
            if (session.Containers[i] is { } container)
            {
                Canvas.SetZIndex(container, 0);
            }
        }

        foreach (var row in session.RowGrids)
        {
            if (row is not null)
            {
                row.RenderTransform = null;
                row.Opacity = 1.0;
                Canvas.SetZIndex(row, 0);
            }
        }

        if (session.HasCapturedPointer)
        {
            session.HasCapturedPointer = false;
            try
            {
                if (pointer is not null)
                {
                    session.DraggedRowGrid.ReleasePointerCapture(pointer);
                }
                else
                {
                    session.DraggedRowGrid.ReleasePointerCaptures();
                }
            }
            catch
            {
                // Ignore if pointer capture already released
            }
        }
    }

    private bool TryResolveSectionContext(
        Grid targetRowGrid,
        out ItemsControl sectionControl,
        out string sectionKind,
        out int foundIndex,
        out List<UIElement?> containers,
        out List<Grid?> rowGrids)
    {
        ItemsControl[] candidates = [LiveItemsControl, UpcomingItemsControl, OfflineItemsControl];
        foreach (var candidate in candidates)
        {
            int count = candidate.Items.Count;
            if (count == 0)
            {
                continue;
            }

            int matchIndex = -1;
            var candidateContainers = new List<UIElement?>(count);
            var candidateRows = new List<Grid?>(count);

            for (int i = 0; i < count; i++)
            {
                var container = candidate.ContainerFromIndex(i) as UIElement;
                candidateContainers.Add(container);

                Grid? row = container is not null && VisualTreeHelper.GetChildrenCount(container) > 0
                    ? VisualTreeHelper.GetChild(container, 0) as Grid
                    : null;
                candidateRows.Add(row);

                if (ReferenceEquals(row, targetRowGrid))
                {
                    matchIndex = i;
                }
            }

            if (matchIndex >= 0)
            {
                sectionControl = candidate;
                sectionKind = candidate.Tag as string ?? "offline";
                foundIndex = matchIndex;
                containers = candidateContainers;
                rowGrids = candidateRows;
                return true;
            }
        }

        sectionControl = LiveItemsControl;
        sectionKind = string.Empty;
        foundIndex = -1;
        containers = [];
        rowGrids = [];
        return false;
    }

    private static bool IsOriginatingFromDeleteButton(DependencyObject? source, Grid rowGrid)
    {
        var current = source;
        while (current is not null && !ReferenceEquals(current, rowGrid))
        {
            if (current is Button { Name: "DeleteButton" })
            {
                return true;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
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
