using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VRemindWin.Core.Services;
using Windows.UI;

namespace VRemindWin.Views.Controls;

/// <summary>
/// Seamless 32px UWP-style window title bar with left-aligned text title (no corner icon)
/// and a single 46x32 close button matching Windows 10/11 UWP caption chrome.
/// </summary>
public sealed class UwpTitleBarControl : Grid
{
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush WhiteBrush = new(Colors.White);
    private static readonly SolidColorBrush CloseHoverBrush = new(ColorFromRgb(UwpWindowChromePolicy.CloseHoverBackgroundRgb));
    private static readonly SolidColorBrush ClosePressedBrush = new(ColorFromRgb(UwpWindowChromePolicy.ClosePressedBackgroundRgb));

    private readonly Grid _dragRegion;
    private readonly TextBlock _titleTextBlock;
    private readonly Border _closeButtonBorder;
    private readonly FontIcon _closeGlyph;

    private bool _isActive = true;
    private bool _isHovered;
    private bool _isPressed;

    public UwpTitleBarControl()
    {
        Height = UwpWindowChromePolicy.TitleBarHeightDip;
        Background = TransparentBrush;

        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _titleTextBlock = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };

        _dragRegion = new Grid
        {
            Background = TransparentBrush
        };
        _dragRegion.Children.Add(_titleTextBlock);
        _dragRegion.PointerPressed += DragRegion_PointerPressed;
        SetColumn(_dragRegion, 0);
        Children.Add(_dragRegion);

        _closeGlyph = new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph = "\uE8BB",
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _closeButtonBorder = new Border
        {
            Width = UwpWindowChromePolicy.CloseButtonWidthDip,
            Height = UwpWindowChromePolicy.TitleBarHeightDip,
            Background = TransparentBrush,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = _closeGlyph
        };
        AutomationProperties.SetName(_closeButtonBorder, UwpWindowChromePolicy.CloseButtonAccessibleName);
        AutomationProperties.SetAutomationId(_closeButtonBorder, "TitleBarCloseButton");
        ToolTipService.SetToolTip(_closeButtonBorder, UwpWindowChromePolicy.CloseButtonAccessibleName);
        _closeButtonBorder.PointerEntered += CloseButton_PointerEntered;
        _closeButtonBorder.PointerExited += CloseButton_PointerExited;
        _closeButtonBorder.PointerPressed += CloseButton_PointerPressed;
        _closeButtonBorder.PointerReleased += CloseButton_PointerReleased;
        _closeButtonBorder.PointerCanceled += CloseButton_PointerCanceled;
        _closeButtonBorder.PointerCaptureLost += CloseButton_PointerCaptureLost;
        SetColumn(_closeButtonBorder, 1);
        Children.Add(_closeButtonBorder);

        Loaded += (_, _) => UpdateVisualState();
        ActualThemeChanged += (_, _) => UpdateVisualState();
        UpdateVisualState();
    }

    public string TitleText
    {
        get => _titleTextBlock.Text;
        set => _titleTextBlock.Text = value ?? string.Empty;
    }

    public event Action? DragRequested;

    public event Action? CloseRequested;

    public void SetActiveState(bool isActive)
    {
        _isActive = isActive;
        UpdateVisualState();
    }

    private void DragRegion_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(_dragRegion).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            DragRequested?.Invoke();
        }
    }

    private void CloseButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = true;
        UpdateCloseButtonVisualState();
    }

    private void CloseButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = false;
        UpdateCloseButtonVisualState();
    }

    private void CloseButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_closeButtonBorder).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isPressed = true;
        _closeButtonBorder.CapturePointer(e.Pointer);
        e.Handled = true;
        UpdateCloseButtonVisualState();
    }

    private void CloseButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        _closeButtonBorder.ReleasePointerCapture(e.Pointer);

        var pos = e.GetCurrentPoint(_closeButtonBorder).Position;
        bool inside = pos.X >= 0 && pos.X <= _closeButtonBorder.ActualWidth
            && pos.Y >= 0 && pos.Y <= _closeButtonBorder.ActualHeight;
        _isHovered = inside;
        UpdateCloseButtonVisualState();

        e.Handled = true;
        if (inside)
        {
            CloseRequested?.Invoke();
        }
    }

    private void CloseButton_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = false;
        _isHovered = false;
        UpdateCloseButtonVisualState();
    }

    private void CloseButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isPressed = false;
        UpdateCloseButtonVisualState();
    }

    private void UpdateVisualState()
    {
        _titleTextBlock.Foreground = ResolveForegroundBrush(_isActive);
        UpdateCloseButtonVisualState();
    }

    private void UpdateCloseButtonVisualState()
    {
        if (_isPressed && _isHovered)
        {
            _closeButtonBorder.Background = ClosePressedBrush;
            _closeGlyph.Foreground = WhiteBrush;
            return;
        }

        if (_isHovered || _isPressed)
        {
            _closeButtonBorder.Background = CloseHoverBrush;
            _closeGlyph.Foreground = WhiteBrush;
            return;
        }

        _closeButtonBorder.Background = TransparentBrush;
        _closeGlyph.Foreground = ResolveForegroundBrush(_isActive);
    }

    private Brush ResolveForegroundBrush(bool isActive)
    {
        string key = isActive ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush";
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush)
        {
            return brush;
        }

        bool isDark = ActualTheme != ElementTheme.Light;
        Color fallback = (isDark, isActive) switch
        {
            (true, true) => Colors.White,
            (true, false) => Color.FromArgb(179, 255, 255, 255),
            (false, true) => Colors.Black,
            (false, false) => Color.FromArgb(153, 0, 0, 0)
        };
        return new SolidColorBrush(fallback);
    }

    private static Color ColorFromRgb(uint rgb)
        => Color.FromArgb(
            255,
            (byte)((rgb >> 16) & 0xFF),
            (byte)((rgb >> 8) & 0xFF),
            (byte)(rgb & 0xFF));
}
