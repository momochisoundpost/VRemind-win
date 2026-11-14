using System.Text.Json.Serialization;

namespace VRemindWin.Core.Models;

public sealed record CliChannelItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("platformDisplayName")] string PlatformDisplayName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string Handle,
    [property: JsonPropertyName("formattedHandle")] string FormattedHandle,
    [property: JsonPropertyName("avatarURL")] string? AvatarURL,
    [property: JsonPropertyName("statusKind")] string StatusKind,
    [property: JsonPropertyName("streamTitle")] string? StreamTitle,
    [property: JsonPropertyName("timeLabel")] string TimeLabel,
    [property: JsonPropertyName("watchURL")] string WatchURL,
    [property: JsonPropertyName("isCelebration")] bool IsCelebration = false,
    [property: JsonPropertyName("celebrationType")] string? CelebrationType = null
)
{
    public bool IsLive => string.Equals(StatusKind, "live", StringComparison.OrdinalIgnoreCase);
    public bool IsUpcoming => string.Equals(StatusKind, "upcoming", StringComparison.OrdinalIgnoreCase);
    public string EffectiveVisualKind => IsCelebration ? "celebration" : StatusKind;
    public bool ShowStatusBadge => IsLive || (IsUpcoming && IsCelebration);
    public string StatusBadgeText => IsLive ? "LIVE" : "SOON";
    public bool HasStreamTitle => !string.IsNullOrWhiteSpace(StreamTitle);
    public string TooltipText => $"{PlatformDisplayName}: {Handle}";
    public string PlatformBadgeHex => string.Equals(Platform, "twitch", StringComparison.OrdinalIgnoreCase)
        ? "#FF9146FF"
        : "#FFFF0000";
    public string InitialLetter => string.IsNullOrWhiteSpace(Name)
        ? "?"
        : Name.Trim()[0].ToString().ToUpperInvariant();
}

public sealed record CliSections(
    [property: JsonPropertyName("live")] IReadOnlyList<CliChannelItem> Live,
    [property: JsonPropertyName("upcoming")] IReadOnlyList<CliChannelItem> Upcoming,
    [property: JsonPropertyName("offline")] IReadOnlyList<CliChannelItem> Offline
)
{
    public static CliSections Empty { get; } = new([], [], []);
}

public sealed record CliNotificationItem(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("channelName")] string? ChannelName,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("targetURL")] string TargetURL,
    [property: JsonPropertyName("playSound")] bool PlaySound
);

public sealed record SecretDialogPayload(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("abort")] string Abort,
    [property: JsonPropertyName("cancel")] string Cancel,
    [property: JsonPropertyName("ignore")] string Ignore
);

public sealed record CliReleaseDto(
    [property: JsonPropertyName("tagName")] string TagName,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("htmlURL")] string HtmlURL,
    [property: JsonPropertyName("downloadURL")] string DownloadURL
);

public sealed record CliUpdateCheckResult(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("latestVersion")] string? LatestVersion = null,
    [property: JsonPropertyName("remoteVersion")] string? RemoteVersion = null,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage = null,
    [property: JsonPropertyName("httpStatusCode")] int? HttpStatusCode = null,
    [property: JsonPropertyName("release")] CliReleaseDto? Release = null
);

public sealed record AppSettingsDto(
    [property: JsonPropertyName("launchAtLogin")] bool LaunchAtLogin,
    [property: JsonPropertyName("notifyOnLive")] bool NotifyOnLive,
    [property: JsonPropertyName("notifyOnUpcoming")] bool NotifyOnUpcoming,
    [property: JsonPropertyName("playNotificationSound")] bool PlayNotificationSound,
    [property: JsonPropertyName("hasCompletedOnboarding")] bool HasCompletedOnboarding,
    [property: JsonPropertyName("updateConsent")] string UpdateConsent,
    [property: JsonPropertyName("lastUpdateCheckDate")] DateTimeOffset? LastUpdateCheckDate,
    [property: JsonPropertyName("ignoredUpdateTag")] string? IgnoredUpdateTag,
    [property: JsonPropertyName("reduceMotion")] bool ReduceMotion,
    [property: JsonPropertyName("lastNotifiedDevBirthdayYear")] int? LastNotifiedDevBirthdayYear
)
{
    public static AppSettingsDto Default { get; } = new(
        LaunchAtLogin: false,
        NotifyOnLive: true,
        NotifyOnUpcoming: false,
        PlayNotificationSound: true,
        HasCompletedOnboarding: false,
        UpdateConsent: "undecided",
        LastUpdateCheckDate: null,
        IgnoredUpdateTag: null,
        ReduceMotion: false,
        LastNotifiedDevBirthdayYear: null
    );
}

public sealed record CliStateEnvelope(
    [property: JsonPropertyName("liveCount")] int LiveCount,
    [property: JsonPropertyName("sections")] CliSections Sections,
    [property: JsonPropertyName("settings")] AppSettingsDto Settings,
    [property: JsonPropertyName("notifications")] IReadOnlyList<CliNotificationItem> Notifications,
    [property: JsonPropertyName("isPopoverCelebrating")] bool IsPopoverCelebrating = false,
    [property: JsonPropertyName("appVersion")] string AppVersion = "1.3.2"
)
{
    public static CliStateEnvelope Empty { get; } = new(
        LiveCount: 0,
        Sections: CliSections.Empty,
        Settings: AppSettingsDto.Default,
        Notifications: [],
        IsPopoverCelebrating: false,
        AppVersion: "1.3.2"
    );
}

public sealed record CliCommandResponse(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("secretDialog")] SecretDialogPayload? SecretDialog,
    [property: JsonPropertyName("state")] CliStateEnvelope? State,
    [property: JsonPropertyName("updateResult")] CliUpdateCheckResult? UpdateResult = null,
    [property: JsonPropertyName("backupChannelCount")] int? BackupChannelCount = null
);
