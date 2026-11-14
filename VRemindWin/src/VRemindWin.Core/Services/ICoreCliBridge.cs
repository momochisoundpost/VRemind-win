using VRemindWin.Core.Models;

namespace VRemindWin.Core.Services;

public interface ICoreCliBridge
{
    Task<CliCommandResponse> GetStateAsync(CancellationToken ct = default);
    Task<CliCommandResponse> RefreshAsync(CancellationToken ct = default);
    Task<CliCommandResponse> AddChannelAsync(string platform, string input, bool ignoreSecret = false, CancellationToken ct = default);
    Task<CliCommandResponse> RemoveChannelAsync(string channelId, CancellationToken ct = default);
    Task<CliCommandResponse> UpdateSettingsAsync(AppSettingsDto settings, CancellationToken ct = default);
    Task<CliCommandResponse> CompleteOnboardingAsync(string platform, string input, string updateConsent, CancellationToken ct = default);
    Task<CliCommandResponse> ResetFactoryDefaultsAsync(CancellationToken ct = default);
    Task<CliCommandResponse> ExportBackupAsync(string filePath, CancellationToken ct = default);
    Task<CliCommandResponse> ValidateBackupAsync(string filePath, CancellationToken ct = default);
    Task<CliCommandResponse> RestoreBackupAsync(string filePath, CancellationToken ct = default);
    Task<CliCommandResponse> CheckForUpdatesAsync(bool manual = false, CancellationToken ct = default);
}

public interface IToastNotificationDispatcher
{
    void Dispatch(CliNotificationItem item);
}
