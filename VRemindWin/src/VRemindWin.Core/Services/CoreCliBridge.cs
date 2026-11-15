using System.Diagnostics;
using System.Text;
using System.Text.Json;
using VRemindWin.Core.Models;

namespace VRemindWin.Core.Services;

public sealed class CoreCliBridge : ICoreCliBridge
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _cliExePath;
    private readonly string? _dataDir;
    private readonly SemaphoreSlim _orderWriteLock = new(1, 1);

    public CoreCliBridge(string cliExePath, string? dataDir = null)
    {
        if (string.IsNullOrWhiteSpace(cliExePath))
        {
            throw new ArgumentException("CLI executable path must not be empty.", nameof(cliExePath));
        }

        _cliExePath = cliExePath;
        _dataDir = dataDir;
    }

    public Task<CliCommandResponse> GetStateAsync(CancellationToken ct = default)
        => ExecuteAsync(["state"], ct);

    public Task<CliCommandResponse> RefreshAsync(CancellationToken ct = default)
        => ExecuteAsync(["refresh"], ct);

    public Task<CliCommandResponse> AddChannelAsync(
        string platform,
        string input,
        bool ignoreSecret = false,
        CancellationToken ct = default)
    {
        List<string> args = ["add", "--platform", platform, "--input", input];
        if (ignoreSecret)
        {
            args.Add("--ignore-secret");
        }
        return ExecuteAsync(args, ct);
    }

    public Task<CliCommandResponse> RemoveChannelAsync(string channelId, CancellationToken ct = default)
        => ExecuteAsync(["remove", "--id", channelId], ct);

    public Task<CliCommandResponse> UpdateSettingsAsync(AppSettingsDto settings, CancellationToken ct = default)
        => ExecuteAsync([
            "settings",
            "--notify-on-live", settings.NotifyOnLive ? "true" : "false",
            "--notify-on-upcoming", settings.NotifyOnUpcoming ? "true" : "false",
            "--play-sound", settings.PlayNotificationSound ? "true" : "false",
            "--launch-at-login", settings.LaunchAtLogin ? "true" : "false",
            "--reduce-motion", settings.ReduceMotion ? "true" : "false",
            "--has-completed-onboarding", settings.HasCompletedOnboarding ? "true" : "false",
            "--update-consent", string.IsNullOrWhiteSpace(settings.UpdateConsent) ? "undecided" : settings.UpdateConsent,
            "--ignored-update-tag", string.IsNullOrWhiteSpace(settings.IgnoredUpdateTag) ? "none" : settings.IgnoredUpdateTag
        ], ct);

    public Task<CliCommandResponse> CompleteOnboardingAsync(
        string platform,
        string input,
        string updateConsent,
        CancellationToken ct = default)
        => ExecuteAsync([
            "complete-onboarding",
            "--platform", platform,
            "--input", input,
            "--update-consent", string.IsNullOrWhiteSpace(updateConsent) ? "allowed" : updateConsent
        ], ct);

    public Task<CliCommandResponse> ResetFactoryDefaultsAsync(CancellationToken ct = default)
        => ExecuteAsync(["reset"], ct);

    public Task<CliCommandResponse> ExportBackupAsync(string filePath, CancellationToken ct = default)
        => ExecuteAsync(["export-backup", "--file", filePath], ct);

    public Task<CliCommandResponse> ValidateBackupAsync(string filePath, CancellationToken ct = default)
        => ExecuteAsync(["validate-backup", "--file", filePath], ct);

    public Task<CliCommandResponse> RestoreBackupAsync(string filePath, CancellationToken ct = default)
        => ExecuteAsync(["restore-backup", "--file", filePath], ct);

    public Task<CliCommandResponse> CheckForUpdatesAsync(bool manual = false, CancellationToken ct = default)
    {
        List<string> args = ["check-update"];
        if (manual)
        {
            args.Add("--manual");
        }
        return ExecuteAsync(args, ct);
    }

    public async Task ReorderChannelsAsync(IReadOnlyList<string> orderedSectionIds, CancellationToken ct = default)
    {
        string targetDataDir = ChannelOrderPolicy.ResolveDefaultDataDirectory(_dataDir);
        await _orderWriteLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await Task.Run(
                () => ChannelOrderPolicy.PersistSectionOrder(targetDataDir, orderedSectionIds),
                ct).ConfigureAwait(false);
        }
        finally
        {
            _orderWriteLock.Release();
        }
    }

    private async Task<CliCommandResponse> ExecuteAsync(IEnumerable<string> arguments, CancellationToken ct)
    {
        if (!File.Exists(_cliExePath))
        {
            throw new FileNotFoundException($"Missing VRemindCore CLI binary at '{_cliExePath}'.", _cliExePath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _cliExePath,
            WorkingDirectory = Path.GetDirectoryName(_cliExePath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(_dataDir))
        {
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(_dataDir);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(45));
        var effectiveCt = timeoutCts.Token;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start '{_cliExePath}'.");

        using var cancelReg = effectiveCt.Register(static state =>
        {
            if (state is Process p)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore race if process already exited
                }
            }
        }, process);

        var stdoutTask = process.StandardOutput.ReadToEndAsync(effectiveCt);
        var stderrTask = process.StandardError.ReadToEndAsync(effectiveCt);

        await process.WaitForExitAsync(effectiveCt).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"vremind-core.exe exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        var parsed = JsonSerializer.Deserialize<CliCommandResponse>(stdout, JsonOptions);
        return parsed ?? throw new InvalidDataException("vremind-core.exe returned empty JSON output.");
    }
}
