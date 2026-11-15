using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VRemindWin.Core.Services;
using VRemindWin.Views.Controls;
using Windows.UI;

namespace VRemindWin.Helpers;

/// <summary>
/// Replaces Win32 caption/border chrome on standard overlapped windows with a seamless UWP border,
/// DWM shadow and dark-mode styling, title-bar dragging, and optional edge resizing.
/// </summary>
public sealed class UwpWindowChromeController : IDisposable
{
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmGetMinMaxInfo = 0x0024;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmNcCalcSize = 0x0083;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmNcLButtonDown = 0x00A1;
    private const nuint ChromeSubclassId = 2;

    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly Grid _chromeBorderGrid;
    private readonly UwpTitleBarControl _titleBar;
    private readonly bool _isResizable;
    private readonly int _minWidthDip;
    private readonly int _minHeightDip;
    private readonly SubclassProcDelegate _subclassProc;
    private readonly List<ResizeGripElement> _resizeGrips = [];

    private bool _isSubclassAttached;
    private bool _isWindowActive = true;
    private bool _isDisposed;

    public UwpWindowChromeController(
        Window window,
        IntPtr hwnd,
        AppWindow appWindow,
        Grid chromeBorderGrid,
        UwpTitleBarControl titleBar,
        bool isResizable,
        int minWidthDip = 360,
        int minHeightDip = 300)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _hwnd = hwnd;
        _appWindow = appWindow ?? throw new ArgumentNullException(nameof(appWindow));
        _chromeBorderGrid = chromeBorderGrid ?? throw new ArgumentNullException(nameof(chromeBorderGrid));
        _titleBar = titleBar ?? throw new ArgumentNullException(nameof(titleBar));
        _isResizable = isResizable;
        _minWidthDip = minWidthDip;
        _minHeightDip = minHeightDip;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            // IsResizable must be false before SetBorderAndTitleBar(false, false) on Windows 10/11.
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _subclassProc = ChromeSubclassProc;
        AttachWindowSubclass();
        ApplyNormalizedFrameStyles();
        ApplyChromeForCurrentState();

        if (_isResizable)
        {
            InstallResizeGrips();
        }

        _chromeBorderGrid.Loaded += ChromeBorderGrid_Loaded;
        _chromeBorderGrid.ActualThemeChanged += ChromeBorderGrid_ActualThemeChanged;
        _window.Activated += Window_Activated;
        _titleBar.DragRequested += TitleBar_DragRequested;
        _titleBar.CloseRequested += TitleBar_CloseRequested;
    }

    public void EnsureChromeStyles()
    {
        if (_isDisposed)
        {
            return;
        }

        ApplyNormalizedFrameStyles();
        ApplyChromeForCurrentState();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _chromeBorderGrid.Loaded -= ChromeBorderGrid_Loaded;
        _chromeBorderGrid.ActualThemeChanged -= ChromeBorderGrid_ActualThemeChanged;
        _window.Activated -= Window_Activated;
        _titleBar.DragRequested -= TitleBar_DragRequested;
        _titleBar.CloseRequested -= TitleBar_CloseRequested;

        foreach (var grip in _resizeGrips)
        {
            grip.PointerPressed -= ResizeGrip_PointerPressed;
            _chromeBorderGrid.Children.Remove(grip);
        }
        _resizeGrips.Clear();

        DetachWindowSubclass();
    }

    private void ChromeBorderGrid_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyNormalizedFrameStyles();
        ApplyChromeForCurrentState();
    }

    private void ChromeBorderGrid_ActualThemeChanged(FrameworkElement sender, object args)
        => ApplyChromeForCurrentState();

    private void Window_Activated(object sender, WindowActivatedEventArgs args)
    {
        _isWindowActive = args.WindowActivationState != WindowActivationState.Deactivated;
        ApplyNormalizedFrameStyles();
        ApplyChromeForCurrentState();
    }

    private void TitleBar_DragRequested()
        => StartNonClientAction(UwpWindowChromePolicy.HtCaption);

    private void TitleBar_CloseRequested()
        => _window.Close();

    private void ResizeGrip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ResizeGripElement grip && e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            StartNonClientAction(grip.HitTestCode);
        }
    }

    private void StartNonClientAction(int hitTestCode)
    {
        if (_isDisposed)
        {
            return;
        }

        _ = ReleaseCapture();
        IntPtr lParam = IntPtr.Zero;
        if (GetCursorPos(out var pt))
        {
            lParam = new IntPtr(((pt.Y & 0xFFFF) << 16) | (pt.X & 0xFFFF));
        }

        _ = SendMessageW(_hwnd, WmNcLButtonDown, new IntPtr(hitTestCode), lParam);
    }

    private void InstallResizeGrips()
    {
        double edge = UwpWindowChromePolicy.ResizeBorderThicknessDip;
        double corner = edge * 2.0;
        double closeWidth = UwpWindowChromePolicy.CloseButtonWidthDip;
        double titleHeight = UwpWindowChromePolicy.TitleBarHeightDip;

        // Leave the 46x32 top-right close button unobstructed so Fitts's law top-right clicks always close.
        AddGrip(UwpWindowChromePolicy.HtLeft, InputSystemCursorShape.SizeWestEast,
            HorizontalAlignment.Left, VerticalAlignment.Stretch, edge, double.NaN, new Thickness(0, corner, 0, corner));
        AddGrip(UwpWindowChromePolicy.HtRight, InputSystemCursorShape.SizeWestEast,
            HorizontalAlignment.Right, VerticalAlignment.Stretch, edge, double.NaN, new Thickness(0, titleHeight, 0, corner));
        AddGrip(UwpWindowChromePolicy.HtTop, InputSystemCursorShape.SizeNorthSouth,
            HorizontalAlignment.Stretch, VerticalAlignment.Top, double.NaN, edge, new Thickness(corner, 0, closeWidth, 0));
        AddGrip(UwpWindowChromePolicy.HtBottom, InputSystemCursorShape.SizeNorthSouth,
            HorizontalAlignment.Stretch, VerticalAlignment.Bottom, double.NaN, edge, new Thickness(corner, 0, corner, 0));

        AddGrip(UwpWindowChromePolicy.HtTopLeft, InputSystemCursorShape.SizeNorthwestSoutheast,
            HorizontalAlignment.Left, VerticalAlignment.Top, corner, corner, new Thickness(0));
        AddGrip(UwpWindowChromePolicy.HtBottomLeft, InputSystemCursorShape.SizeNortheastSouthwest,
            HorizontalAlignment.Left, VerticalAlignment.Bottom, corner, corner, new Thickness(0));
        AddGrip(UwpWindowChromePolicy.HtBottomRight, InputSystemCursorShape.SizeNorthwestSoutheast,
            HorizontalAlignment.Right, VerticalAlignment.Bottom, corner, corner, new Thickness(0));
    }

    private void AddGrip(
        int hitTestCode,
        InputSystemCursorShape cursorShape,
        HorizontalAlignment hAlign,
        VerticalAlignment vAlign,
        double width,
        double height,
        Thickness margin)
    {
        var grip = new ResizeGripElement(hitTestCode, cursorShape)
        {
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
            Margin = margin
        };

        if (!double.IsNaN(width))
        {
            grip.Width = width;
        }

        if (!double.IsNaN(height))
        {
            grip.Height = height;
        }

        Grid.SetRow(grip, 0);
        Grid.SetRowSpan(grip, Math.Max(1, _chromeBorderGrid.RowDefinitions.Count));
        Canvas.SetZIndex(grip, 100);
        grip.PointerPressed += ResizeGrip_PointerPressed;

        _resizeGrips.Add(grip);
        _chromeBorderGrid.Children.Add(grip);
    }

    private void AttachWindowSubclass()
    {
        if (!_isSubclassAttached && SetWindowSubclass(_hwnd, _subclassProc, ChromeSubclassId, IntPtr.Zero))
        {
            _isSubclassAttached = true;
        }
    }

    private void DetachWindowSubclass()
    {
        if (_isSubclassAttached)
        {
            _ = RemoveWindowSubclass(_hwnd, _subclassProc, ChromeSubclassId);
            _isSubclassAttached = false;
        }
    }

    private IntPtr ChromeSubclassProc(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam,
        nuint uIdSubclass,
        IntPtr dwRefData)
    {
        if (uMsg == WmNcCalcSize && wParam != IntPtr.Zero)
        {
            // Eliminate Win32 non-client border/titlebar insets while keeping DWM shadow and optional WS_THICKFRAME.
            return IntPtr.Zero;
        }

        if (uMsg == WmEraseBkgnd)
        {
            return new IntPtr(1);
        }

        if (uMsg == WmGetMinMaxInfo && _isResizable && lParam != IntPtr.Zero)
        {
            _ = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            uint dpi = GetDpiForWindow(hWnd);
            double scale = dpi == 0 ? 1.0 : dpi / 96.0;
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMinTrackSize.X = Math.Max(mmi.ptMinTrackSize.X, (int)Math.Round(_minWidthDip * scale));
            mmi.ptMinTrackSize.Y = Math.Max(mmi.ptMinTrackSize.Y, (int)Math.Round(_minHeightDip * scale));
            Marshal.StructureToPtr(mmi, lParam, fDeleteOld: false);
            return IntPtr.Zero;
        }

        if (uMsg == WmNcHitTest && _isResizable)
        {
            if (GetWindowRect(hWnd, out var rect))
            {
                long raw = lParam.ToInt64();
                int screenX = unchecked((short)(raw & 0xFFFF));
                int screenY = unchecked((short)((raw >> 16) & 0xFFFF));
                uint dpi = GetDpiForWindow(hWnd);
                double scale = dpi == 0 ? 1.0 : dpi / 96.0;

                double xDip = (screenX - rect.Left) / scale;
                double yDip = (screenY - rect.Top) / scale;
                double widthDip = (rect.Right - rect.Left) / scale;
                double heightDip = (rect.Bottom - rect.Top) / scale;

                bool inCloseButton = xDip >= widthDip - UwpWindowChromePolicy.CloseButtonWidthDip
                    && yDip >= 0
                    && yDip < UwpWindowChromePolicy.TitleBarHeightDip;
                if (!inCloseButton)
                {
                    int ht = UwpWindowChromePolicy.HitTestResizeBorder(
                        xDip,
                        yDip,
                        widthDip,
                        heightDip,
                        UwpWindowChromePolicy.ResizeBorderThicknessDip,
                        isResizable: true);
                    if (ht != UwpWindowChromePolicy.HtClient)
                    {
                        return new IntPtr(ht);
                    }
                }
            }
        }

        if (uMsg == WmNcDestroy)
        {
            DetachWindowSubclass();
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ApplyChromeForCurrentState()
    {
        if (_isDisposed)
        {
            return;
        }

        int osBuild = Environment.OSVersion.Version.Build;
        bool isDarkTheme = _chromeBorderGrid.ActualTheme != ElementTheme.Light;

        var shadowMargins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 1 };
        _ = DwmExtendFrameIntoClientArea(_hwnd, ref shadowMargins);

        uint useDark = isDarkTheme ? 1u : 0u;
        if (DwmSetWindowAttribute(_hwnd, UwpWindowChromePolicy.DwmWaUseImmersiveDarkMode, ref useDark, sizeof(uint)) != 0)
        {
            _ = DwmSetWindowAttribute(_hwnd, UwpWindowChromePolicy.DwmWaUseImmersiveDarkModeLegacy, ref useDark, sizeof(uint));
        }

        bool dwmApplied = false;
        var candidate = UwpWindowChromePolicy.ResolveChromeLayout(
            osBuild,
            dwmAttributesSucceeded: true,
            isDarkTheme: isDarkTheme,
            isWindowActive: _isWindowActive);

        if (UwpWindowChromePolicy.IsWindows11OrGreater(osBuild))
        {
            uint cornerPref = candidate.CornerPreferenceValue;
            int hrCorner = DwmSetWindowAttribute(
                _hwnd,
                UwpWindowChromePolicy.DwmWaWindowCornerPreference,
                ref cornerPref,
                sizeof(uint));

            uint borderColor = candidate.BorderColorRef;
            int hrBorder = DwmSetWindowAttribute(
                _hwnd,
                UwpWindowChromePolicy.DwmWaBorderColor,
                ref borderColor,
                sizeof(uint));

            dwmApplied = hrCorner == 0 && hrBorder == 0;
        }

        var layout = UwpWindowChromePolicy.ResolveChromeLayout(
            osBuild,
            dwmAttributesSucceeded: dwmApplied,
            isDarkTheme: isDarkTheme,
            isWindowActive: _isWindowActive);

        _chromeBorderGrid.CornerRadius = new CornerRadius(layout.RootCornerRadius);
        _chromeBorderGrid.BorderThickness = new Thickness(layout.RootBorderThickness);
        _chromeBorderGrid.BorderBrush = layout.RootBorderThickness > 0
            ? new SolidColorBrush(ColorFromColorRef(layout.BorderColorRef))
            : null;

        _titleBar.SetActiveState(layout.IsTitleActive);
    }

    private void ApplyNormalizedFrameStyles()
    {
        const int gwlStyle = -16;
        const int gwlExStyle = -20;
        const uint swpNoSize = 0x0001;
        const uint swpNoMove = 0x0002;
        const uint swpNoZOrder = 0x0004;
        const uint swpNoActivate = 0x0010;
        const uint swpFrameChanged = 0x0020;

        long currentStyle = GetWindowLongPtrW(_hwnd, gwlStyle).ToInt64();
        long currentExStyle = GetWindowLongPtrW(_hwnd, gwlExStyle).ToInt64();
        var (normalizedStyle, normalizedExStyle) = UwpWindowChromePolicy.StripStandardWindowFrameStyles(
            currentStyle,
            currentExStyle,
            preserveResizeFrame: _isResizable);

        bool changed = false;
        if (normalizedStyle != currentStyle)
        {
            _ = SetWindowLongPtrW(_hwnd, gwlStyle, new IntPtr(normalizedStyle));
            changed = true;
        }

        if (normalizedExStyle != currentExStyle)
        {
            _ = SetWindowLongPtrW(_hwnd, gwlExStyle, new IntPtr(normalizedExStyle));
            changed = true;
        }

        if (changed)
        {
            _ = SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                swpNoMove | swpNoSize | swpNoZOrder | swpNoActivate | swpFrameChanged);
        }
    }

    private static Color ColorFromColorRef(uint colorRef)
        => Color.FromArgb(
            255,
            (byte)(colorRef & 0xFF),
            (byte)((colorRef >> 8) & 0xFF),
            (byte)((colorRef >> 16) & 0xFF));

    private sealed class ResizeGripElement : Grid
    {
        public ResizeGripElement(int hitTestCode, InputSystemCursorShape cursorShape)
        {
            HitTestCode = hitTestCode;
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            ProtectedCursor = InputSystemCursor.Create(cursorShape);
        }

        public int HitTestCode { get; }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr SubclassProcDelegate(
        IntPtr hWnd,
        uint uMsg,
        IntPtr wParam,
        IntPtr lParam,
        nuint uIdSubclass,
        IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, uint cbAttribute);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProcDelegate pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
