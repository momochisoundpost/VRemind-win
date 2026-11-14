using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;
using VRemindWin.Core.Models;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace VRemindWin.Core.Services;

public sealed class WindowsToastService : IToastNotificationDispatcher, IDisposable
{
    public const string AppUserModelId = "MomochiSoundpost.VRemind";
    public const string ToastActivatorClsid = "D8B4E62A-5C39-4E8B-A193-7E4A6F90C112";
    public const string ToastActivatorArgument = "-ToastActivated";

    private uint? _comRegistrationCookie;
    private ToastActivatorClassFactory? _classFactory;
    private Action<Uri>? _onUrlActivated;
    private bool _registered;

    public static string BuildToastXml(CliNotificationItem item)
    {
        var launchArgs = $"action=openUrl&url={Uri.EscapeDataString(item.TargetURL)}";
        var escapedLaunch = SecurityElement.Escape(launchArgs) ?? string.Empty;
        var escapedTitle = SecurityElement.Escape(item.Title) ?? string.Empty;
        var escapedBody = SecurityElement.Escape(item.Body) ?? string.Empty;
        var audioNode = item.PlaySound ? string.Empty : "<audio silent=\"true\" />";

        return $"""
            <toast launch="{escapedLaunch}">
              <visual>
                <binding template="ToastGeneric">
                  <text>{escapedTitle}</text>
                  <text>{escapedBody}</text>
                </binding>
              </visual>
              {audioNode}
            </toast>
            """;
    }

    public void EnsureRegistered(string exePath, Action<Uri> onUrlActivated)
    {
        _onUrlActivated = onUrlActivated ?? throw new ArgumentNullException(nameof(onUrlActivated));

        if (!_registered)
        {
            _ = NativeToastMethods.SetCurrentProcessExplicitAppUserModelID(AppUserModelId);
            RegisterActivatorRegistry(exePath);
            EnsureStartMenuShortcut(exePath);
            RegisterComClassFactory();
            _registered = true;
        }
    }

    public void Dispatch(CliNotificationItem item)
    {
        if (!LinkSafetyValidator.TryValidateAllowedUri(item.TargetURL, out _))
        {
            return;
        }

        try
        {
            var xmlText = BuildToastXml(item);
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xmlText);

            var toast = new ToastNotification(xmlDoc);
            var notifier = ToastNotificationManager.CreateToastNotifier(AppUserModelId);
            notifier.Show(toast);
        }
        catch
        {
            // Ignore OS notification delivery errors (e.g., disabled by Group Policy or Shell indexing delay)
            // so background monitoring and UI state updates continue uninterrupted.
        }
    }

    internal void HandleActivatedArgs(string invokedArgs)
    {
        if (LinkSafetyValidator.TryExtractTargetUrlFromToastArgs(invokedArgs, out var uri) && uri is not null)
        {
            _onUrlActivated?.Invoke(uri);
        }
    }

    private static void RegisterActivatorRegistry(string exePath)
    {
        var subKeyPath = $@"Software\Classes\CLSID\{{{ToastActivatorClsid}}}\LocalServer32";
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        key?.SetValue(null, $"\"{exePath}\" {ToastActivatorArgument}", RegistryValueKind.String);
    }

    private static void EnsureStartMenuShortcut(string exePath)
    {
        var programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        if (string.IsNullOrWhiteSpace(programsDir))
        {
            return;
        }

        Directory.CreateDirectory(programsDir);
        var shortcutPath = Path.Combine(programsDir, "VRemind.lnk");

        var shellLink = (IShellLinkW)new CShellLink();
        try
        {
            shellLink.SetPath(exePath);
            shellLink.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory);
            shellLink.SetDescription("VRemind Stream Reminder");

            var propertyStore = (IPropertyStore)shellLink;
            var appIdKey = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
            var clsidKey = new PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 26);

            using var appIdVar = PropVariant.FromString(AppUserModelId);
            propertyStore.SetValue(ref appIdKey, ref appIdVar.Raw);

            using var clsidVar = PropVariant.FromGuid(Guid.Parse(ToastActivatorClsid));
            propertyStore.SetValue(ref clsidKey, ref clsidVar.Raw);

            propertyStore.Commit();

            var persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }
        finally
        {
            if (Marshal.IsComObject(shellLink))
            {
                Marshal.ReleaseComObject(shellLink);
            }
        }
    }

    private void RegisterComClassFactory()
    {
        if (_comRegistrationCookie.HasValue)
        {
            return;
        }

        var clsid = Guid.Parse(ToastActivatorClsid);
        _classFactory = new ToastActivatorClassFactory(this);
        var hr = NativeToastMethods.CoRegisterClassObject(
            ref clsid,
            _classFactory,
            NativeToastMethods.CLSCTX_LOCAL_SERVER,
            NativeToastMethods.REGCLS_MULTIPLEUSE,
            out var cookie);
        if (hr >= 0)
        {
            _comRegistrationCookie = cookie;
        }
    }

    public void Dispose()
    {
        if (_comRegistrationCookie is uint cookie)
        {
            _ = NativeToastMethods.CoRevokeClassObject(cookie);
            _comRegistrationCookie = null;
        }
    }

    // MARK: - COM Interop Definitions

    [ComImport]
    [Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface INotificationActivationCallback
    {
        void Activate(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string invokedArgs,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] NOTIFICATION_USER_INPUT_DATA[] data,
            uint dataCount);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFICATION_USER_INPUT_DATA
    {
        public IntPtr Key;
        public IntPtr Value;
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig]
        int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

        [PreserveSig]
        int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ToastActivatorCallback(WindowsToastService owner) : INotificationActivationCallback
    {
        public void Activate(
            string appUserModelId,
            string invokedArgs,
            NOTIFICATION_USER_INPUT_DATA[] data,
            uint dataCount)
        {
            owner.HandleActivatedArgs(invokedArgs);
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ToastActivatorClassFactory(WindowsToastService owner) : IClassFactory
    {
        private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");
        private static readonly Guid IidNotificationCallback = new("53E31837-6600-4A81-9395-75CFFE746F94");

        public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
        {
            ppvObject = IntPtr.Zero;
            if (pUnkOuter != IntPtr.Zero)
            {
                return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
            }
            if (riid != IidIUnknown && riid != IidNotificationCallback)
            {
                return unchecked((int)0x80004002); // E_NOINTERFACE
            }

            var callback = new ToastActivatorCallback(owner);
            ppvObject = Marshal.GetComInterfaceForObject<ToastActivatorCallback, INotificationActivationCallback>(callback);
            return 0;
        }

        public int LockServer(bool fLock) => 0;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PROPERTYKEY pkey);
        void GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        void SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY(Guid fmtid, uint pid)
    {
        public Guid fmtid = fmtid;
        public uint pid = pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr ptrVal;
    }

    private sealed class PropVariant : IDisposable
    {
        public PROPVARIANT Raw;

        public static PropVariant FromString(string value) => new()
        {
            Raw = new PROPVARIANT
            {
                vt = 31, // VT_LPWSTR
                ptrVal = Marshal.StringToCoTaskMemUni(value)
            }
        };

        public static PropVariant FromGuid(Guid guid)
        {
            var ptr = Marshal.AllocCoTaskMem(16);
            Marshal.Copy(guid.ToByteArray(), 0, ptr, 16);
            return new PropVariant
            {
                Raw = new PROPVARIANT
                {
                    vt = 72, // VT_CLSID
                    ptrVal = ptr
                }
            };
        }

        public void Dispose()
        {
            _ = NativeToastMethods.PropVariantClear(ref Raw);
        }
    }

    private static class NativeToastMethods
    {
        public const uint CLSCTX_LOCAL_SERVER = 4;
        public const uint REGCLS_MULTIPLEUSE = 1;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SetCurrentProcessExplicitAppUserModelID(string appID);

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int CoRegisterClassObject(
            ref Guid rclsid,
            [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
            uint dwClsContext,
            uint flags,
            out uint lpdwRegister);

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int CoRevokeClassObject(uint dwRegister);

        [DllImport("ole32.dll", PreserveSig = true)]
        public static extern int PropVariantClear(ref PROPVARIANT pvar);
    }
}
