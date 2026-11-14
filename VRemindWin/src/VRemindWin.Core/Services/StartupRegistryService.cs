using Microsoft.Win32;

namespace VRemindWin.Core.Services;

public sealed class StartupRegistryService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "VRemind";

    public bool IsLaunchAtLoginEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var val = key?.GetValue(AppValueName) as string;
        return !string.IsNullOrWhiteSpace(val);
    }

    public void SetLaunchAtLogin(bool enabled, string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            return;
        }

        if (enabled)
        {
            key.SetValue(AppValueName, $"\"{exePath}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppValueName, throwOnMissingValue: false);
        }
    }
}
