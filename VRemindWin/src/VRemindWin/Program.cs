using Microsoft.UI.Dispatching;
using VRemindWin.Core.Services;

namespace VRemindWin;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        using var singleInstance = new SingleInstanceService();
        if (!singleInstance.TryAcquirePrimary())
        {
            if (!SingleInstanceService.IsToastActivatedArgs(args))
            {
                SingleInstanceService.SignalPrimaryToShow();
            }

            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();

        Microsoft.UI.Xaml.Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(singleInstance);
        });
    }

    internal static void WriteCrashLog(string source, Exception? ex, string? detail = null)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VRemind");
            Directory.CreateDirectory(dir);
            var text = $"[{DateTimeOffset.Now:O}] {source}\n{detail}\n{ex}\n\n";
            File.AppendAllText(Path.Combine(dir, "crash.log"), text);
        }
        catch
        {
            // Ignore logging errors
        }
    }
}
