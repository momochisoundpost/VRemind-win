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
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly PopoverView _popoverView;
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

        // AppWindow.IsShownInSwitchers throws NotImplementedException on Windows 10; apply WS_EX_TOOLWINDOW natively.
        HideFromTaskbarAndSwitcher(_hwnd);
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
        _appWindow.MoveAndResize(new RectInt32(x, y, width, height));
        _appWindow.Show(activateWindow: true);
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

    private static void HideFromTaskbarAndSwitcher(IntPtr hwnd)
    {
        const int gwlExStyle = -20;
        const long wsExToolWindow = 0x00000080L;
        const long wsExAppWindow = 0x00040000L;
        const uint swpNoSize = 0x0001;
        const uint swpNoMove = 0x0002;
        const uint swpNoZOrder = 0x0004;
        const uint swpNoActivate = 0x0010;
        const uint swpFrameChanged = 0x0020;

        long exStyle = GetWindowLongPtrW(hwnd, gwlExStyle).ToInt64();
        exStyle = (exStyle | wsExToolWindow) & ~wsExAppWindow;
        _ = SetWindowLongPtrW(hwnd, gwlExStyle, new IntPtr(exStyle));
        _ = SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, swpNoMove | swpNoSize | swpNoZOrder | swpNoActivate | swpFrameChanged);
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
}
