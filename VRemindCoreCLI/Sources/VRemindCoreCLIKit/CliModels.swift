import Foundation
import VRemindCore

public struct CliChannelItem: Codable, Equatable, Sendable {
    public let id: String
    public let platform: String
    public let platformDisplayName: String
    public let name: String
    public let handle: String
    public let formattedHandle: String
    public let avatarURL: String?
    public let statusKind: String
    public let streamTitle: String?
    public let timeLabel: String
    public let watchURL: String
    public let isCelebration: Bool
    public let celebrationType: String?

    public init(
        id: String,
        platform: String,
        platformDisplayName: String,
        name: String,
        handle: String,
        formattedHandle: String,
        avatarURL: String?,
        statusKind: String,
        streamTitle: String?,
        timeLabel: String,
        watchURL: String,
        isCelebration: Bool = false,
        celebrationType: String? = nil
    ) {
        self.id = id
        self.platform = platform
        self.platformDisplayName = platformDisplayName
        self.name = name
        self.handle = handle
        self.formattedHandle = formattedHandle
        self.avatarURL = avatarURL
        self.statusKind = statusKind
        self.streamTitle = streamTitle
        self.timeLabel = timeLabel
        self.watchURL = watchURL
        self.isCelebration = isCelebration
        self.celebrationType = celebrationType
    }
}

public struct CliSections: Codable, Equatable, Sendable {
    public let live: [CliChannelItem]
    public let upcoming: [CliChannelItem]
    public let offline: [CliChannelItem]

    public init(live: [CliChannelItem], upcoming: [CliChannelItem], offline: [CliChannelItem]) {
        self.live = live
        self.upcoming = upcoming
        self.offline = offline
    }
}

public struct CliNotificationItem: Codable, Equatable, Sendable {
    public let kind: String
    public let channelName: String?
    public let title: String
    public let body: String
    public let targetURL: String
    public let playSound: Bool

    public init(
        kind: String,
        channelName: String?,
        title: String,
        body: String,
        targetURL: String,
        playSound: Bool
    ) {
        self.kind = kind
        self.channelName = channelName
        self.title = title
        self.body = body
        self.targetURL = targetURL
        self.playSound = playSound
    }
}

public struct SecretDialogPayload: Codable, Equatable, Sendable {
    public let title: String
    public let message: String
    public let abort: String
    public let cancel: String
    public let ignore: String

    public init(title: String, message: String, abort: String, cancel: String, ignore: String) {
        self.title = title
        self.message = message
        self.abort = abort
        self.cancel = cancel
        self.ignore = ignore
    }
}

public struct CliReleaseDto: Codable, Equatable, Sendable {
    public let tagName: String
    public let name: String?
    public let body: String?
    public let htmlURL: String
    public let downloadURL: String

    public init(
        tagName: String,
        name: String?,
        body: String?,
        htmlURL: String,
        downloadURL: String
    ) {
        self.tagName = tagName
        self.name = name
        self.body = body
        self.htmlURL = htmlURL
        self.downloadURL = downloadURL
    }
}

public struct CliUpdateCheckResult: Codable, Equatable, Sendable {
    public let status: String
    public let latestVersion: String?
    public let remoteVersion: String?
    public let errorMessage: String?
    public let httpStatusCode: Int?
    public let release: CliReleaseDto?

    public init(
        status: String,
        latestVersion: String? = nil,
        remoteVersion: String? = nil,
        errorMessage: String? = nil,
        httpStatusCode: Int? = nil,
        release: CliReleaseDto? = nil
    ) {
        self.status = status
        self.latestVersion = latestVersion
        self.remoteVersion = remoteVersion
        self.errorMessage = errorMessage
        self.httpStatusCode = httpStatusCode
        self.release = release
    }
}

public struct CliStateEnvelope: Codable, Equatable, Sendable {
    public let liveCount: Int
    public let isPopoverCelebrating: Bool
    public let appVersion: String
    public let sections: CliSections
    public let settings: AppSettings
    public let notifications: [CliNotificationItem]

    public init(
        liveCount: Int,
        isPopoverCelebrating: Bool = false,
        appVersion: String = BackupPayload.defaultAppVersion,
        sections: CliSections,
        settings: AppSettings,
        notifications: [CliNotificationItem]
    ) {
        self.liveCount = liveCount
        self.isPopoverCelebrating = isPopoverCelebrating
        self.appVersion = appVersion
        self.sections = sections
        self.settings = settings
        self.notifications = notifications
    }
}

public struct CliCommandResponse: Codable, Equatable, Sendable {
    public let outcome: String
    public let errorMessage: String?
    public let secretDialog: SecretDialogPayload?
    public let state: CliStateEnvelope?
    public let updateResult: CliUpdateCheckResult?
    public let backupChannelCount: Int?

    public init(
        outcome: String,
        errorMessage: String? = nil,
        secretDialog: SecretDialogPayload? = nil,
        state: CliStateEnvelope? = nil,
        updateResult: CliUpdateCheckResult? = nil,
        backupChannelCount: Int? = nil
    ) {
        self.outcome = outcome
        self.errorMessage = errorMessage
        self.secretDialog = secretDialog
        self.state = state
        self.updateResult = updateResult
        self.backupChannelCount = backupChannelCount
    }
}
