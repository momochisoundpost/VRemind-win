using System.IO.Pipes;

namespace VRemindWin.Core.Services;

public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName = @"Local\MomochiSoundpost.VRemind.SingleInstance";
    private const string PipeName = "MomochiSoundpost.VRemind.ActivationPipe";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;

    public bool IsPrimaryInstance { get; private set; }

    public static bool IsToastActivatedArgs(string[] args)
        => args.Any(a => string.Equals(a, WindowsToastService.ToastActivatorArgument, StringComparison.OrdinalIgnoreCase));

    public bool TryAcquirePrimary()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        IsPrimaryInstance = createdNew;
        return createdNew;
    }

    public void StartActivationPipeServer(Action onShowRequested)
    {
        if (!IsPrimaryInstance)
        {
            return;
        }

        _pipeCts = new CancellationTokenSource();
        var ct = _pipeCts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var reader = new StreamReader(server);
                    var msg = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (string.Equals(msg, "SHOW", StringComparison.OrdinalIgnoreCase))
                    {
                        onShowRequested();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ignore transient pipe errors and continue listening
                }
            }
        }, ct);
    }

    public static void SignalPrimaryToShow()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 500);
            using var writer = new StreamWriter(client);
            writer.WriteLine("SHOW");
            writer.Flush();
        }
        catch
        {
            // Primary instance may be exiting
        }
    }

    public void Dispose()
    {
        _pipeCts?.Cancel();
        _pipeCts?.Dispose();
        _pipeCts = null;

        if (_mutex is not null)
        {
            if (IsPrimaryInstance)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore if already abandoned
                }
            }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
