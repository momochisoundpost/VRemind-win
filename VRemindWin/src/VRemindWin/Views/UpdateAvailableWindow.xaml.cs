using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using VRemindWin.Core.Models;
using VRemindWin.Core.Services;
using VRemindWin.Helpers;
using Windows.Graphics;
using WinRT.Interop;

namespace VRemindWin.Views;

public sealed partial class UpdateAvailableWindow : Window
{
    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly UwpWindowChromeController _chromeController;
    private readonly Action<Uri> _onOpenUri;
    private readonly Func<string, Task> _onSkipVersion;
    private CliReleaseDto _release;

    public UpdateAvailableWindow(
        CliReleaseDto release,
        string currentVersion,
        Action<Uri> onOpenUri,
        Func<string, Task> onSkipVersion)
    {
        _release = release ?? throw new ArgumentNullException(nameof(release));
        _onOpenUri = onOpenUri ?? throw new ArgumentNullException(nameof(onOpenUri));
        _onSkipVersion = onSkipVersion ?? throw new ArgumentNullException(nameof(onSkipVersion));

        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Title = "Software Update";

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            _appWindow.SetIcon(iconPath);
        }

        var pngIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app-icon.png");
        if (File.Exists(pngIconPath))
        {
            HeaderIconImage.Source = new BitmapImage(new Uri(pngIconPath));
        }

        _chromeController = new UwpWindowChromeController(
            window: this,
            hwnd: _hwnd,
            appWindow: _appWindow,
            chromeBorderGrid: WindowChromeRoot,
            titleBar: WindowTitleBar,
            isResizable: true);

        uint dpi = GetDpiForWindow(_hwnd);
        double scale = dpi == 0 ? 1.0 : dpi / 96.0;
        _appWindow.Resize(new SizeInt32(
            (int)Math.Round(480 * scale),
            (int)Math.Round(420 * scale)));

        UpdateRelease(release, currentVersion);

        Closed += (_, _) =>
        {
            _chromeController.Dispose();
            MarkdownViewer.CleanupMediaPlayers();
        };
    }

    public void UpdateRelease(CliReleaseDto release, string currentVersion)
    {
        _release = release;

        var chrome = UpdateWindowChromePolicy.Resolve(release);
        _appWindow.Title = chrome.WindowTitle;
        WindowTitleBar.TitleText = chrome.WindowTitle;
        TitleTextBlock.Text = chrome.HeaderTitle;
        SubtitleTextBlock.Text = $"You are currently running VRemind v{currentVersion}. Here's what's new:";
        SubtitleTextBlock.Visibility = chrome.ShowSubtitle ? Visibility.Visible : Visibility.Collapsed;
        SkipVersionButton.Visibility = chrome.ShowSkipButton ? Visibility.Visible : Visibility.Collapsed;
        DownloadUpdateButton.Visibility = chrome.ShowDownloadButton ? Visibility.Visible : Visibility.Collapsed;
        RemindLaterButton.Content = chrome.DismissButtonText;

        var body = string.IsNullOrWhiteSpace(release.Body)
            ? "_No release notes provided._"
            : release.Body;
        MarkdownViewer.SetMarkdown(body, _onOpenUri);
    }

    public void ShowAndFocus()
    {
        _chromeController.EnsureChromeStyles();
        _appWindow.Show(activateWindow: true);
        _chromeController.EnsureChromeStyles();
        Activate();
        _ = SetForegroundWindow(_hwnd);
    }

    private async void SkipVersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_release.IsReaderMode)
        {
            Close();
            return;
        }

        try
        {
            await _onSkipVersion(_release.TagName);
        }
        catch (Exception ex)
        {
            Program.WriteCrashLog("UpdateAvailableWindow.SkipVersion", ex);
        }
        finally
        {
            Close();
        }
    }

    private void RemindLaterButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_release.IsReaderMode)
        {
            Close();
            return;
        }

        var target = !string.IsNullOrWhiteSpace(_release.DownloadURL)
            ? _release.DownloadURL
            : _release.HtmlURL;

        if (LinkSafetyValidator.TryValidateAllowedUri(target, out var safeUri) && safeUri is not null)
        {
            _onOpenUri(safeUri);
        }
        Close();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
