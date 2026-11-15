using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using VRemindWin.Core.ViewModels;
using VRemindWin.Helpers;
using Windows.Graphics;
using WinRT.Interop;

namespace VRemindWin.Views;

public sealed partial class OnboardingWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly UwpWindowChromeController _chromeController;

    public OnboardingWindow(OnboardingViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "Welcome to VRemind";

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        var pngIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.png");
        if (File.Exists(pngIconPath))
        {
            AppIconImage.Source = new BitmapImage(new Uri(pngIconPath));
        }

        _chromeController = new UwpWindowChromeController(
            window: this,
            hwnd: _hwnd,
            appWindow: _appWindow,
            chromeBorderGrid: WindowChromeRoot,
            titleBar: WindowTitleBar,
            isResizable: false);

        uint dpi = GetDpiForWindow(_hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        _appWindow.Resize(new SizeInt32(
            (int)Math.Round(460 * scale),
            (int)Math.Round(520 * scale)));

        Closed += (_, _) =>
        {
            _chromeController.Dispose();
            Bindings.StopTracking();
        };
    }

    public OnboardingViewModel ViewModel { get; }

    public void ShowAndFocus()
    {
        _chromeController.EnsureChromeStyles();
        _appWindow.Show(activateWindow: true);
        _chromeController.EnsureChromeStyles();
        Activate();
        _ = SetForegroundWindow(_hwnd);
    }

    private void PlatformComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlatformComboBox.SelectedItem is ComboBoxItem item && item.Tag is string platform)
        {
            ViewModel.SelectedPlatform = platform;
        }
    }

    private void SendTestNotification_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SendTestNotification();
    }

    private async void GetStarted_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (await ViewModel.CompleteOnboardingAsync())
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("OnboardingWindow.GetStarted", ex);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
