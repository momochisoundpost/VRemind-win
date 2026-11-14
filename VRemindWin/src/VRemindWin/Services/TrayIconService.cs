using System.ComponentModel;
using System.Runtime.InteropServices;
using VRemindWin.Core.Services;

namespace VRemindWin.Services;

/// <summary>
/// Manages the Win32 notification area (system tray) icon, tooltip, left-click flyout anchor,
/// and right-click context menu without relying on Windows Forms.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private const uint WM_USER = 0x0400;
    private const uint WM_TRAYICON = WM_USER + 1;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint NIN_SELECT = 0x0400;
    private const uint NIN_KEYSELECT = 0x0401;
    private const uint WM_NULL = 0x0000;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_SEPARATOR = 0x00000800;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_NONOTIFY = 0x0080;

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    private const int MenuOpenId = 1001;
    private const int MenuRefreshId = 1002;
    private const int MenuSettingsId = 1003;
    private const int MenuQuitId = 1004;
    private const uint TrayIconId = 1;

    private readonly WndProcDelegate _wndProc;
    private readonly Action _onTogglePopover;
    private readonly Action _onRefresh;
    private readonly Action _onOpenSettings;
    private readonly Action _onQuit;
    private readonly string _className;
    private readonly uint _taskbarCreatedMsg;

    private IntPtr _hwnd;
    private IntPtr _normalIcon;
    private IntPtr _liveIcon;
    private bool _isLive;
    private bool _disposed;
    private bool _iconAdded;

    public TrayIconService(
        string normalIconPath,
        string liveIconPath,
        Action onTogglePopover,
        Action onRefresh,
        Action onOpenSettings,
        Action onQuit)
    {
        _onTogglePopover = onTogglePopover ?? throw new ArgumentNullException(nameof(onTogglePopover));
        _onRefresh = onRefresh ?? throw new ArgumentNullException(nameof(onRefresh));
        _onOpenSettings = onOpenSettings ?? throw new ArgumentNullException(nameof(onOpenSettings));
        _onQuit = onQuit ?? throw new ArgumentNullException(nameof(onQuit));

        _wndProc = WindowProc;
        _className = $"VRemindWinTrayWindow_{Environment.ProcessId}";
        _taskbarCreatedMsg = RegisterWindowMessageW("TaskbarCreated");

        int cx = Math.Max(16, GetSystemMetrics(SM_CXSMICON));
        int cy = Math.Max(16, GetSystemMetrics(SM_CYSMICON));
        _normalIcon = LoadImageW(IntPtr.Zero, normalIconPath, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
        _liveIcon = LoadImageW(IntPtr.Zero, liveIconPath, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);

        CreateMessageWindow();
        AddOrUpdateIcon(isAdd: true);
    }

    public void SetLiveState(bool isLive)
    {
        if (_disposed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _isLive = isLive;
        AddOrUpdateIcon(isAdd: !_iconAdded);
    }

    public (int X, int Y, int Width, int Height) GetFlyoutBounds(int logicalWidth = 360, int logicalHeight = 440)
    {
        var id = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            guidItem = Guid.Empty
        };

        RECT anchorRect;
        IntPtr hMonitor;
        if (Shell_NotifyIconGetRect(ref id, out anchorRect) == 0)
        {
            hMonitor = MonitorFromRect(ref anchorRect, MONITOR_DEFAULTTONEAREST);
        }
        else
        {
            GetCursorPos(out var cursor);
            anchorRect = new RECT { Left = cursor.X, Top = cursor.Y, Right = cursor.X + 1, Bottom = cursor.Y + 1 };
            hMonitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
        }

        var monitorInfo = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        var workArea = GetMonitorInfoW(hMonitor, ref monitorInfo)
            ? (monitorInfo.rcWork.Left, monitorInfo.rcWork.Top, monitorInfo.rcWork.Right, monitorInfo.rcWork.Bottom)
            : (0, 0, 1920, 1040);

        uint dpi = 96;
        if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
        {
            dpi = dpiX;
        }

        return TrayFlyoutPositioner.CalculateFlyoutBounds(
            iconRect: (anchorRect.Left, anchorRect.Top, anchorRect.Right, anchorRect.Bottom),
            workArea: workArea,
            dpi: dpi,
            logicalWidth: logicalWidth,
            logicalHeight: logicalHeight);
    }

    private void CreateMessageWindow()
    {
        var hInstance = GetModuleHandleW(null);
        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = hInstance,
            lpszClassName = _className
        };

        if (RegisterClassExW(ref wndClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to register tray message window class.");
        }

        // Top-level zero-size hidden window (not HWND_MESSAGE) so we still receive TaskbarCreated broadcasts.
        _hwnd = CreateWindowExW(
            0,
            _className,
            "VRemindTrayMessageWindow",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create tray message window.");
        }
    }

    private void AddOrUpdateIcon(bool isAdd)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hwnd,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _isLive && _liveIcon != IntPtr.Zero ? _liveIcon : _normalIcon,
            szTip = _isLive ? "VRemind (Live now)" : "VRemind",
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };

        bool ok = Shell_NotifyIconW(isAdd ? NIM_ADD : NIM_MODIFY, ref nid);
        if (!ok && !isAdd)
        {
            ok = Shell_NotifyIconW(NIM_ADD, ref nid);
        }

        if (ok)
        {
            _iconAdded = true;
        }
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            uint mouseMsg = unchecked((uint)(lParam.ToInt64() & 0xFFFF));
            if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK or NIN_SELECT or NIN_KEYSELECT)
            {
                _onTogglePopover();
                return IntPtr.Zero;
            }

            if (mouseMsg is WM_RBUTTONUP or WM_CONTEXTMENU)
            {
                ShowContextMenu();
                return IntPtr.Zero;
            }
        }
        else if (_taskbarCreatedMsg != 0 && msg == _taskbarCreatedMsg)
        {
            _iconAdded = false;
            AddOrUpdateIcon(isAdd: true);
            return IntPtr.Zero;
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        IntPtr menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendMenuW(menu, MF_STRING, (UIntPtr)MenuOpenId, "Open VRemind");
            AppendMenuW(menu, MF_STRING, (UIntPtr)MenuRefreshId, "Refresh Now");
            AppendMenuW(menu, MF_STRING, (UIntPtr)MenuSettingsId, "Settings...");
            AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenuW(menu, MF_STRING, (UIntPtr)MenuQuitId, "Quit VRemind");

            GetCursorPos(out var cursor);
            SetForegroundWindow(_hwnd);
            int selected = TrackPopupMenuEx(
                menu,
                TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_NONOTIFY,
                cursor.X,
                cursor.Y,
                _hwnd,
                IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (selected)
            {
                case MenuOpenId:
                    _onTogglePopover();
                    break;
                case MenuRefreshId:
                    _onRefresh();
                    break;
                case MenuSettingsId:
                    _onOpenSettings();
                    break;
                case MenuQuitId:
                    _onQuit();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            if (_iconAdded)
            {
                var nid = new NOTIFYICONDATAW
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                    hWnd = _hwnd,
                    uID = TrayIconId,
                    szTip = string.Empty,
                    szInfo = string.Empty,
                    szInfoTitle = string.Empty
                };
                _ = Shell_NotifyIconW(NIM_DELETE, ref nid);
                _iconAdded = false;
            }

            _ = DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            _ = UnregisterClassW(_className, GetModuleHandleW(null));
        }

        if (_normalIcon != IntPtr.Zero)
        {
            _ = DestroyIcon(_normalIcon);
            _normalIcon = IntPtr.Zero;
        }

        if (_liveIcon != IntPtr.Zero)
        {
            _ = DestroyIcon(_liveIcon);
            _liveIcon = IntPtr.Zero;
        }
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
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
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hwnd,
        IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
