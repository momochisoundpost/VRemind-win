using System.Runtime.InteropServices;

namespace VRemindWin.Services;

/// <summary>
/// Provides Windows 10 1809 LTSC-compatible native file open/save dialogs using <c>comdlg32.dll</c>
/// without relying on packaged WinRT picker brokers.
/// </summary>
public static class NativeFileDialogService
{
    private const int MaxFileBufferChars = 4096;
    private const int OfnOverwritePrompt = 0x00000002;
    private const int OfnNoChangeDir = 0x00000008;
    private const int OfnPathMustExist = 0x00000800;
    private const int OfnFileMustExist = 0x00001000;
    private const int OfnExplorer = 0x00080000;

    private const string JsonFilter = "JSON Files (*.json)\0*.json\0All Files (*.*)\0*.*\0\0";

    public static string? ShowSaveJsonDialog(IntPtr ownerHwnd, string defaultFileName)
    {
        IntPtr fileBuffer = Marshal.AllocHGlobal(MaxFileBufferChars * sizeof(char));
        try
        {
            var chars = new char[MaxFileBufferChars];
            if (!string.IsNullOrEmpty(defaultFileName))
            {
                int copyLen = Math.Min(defaultFileName.Length, MaxFileBufferChars - 1);
                defaultFileName.CopyTo(0, chars, 0, copyLen);
            }
            Marshal.Copy(chars, 0, fileBuffer, MaxFileBufferChars);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = JsonFilter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MaxFileBufferChars,
                lpstrTitle = "Export VRemind Backup",
                lpstrDefExt = "json",
                Flags = OfnExplorer | OfnPathMustExist | OfnOverwritePrompt | OfnNoChangeDir
            };

            return GetSaveFileNameW(ref ofn)
                ? Marshal.PtrToStringUni(fileBuffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
    }

    public static string? ShowOpenJsonDialog(IntPtr ownerHwnd)
    {
        IntPtr fileBuffer = Marshal.AllocHGlobal(MaxFileBufferChars * sizeof(char));
        try
        {
            var chars = new char[MaxFileBufferChars];
            Marshal.Copy(chars, 0, fileBuffer, MaxFileBufferChars);

            var ofn = new OPENFILENAMEW
            {
                lStructSize = Marshal.SizeOf<OPENFILENAMEW>(),
                hwndOwner = ownerHwnd,
                lpstrFilter = JsonFilter,
                nFilterIndex = 1,
                lpstrFile = fileBuffer,
                nMaxFile = MaxFileBufferChars,
                lpstrTitle = "Restore VRemind Backup",
                lpstrDefExt = "json",
                Flags = OfnExplorer | OfnPathMustExist | OfnFileMustExist | OfnNoChangeDir
            };

            return GetOpenFileNameW(ref ofn)
                ? Marshal.PtrToStringUni(fileBuffer)
                : null;
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAMEW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrInitialDir;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetSaveFileNameW(ref OPENFILENAMEW lpofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAMEW lpofn);
}
