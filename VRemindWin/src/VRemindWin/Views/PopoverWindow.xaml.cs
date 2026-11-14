using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using VRemindWin.Core.Services;
using VRemindWin.Core.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace VRemindWin.Views;

public sealed partial class PopoverWindow : Window
{
    private const uint WmEraseBkgnd = 0x0014;
    private const uint WmNcDestroy = 0x0082;
    private const uint WmNcCalcSize = 0x0083;
    private const nuint ChromeSubclassId = 1;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly PopoverView _popoverView;
    private readonly SubclassProcDelegate _subclassProc;
    private bool _isSubclassAttached;
    private bool _isModalDialogOpen;
    private bool _allowClose;
    private DateTimeOffset? _lastAutoDeactivatedUtc;

    public PopoverWindow(
        PopoverViewModel viewModel,
        Action onOpenSettings,
        Action onQuit,
        Action<Uri> onOpenUri)
    {
        InitializeComponent();

        _popoverView = new PopoverView(
            viewModel: viewModel,
            onOpenSettings: onOpenSettings,
            onQuit: onQuit,
            onOpenUri: onOpenUri,
            onRequestDismiss: HideFlyout,
            onModalStateChanged: isOpen => _isModalDialogOpen = isOpen);
        HostContainer.Children.Add(_popoverView);

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "VRemind";

        try
        {
            _appWindow.IsShownInSwitchers = false;
        }
        catch (NotImplementedException)
        {
            // Expected on Windows 10 (1809-22H2); WS_EX_TOOLWINDOW handles this natively.
        }

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            // IsResizable must be false before SetBorderAndTitleBar(false, false) on Windows 10/11.
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }

        _subclassProc = ChromeSubclassProc;
        AttachWindowSubclass();
        ApplyBorderlessChrome();

        _popoverView.ActualThemeChanged += (_, _) => ApplyDwmChromeForCurrentTheme();
        _appWindow.Closing += AppWindow_Closing;
        Activated += PopoverWindow_Activated;

        _appWindow.Hide();
    }

    public bool IsFlyoutVisible => _appWindow.IsVisible;

    public bool ConsumeRecentDeactivation()
    {
        var shouldSuppress = TrayFlyoutPositioner.ShouldSuppressTrayReopen(_lastAutoDeactivatedUtc, DateTimeOffset.UtcNow);
        _lastAutoDeactivatedUtc = null;
        return shouldSuppress;
    }

    public void ShowAt(int x, int y, int width, int height)
    {
        _lastAutoDeactivatedUtc = null;
        ApplyBorderlessStyles();
        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        _appWindow.Show(activateWindow: true);
        ApplyBorderlessStyles(ensureTopMost: true);
        _popoverView.SetPopoverVisible(true);
        Activate();
        _ = SetForegroundWindow(_hwnd);
        _popoverView.FocusInput();
    }

    public void HideFlyout()
    {
        if (!_isModalDialogOpen && _appWindow.IsVisible)
        {
            _popoverView.SetPopoverVisible(false);
            _appWindow.Hide();
        }
    }

    public void ForceClose()
    {
        _popoverView.SetPopoverVisible(false);
        DetachWindowSubclass();
        _allowClose = true;
        Close();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_allowClose)
        {
            args.Cancel = true;
            HideFlyout();
        }
    }

    private void PopoverWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && !_isModalDialogOpen)
        {
            if (_appWindow.IsVisible)
            {
                _lastAutoDeactivatedUtc = DateTimeOffset.UtcNow;
                HideFlyout();
            }
        }
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
            // Eliminate any residual Win32 non-client border/titlebar inset (e.g. WS_DLGFRAME / WS_EX_WINDOWEDGE).
            return IntPtr.Zero;
        }

        if (uMsg == WmEraseBkgnd)
        {
            return new IntPtr(1);
        }

        if (uMsg == WmNcDestroy)
        {
            DetachWindowSubclass();
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void ApplyBorderlessChrome()
    {
        ApplyBorderlessStyles();
        ApplyDwmChromeForCurrentTheme();
    }

    private void ApplyDwmChromeForCurrentTheme()
    {
        int osBuild = Environment.OSVersion.Version.Build;
        bool isDarkTheme = _popoverView.ActualTheme != ElementTheme.Light;
        bool dwmApplied = false;

        if (PopoverWindowChromePolicy.IsWindows11OrGreater(osBuild))
        {
            // Keep DWM drop shadow alive on borderless window without altering client geometry.
            var shadowMargins = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 1 };
            _ = DwmExtendFrameIntoClientArea(_hwnd, ref shadowMargins);

            var layoutCandidate = PopoverWindowChromePolicy.ResolveChromeLayout(osBuild, dwmAttributesSucceeded: true, isDarkTheme: isDarkTheme);
            uint cornerPref = layoutCandidate.CornerPreferenceValue;
            int hrCorner = DwmSetWindowAttribute(
                _hwnd,
                PopoverWindowChromePolicy.DwmWaWindowCornerPreference,
                ref cornerPref,
                sizeof(uint));

            uint borderColor = layoutCandidate.BorderColorRef;
            int hrBorder = DwmSetWindowAttribute(
                _hwnd,
                PopoverWindowChromePolicy.DwmWaBorderColor,
                ref borderColor,
                sizeof(uint));

            dwmApplied = hrCorner == 0 && hrBorder == 0;
        }

        var chromeLayout = PopoverWindowChromePolicy.ResolveChromeLayout(osBuild, dwmApplied, isDarkTheme);
        _popoverView.ApplyChromeAppearance(chromeLayout.RootCornerRadius, chromeLayout.RootBorderThickness);
    }

    private void ApplyBorderlessStyles(bool ensureTopMost = false)
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
        var (borderlessStyle, borderlessExStyle) = PopoverWindowChromePolicy.StripFrameStyles(currentStyle, currentExStyle);

        if (borderlessStyle != currentStyle)
        {
            _ = SetWindowLongPtrW(_hwnd, gwlStyle, new IntPtr(borderlessStyle));
        }

        if (borderlessExStyle != currentExStyle)
        {
            _ = SetWindowLongPtrW(_hwnd, gwlExStyle, new IntPtr(borderlessExStyle));
        }

        IntPtr insertAfter = ensureTopMost ? new IntPtr(-1) : IntPtr.Zero;
        uint flags = swpNoMove | swpNoSize | swpNoActivate | swpFrameChanged | (ensureTopMost ? 0u : swpNoZOrder);
        _ = SetWindowPos(_hwnd, insertAfter, 0, 0, 0, 0, flags);
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

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

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
