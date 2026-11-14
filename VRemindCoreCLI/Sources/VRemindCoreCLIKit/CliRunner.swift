import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
#if os(Windows)
import WinSDK
#elseif canImport(Glibc)
import Glibc
#elseif canImport(Darwin)
import Darwin
#endif
import VRemindCore

public enum CliRunnerError: Error, LocalizedError {
    case missingCommand
    case unknownCommand(String)
    case missingOption(String)
    case invalidOptionValue(String, String)

    public var errorDescription: String? {
        switch self {
        case .missingCommand:
            return "Missing command. Expected one of: state, refresh, add, remove, settings, complete-onboarding, reset, export-backup, validate-backup, restore-backup, check-update."
        case .unknownCommand(let cmd):
            return "Unknown command '\(cmd)'."
        case .missingOption(let name):
            return "Missing required option '\(name)'."
        case .invalidOptionValue(let name, let value):
            return "Invalid value '\(value)' for option '\(name)'."
        }
    }
}

public final class CliRunner: Sendable {
    private static let devBirthdayMailto = "mailto:y2karyl@disroot.org?subject=Happy%20Birthday%20Caitlyn!"
    private static let defaultGithubReleasesLatestURL = "https://api.github.com/repos/momochisoundpost/VRemind-win/releases/latest"

    public init() {}

    public func execute(
        arguments: [String],
        fetcher: @escaping HTTPFetcher = StreamMonitorEngine.defaultFetcher,
        now: Date = Date(),
        calendar: Calendar = .current
    ) async throws -> CliCommandResponse {
        guard let command = arguments.first?.lowercased() else {
            throw CliRunnerError.missingCommand
        }
        let options = parseOptions(Array(arguments.dropFirst()))
        let dataDir = resolveDataDirectory(from: options)

        switch command {
        case "state":
            return try withDataDirLock(in: dataDir) {
                let (channels, settings, statuses) = try loadOrSeedState(in: dataDir)
                let envelope = buildEnvelope(
                    channels: channels,
                    settings: settings,
                    statuses: statuses,
                    notifications: [],
                    now: now,
                    calendar: calendar
                )
                return CliCommandResponse(outcome: "ok", state: envelope)
            }

        case "refresh":
            return try await runRefresh(
                in: dataDir,
                fetcher: fetcher,
                now: now,
                calendar: calendar
            )

        case "add":
            return try withDataDirLock(in: dataDir) {
                try runAdd(options: options, in: dataDir, now: now, calendar: calendar)
            }

        case "remove":
            return try withDataDirLock(in: dataDir) {
                try runRemove(options: options, in: dataDir, now: now, calendar: calendar)
            }

        case "settings":
            return try withDataDirLock(in: dataDir) {
                try runSettings(options: options, in: dataDir, now: now, calendar: calendar)
            }

        case "complete-onboarding":
            return try withDataDirLock(in: dataDir) {
                try runCompleteOnboarding(options: options, in: dataDir, now: now, calendar: calendar)
            }

        case "reset":
            return try withDataDirLock(in: dataDir) {
                try runReset(in: dataDir, now: now, calendar: calendar)
            }

        case "export-backup":
            return try withDataDirLock(in: dataDir) {
                try runExportBackup(options: options, in: dataDir, now: now, calendar: calendar)
            }

        case "validate-backup":
            return try withDataDirLock(in: dataDir) {
                try runValidateBackup(options: options, in: dataDir, now: now, calendar: calendar)
            }

        case "restore-backup":
            return try withDataDirLock(in: dataDir) {
                try runRestoreBackup(options: options, in: dataDir, now: now, calendar: calendar)
            }

        case "check-update":
            return try await runCheckUpdate(
                options: options,
                in: dataDir,
                fetcher: fetcher,
                now: now,
                calendar: calendar
            )

        default:
            throw CliRunnerError.unknownCommand(command)
        }
    }

    // MARK: - Subcommands

    private final class MonitorEventCollector: @unchecked Sendable {
        private let lock = NSLock()
        private(set) var events: [MonitorEvent] = []

        func append(_ event: MonitorEvent) {
            lock.lock()
            defer { lock.unlock() }
            events.append(event)
        }
    }

    private func runRefresh(
        in dataDir: URL,
        fetcher: @escaping HTTPFetcher,
        now: Date,
        calendar: Calendar
    ) async throws -> CliCommandResponse {
        let loaded = try withDataDirLock(in: dataDir) {
            try loadOrSeedState(in: dataDir)
        }
        var channels = loaded.channels
        let settings = loaded.settings
        let previousStatuses = loaded.statuses
        var updatedDevBirthdayYear: Int?

        let engine = StreamMonitorEngine()
        let collector = MonitorEventCollector()

        let updatedStatuses = await engine.refreshPass(
            channels: channels,
            settings: settings,
            currentStatuses: previousStatuses,
            fetcher: fetcher,
            date: now,
            calendar: calendar
        ) { event in
            collector.append(event)
        }

        var pendingNotifications: [(channelId: String?, item: CliNotificationItem)] = []
        for event in collector.events {
            switch event {
            case .metadataUpdated(let channelId, let name, let avatarURL):
                if let idx = Self.channelIndex(for: channelId, in: channels) {
                    if let trimmed = name?.trimmingCharacters(in: .whitespacesAndNewlines), !trimmed.isEmpty {
                        channels[idx].name = trimmed
                    }
                    if let avatarURL {
                        channels[idx].avatarURL = avatarURL
                    }
                }

            case .statusUpdated(let channelId, let status):
                if let idx = Self.channelIndex(for: channelId, in: channels) {
                    switch status {
                    case .live(_, _, let startedAt):
                        channels[idx].lastStreamedAt = startedAt ?? now
                    case .offline(let lastStreamed):
                        let wasLive: Bool
                        if case .live = previousStatuses[channels[idx].id] ?? previousStatuses[channels[idx].id.lowercased()] {
                            wasLive = true
                        } else {
                            wasLive = false
                        }
                        if wasLive {
                            channels[idx].lastStreamedAt = now
                        } else if let lastStreamed {
                            channels[idx].lastStreamedAt = lastStreamed
                        }
                    case .upcoming, .unknown:
                        break
                    }
                }

            case .notifyLive(let channel, let videoId, let title):
                if let idx = Self.channelIndex(for: channel.id, in: channels) {
                    channels[idx].lastNotifiedVideoId = videoId
                    let current = channels[idx]
                    let status = StreamStatus.live(videoId: videoId, title: title, startedAt: nil)
                    let targetURL = Self.watchURLString(for: current, status: status)
                    pendingNotifications.append((
                        channelId: current.id,
                        item: CliNotificationItem(
                            kind: "live",
                            channelName: current.name,
                            title: current.name,
                            body: "🔴 LIVE: \(title)",
                            targetURL: targetURL,
                            playSound: settings.playNotificationSound
                        )
                    ))
                }

            case .notifyUpcoming(let channel, let videoId, let title):
                if let idx = Self.channelIndex(for: channel.id, in: channels) {
                    channels[idx].lastNotifiedUpcomingId = videoId
                    let current = channels[idx]
                    let status = StreamStatus.upcoming(videoId: videoId, title: title, scheduledStartTime: nil)
                    let targetURL = Self.watchURLString(for: current, status: status)
                    pendingNotifications.append((
                        channelId: current.id,
                        item: CliNotificationItem(
                            kind: "upcoming",
                            channelName: current.name,
                            title: current.name,
                            body: "⏳ UPCOMING: \(title)",
                            targetURL: targetURL,
                            playSound: settings.playNotificationSound
                        )
                    ))
                }

            case .notifyDeveloperBirthday(let year):
                updatedDevBirthdayYear = year
                pendingNotifications.append((
                    channelId: nil,
                    item: CliNotificationItem(
                        kind: "developerBirthday",
                        channelName: nil,
                        title: "🎉 Happy Birthday to the Developer!",
                        body: "Today is Caitlyn Momochi's birthday! Click to send a birthday wish.",
                        targetURL: Self.devBirthdayMailto,
                        playSound: settings.playNotificationSound
                    )
                ))

            case .refreshFinished:
                break
            }
        }

        let snapshot = channels
        let envelope = try withDataDirLock(in: dataDir) {
            let channelStorage = JSONFileChannelStorage(fileURL: dataDir.appendingPathComponent("channels.json"))
            let settingsStorage = JSONFileSettingsStorage(fileURL: dataDir.appendingPathComponent("settings.json"))
            let diskChannels = try channelStorage.loadChannels()
            var reconciledSettings = try settingsStorage.loadSettings()
            if let year = updatedDevBirthdayYear {
                reconciledSettings.lastNotifiedDevBirthdayYear = year
            }
            let diskIds = Set(diskChannels.map { $0.id.lowercased() })

            var reconciledChannels: [WatchedChannel] = []
            for diskChannel in diskChannels {
                if let updated = snapshot.first(where: { $0.id.lowercased() == diskChannel.id.lowercased() }) {
                    reconciledChannels.append(updated)
                } else {
                    reconciledChannels.append(diskChannel)
                }
            }

            let filteredStatuses = updatedStatuses.filter { key, _ in
                diskIds.contains(key.lowercased())
            }
            let filteredNotifications = pendingNotifications.compactMap { entry -> CliNotificationItem? in
                guard let channelId = entry.channelId else {
                    return entry.item
                }
                return diskIds.contains(channelId.lowercased()) ? entry.item : nil
            }

            try channelStorage.saveChannels(reconciledChannels)
            if updatedDevBirthdayYear != nil {
                try settingsStorage.saveSettings(reconciledSettings)
            }
            try saveStatuses(filteredStatuses, in: dataDir)

            return buildEnvelope(
                channels: reconciledChannels,
                settings: reconciledSettings,
                statuses: filteredStatuses,
                notifications: filteredNotifications,
                now: now,
                calendar: calendar
            )
        }
        return CliCommandResponse(outcome: "ok", state: envelope)
    }

    private func runAdd(
        options: ParsedOptions,
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        guard let rawInput = options.values["input"] else {
            throw CliRunnerError.missingOption("--input")
        }
        let platformRaw = (options.values["platform"] ?? "youtube").lowercased()
        guard let selectedPlatform = StreamPlatform(rawValue: platformRaw) else {
            throw CliRunnerError.invalidOptionValue("--platform", platformRaw)
        }
        let ignoreSecret = options.flags.contains("ignore-secret")

        let trimmed = rawInput.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            let errorMsg = selectedPlatform == .youtube
                ? "Please enter a valid channel handle or YouTube URL"
                : "Please enter a valid Twitch username or URL"
            return CliCommandResponse(outcome: "validation_error", errorMessage: errorMsg)
        }

        let resolvedPlatform: StreamPlatform
        let normalized: String

        if let detected = WatchedChannel.detectPlatformAndHandle(from: trimmed) {
            resolvedPlatform = detected.platform
            normalized = detected.handle
        } else if let norm = WatchedChannel.normalizeHandle(trimmed, for: selectedPlatform) {
            resolvedPlatform = selectedPlatform
            normalized = norm
        } else {
            let errorMsg = selectedPlatform == .youtube
                ? "Please enter a valid channel handle or YouTube URL"
                : "Please enter a valid Twitch username or URL"
            return CliCommandResponse(outcome: "validation_error", errorMessage: errorMsg)
        }

        var (channels, settings, statuses) = try loadOrSeedState(in: dataDir)
        let targetId = WatchedChannel.makeID(platform: resolvedPlatform, handle: normalized).lowercased()

        if channels.contains(where: { $0.id.lowercased() == targetId }) {
            return CliCommandResponse(outcome: "validation_error", errorMessage: "Channel is already in your watchlist")
        }

        if resolvedPlatform == .youtube && SecretCheck.isTarget(normalized) && !ignoreSecret {
            let strings = SecretCheck.dialogStrings()
            return CliCommandResponse(
                outcome: "secret_prompt",
                secretDialog: SecretDialogPayload(
                    title: strings.title,
                    message: strings.message,
                    abort: strings.abort,
                    cancel: strings.cancel,
                    ignore: strings.ignore
                )
            )
        }

        let defaultDisplayName = (resolvedPlatform == .youtube && normalized.lowercased() == "@bukancaitlyn")
            ? "Caitlyn Momochi"
            : normalized

        let newChannel = WatchedChannel(
            id: WatchedChannel.makeID(platform: resolvedPlatform, handle: normalized),
            platform: resolvedPlatform,
            name: defaultDisplayName,
            handle: normalized,
            avatarURL: nil
        )
        channels.append(newChannel)

        var notifications: [CliNotificationItem] = []
        let currentYear = calendar.component(.year, from: now)
        if CelebrationDetector.isTargetDeveloperChannel(newChannel),
           CelebrationDetector.isDeveloperBirthday(date: now, calendar: calendar),
           settings.lastNotifiedDevBirthdayYear != currentYear {
            settings.lastNotifiedDevBirthdayYear = currentYear
            let settingsStorage = JSONFileSettingsStorage(fileURL: dataDir.appendingPathComponent("settings.json"))
            try settingsStorage.saveSettings(settings)
            notifications.append(
                CliNotificationItem(
                    kind: "developerBirthday",
                    channelName: nil,
                    title: "🎉 Happy Birthday to the Developer!",
                    body: "Today is Caitlyn Momochi's birthday! Click to send a birthday wish.",
                    targetURL: Self.devBirthdayMailto,
                    playSound: settings.playNotificationSound
                )
            )
        }

        let channelStorage = JSONFileChannelStorage(fileURL: dataDir.appendingPathComponent("channels.json"))
        try channelStorage.saveChannels(channels)

        let envelope = buildEnvelope(
            channels: channels,
            settings: settings,
            statuses: statuses,
            notifications: notifications,
            now: now,
            calendar: calendar
        )
        return CliCommandResponse(outcome: "added", state: envelope)
    }

    private func runRemove(
        options: ParsedOptions,
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        guard let targetIdRaw = options.values["id"] else {
            throw CliRunnerError.missingOption("--id")
        }
        let targetId = targetIdRaw.lowercased()
        var (channels, settings, statuses) = try loadOrSeedState(in: dataDir)

        channels.removeAll {
            $0.id.lowercased() == targetId ||
            (!targetId.contains(":") && $0.id.lowercased() == "youtube:\(targetId)")
        }
        statuses = statuses.filter { key, _ in
            let k = key.lowercased()
            return k != targetId && !(!targetId.contains(":") && k == "youtube:\(targetId)")
        }

        let channelStorage = JSONFileChannelStorage(fileURL: dataDir.appendingPathComponent("channels.json"))
        try channelStorage.saveChannels(channels)
        try saveStatuses(statuses, in: dataDir)

        let envelope = buildEnvelope(
            channels: channels,
            settings: settings,
            statuses: statuses,
            notifications: [],
            now: now,
            calendar: calendar
        )
        return CliCommandResponse(outcome: "removed", state: envelope)
    }

    private func runSettings(
        options: ParsedOptions,
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        let (channels, initialSettings, statuses) = try loadOrSeedState(in: dataDir)
        var settings = initialSettings

        if let val = options.values["notify-on-live"] {
            settings.notifyOnLive = try parseBool(val, option: "--notify-on-live")
        }
        if let val = options.values["notify-on-upcoming"] {
            settings.notifyOnUpcoming = try parseBool(val, option: "--notify-on-upcoming")
        }
        if let val = options.values["play-sound"] {
            settings.playNotificationSound = try parseBool(val, option: "--play-sound")
        }
        if let val = options.values["launch-at-login"] {
            settings.launchAtLogin = try parseBool(val, option: "--launch-at-login")
        }
        if let val = options.values["reduce-motion"] {
            settings.reduceMotion = try parseBool(val, option: "--reduce-motion")
        }
        if let val = options.values["has-completed-onboarding"] {
            settings.hasCompletedOnboarding = try parseBool(val, option: "--has-completed-onboarding")
        }
        if let val = options.values["update-consent"] {
            guard let consent = UpdateConsentState(rawValue: val) else {
                throw CliRunnerError.invalidOptionValue("--update-consent", val)
            }
            settings.updateConsent = consent
        }
        if let val = options.values["ignored-update-tag"] {
            let trimmed = val.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.isEmpty || trimmed.lowercased() == "none" {
                settings.ignoredUpdateTag = nil
            } else {
                settings.ignoredUpdateTag = trimmed
            }
        }

        let settingsStorage = JSONFileSettingsStorage(fileURL: dataDir.appendingPathComponent("settings.json"))
        try settingsStorage.saveSettings(settings)

        let envelope = buildEnvelope(
            channels: channels,
            settings: settings,
            statuses: statuses,
            notifications: [],
            now: now,
            calendar: calendar
        )
        return CliCommandResponse(outcome: "updated", state: envelope)
    }

    private func runCompleteOnboarding(
        options: ParsedOptions,
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        var (channels, settings, statuses) = try loadOrSeedState(in: dataDir)
        let platformRaw = (options.values["platform"] ?? "youtube").lowercased()
        guard let selectedPlatform = StreamPlatform(rawValue: platformRaw) else {
            throw CliRunnerError.invalidOptionValue("--platform", platformRaw)
        }
        let rawInput = (options.values["input"] ?? "").trimmingCharacters(in: .whitespacesAndNewlines)

        let ensureDefaultCaitlyn = { (list: inout [WatchedChannel]) in
            let defaultId = "youtube:@bukancaitlyn"
            if !list.contains(where: { $0.id.lowercased() == defaultId || $0.id.lowercased() == "@bukancaitlyn" }) {
                list.append(
                    WatchedChannel(
                        id: defaultId,
                        platform: .youtube,
                        name: "Caitlyn Momochi",
                        handle: "@bukancaitlyn"
                    )
                )
            }
        }

        if rawInput.isEmpty {
            ensureDefaultCaitlyn(&channels)
        } else {
            let resolvedPlatform: StreamPlatform
            let normalized: String

            if let detected = WatchedChannel.detectPlatformAndHandle(from: rawInput) {
                resolvedPlatform = detected.platform
                normalized = detected.handle
            } else if let norm = WatchedChannel.normalizeHandle(rawInput, for: selectedPlatform) {
                resolvedPlatform = selectedPlatform
                normalized = norm
            } else {
                let errorMsg = selectedPlatform == .youtube
                    ? "Please enter a valid YouTube handle (e.g. @channel)"
                    : "Please enter a valid Twitch username"
                return CliCommandResponse(outcome: "validation_error", errorMessage: errorMsg)
            }

            let isDefaultYouTube = resolvedPlatform == .youtube && normalized.lowercased() == "@bukancaitlyn"
            if isDefaultYouTube {
                ensureDefaultCaitlyn(&channels)
            } else {
                let defaultId = "youtube:@bukancaitlyn"
                if channels.count == 1,
                   let firstId = channels.first?.id.lowercased(),
                   firstId == defaultId || firstId == "@bukancaitlyn" {
                    channels.removeAll()
                    statuses.removeAll()
                }
                let targetId = WatchedChannel.makeID(platform: resolvedPlatform, handle: normalized)
                if !channels.contains(where: { $0.id.lowercased() == targetId.lowercased() }) {
                    channels.append(
                        WatchedChannel(
                            id: targetId,
                            platform: resolvedPlatform,
                            name: normalized,
                            handle: normalized,
                            avatarURL: nil
                        )
                    )
                }
            }
        }

        if let consentRaw = options.values["update-consent"], !consentRaw.isEmpty {
            guard let consent = UpdateConsentState(rawValue: consentRaw) else {
                throw CliRunnerError.invalidOptionValue("--update-consent", consentRaw)
            }
            settings.updateConsent = consent
        } else {
            settings.updateConsent = .allowed
        }
        settings.hasCompletedOnboarding = true

        let channelStorage = JSONFileChannelStorage(fileURL: dataDir.appendingPathComponent("channels.json"))
        let settingsStorage = JSONFileSettingsStorage(fileURL: dataDir.appendingPathComponent("settings.json"))
        try channelStorage.saveChannels(channels)
        try settingsStorage.saveSettings(settings)
        try saveStatuses(statuses, in: dataDir)

        let envelope = buildEnvelope(
            channels: channels,
            settings: settings,
            statuses: statuses,
            notifications: [],
            now: now,
            calendar: calendar
        )
        return CliCommandResponse(outcome: "completed", state: envelope)
    }

    private func runReset(
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        let defaultChannels = [
            WatchedChannel(
                id: "youtube:@bukancaitlyn",
                platform: .youtube,
                name: "Caitlyn Momochi",
                handle: "@bukancaitlyn"
            )
        ]
        let defaultSettings = AppSettings(hasCompletedOnboarding: true)
        let emptyStatuses: [String: StreamStatus] = [:]

        let channelStorage = JSONFileChannelStorage(fileURL: dataDir.appendingPathComponent("channels.json"))
        let settingsStorage = JSONFileSettingsStorage(fileURL: dataDir.appendingPathComponent("settings.json"))
        try channelStorage.saveChannels(defaultChannels)
        try settingsStorage.saveSettings(defaultSettings)
        try saveStatuses(emptyStatuses, in: dataDir)

        let envelope = buildEnvelope(
            channels: defaultChannels,
            settings: defaultSettings,
            statuses: emptyStatuses,
            notifications: [],
            now: now,
            calendar: calendar
        )
        return CliCommandResponse(outcome: "reset", state: envelope)
    }

    private func runExportBackup(
        options: ParsedOptions,
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        guard let filePath = options.values["file"], !filePath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw CliRunnerError.missingOption("--file")
        }
        let (channels, settings, statuses) = try loadOrSeedState(in: dataDir)
        let payload = BackupPayload(
            exportedAt: now,
            settings: settings,
            channels: channels
        )

        do {
            let data = try payload.encodedJSONData()
            let fileURL = URL(fileURLWithPath: filePath)
            let parentDir = fileURL.deletingLastPathComponent()
            if !FileManager.default.fileExists(atPath: parentDir.path) {
                try FileManager.default.createDirectory(at: parentDir, withIntermediateDirectories: true)
            }
            try data.write(to: fileURL, options: .atomic)
        } catch {
            return CliCommandResponse(outcome: "backup_error", errorMessage: error.localizedDescription)
        }

        let envelope = buildEnvelope(
            channels: channels,
            settings: settings,
            statuses: statuses,
            notifications: [],
            now: now,
            calendar: calendar
        )
        return CliCommandResponse(outcome: "exported", state: envelope)
    }

    private func runValidateBackup(
        options: ParsedOptions,
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        guard let filePath = options.values["file"], !filePath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw CliRunnerError.missingOption("--file")
        }
        let (channels, settings, statuses) = try loadOrSeedState(in: dataDir)
        let envelope = buildEnvelope(
            channels: channels,
            settings: settings,
            statuses: statuses,
            notifications: [],
            now: now,
            calendar: calendar
        )

        do {
            let data = try Data(contentsOf: URL(fileURLWithPath: filePath))
            let payload = try BackupPayload.decodeAndValidate(from: data)
            return CliCommandResponse(
                outcome: "valid_backup",
                state: envelope,
                backupChannelCount: payload.channels.count
            )
        } catch let error as BackupError {
            return CliCommandResponse(outcome: "backup_error", errorMessage: error.errorDescription, state: envelope)
        } catch {
            return CliCommandResponse(outcome: "backup_error", errorMessage: error.localizedDescription, state: envelope)
        }
    }

    private func runRestoreBackup(
        options: ParsedOptions,
        in dataDir: URL,
        now: Date,
        calendar: Calendar
    ) throws -> CliCommandResponse {
        guard let filePath = options.values["file"], !filePath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw CliRunnerError.missingOption("--file")
        }

        let payload: BackupPayload
        do {
            let data = try Data(contentsOf: URL(fileURLWithPath: filePath))
            payload = try BackupPayload.decodeAndValidate(from: data)
        } catch let error as BackupError {
            return CliCommandResponse(outcome: "backup_error", errorMessage: error.errorDescription)
        } catch {
            return CliCommandResponse(outcome: "backup_error", errorMessage: error.localizedDescription)
        }

        let (sanitizedSettings, sanitizedChannels) = payload.sanitizedForRestore()
        let emptyStatuses: [String: StreamStatus] = [:]

        let channelStorage = JSONFileChannelStorage(fileURL: dataDir.appendingPathComponent("channels.json"))
        let settingsStorage = JSONFileSettingsStorage(fileURL: dataDir.appendingPathComponent("settings.json"))
        try channelStorage.saveChannels(sanitizedChannels)
        try settingsStorage.saveSettings(sanitizedSettings)
        try saveStatuses(emptyStatuses, in: dataDir)

        let envelope = buildEnvelope(
            channels: sanitizedChannels,
            settings: sanitizedSettings,
            statuses: emptyStatuses,
            notifications: [],
            now: now,
            calendar: calendar
        )
        return CliCommandResponse(outcome: "restored", state: envelope, backupChannelCount: sanitizedChannels.count)
    }

    private func runCheckUpdate(
        options: ParsedOptions,
        in dataDir: URL,
        fetcher: @escaping HTTPFetcher,
        now: Date,
        calendar: Calendar
    ) async throws -> CliCommandResponse {
        let manual = options.flags.contains("manual")
        let repoURLString = options.values["repo-url"] ?? Self.defaultGithubReleasesLatestURL
        let currentVersion = options.values["current-version"] ?? BackupPayload.defaultAppVersion

        let loaded = try withDataDirLock(in: dataDir) {
            try loadOrSeedState(in: dataDir)
        }

        if !manual && loaded.settings.updateConsent != .allowed {
            let envelope = buildEnvelope(
                channels: loaded.channels,
                settings: loaded.settings,
                statuses: loaded.statuses,
                notifications: [],
                now: now,
                calendar: calendar
            )
            return CliCommandResponse(
                outcome: "ok",
                state: envelope,
                updateResult: CliUpdateCheckResult(status: "skippedNotConsented")
            )
        }

        guard let url = URL(string: repoURLString) else {
            return CliCommandResponse(
                outcome: "ok",
                updateResult: CliUpdateCheckResult(status: "failed", errorMessage: "Invalid release URL")
            )
        }

        var request = URLRequest(url: url)
        request.httpMethod = "GET"
        request.setValue("VRemind-win/\(currentVersion)", forHTTPHeaderField: "User-Agent")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")

        let checkResult: CliUpdateCheckResult
        var didSucceedHTTP = false

        do {
            let (data, response) = try await fetcher(request)
            if let httpResponse = response as? HTTPURLResponse, !(200...299).contains(httpResponse.statusCode) {
                checkResult = CliUpdateCheckResult(
                    status: "failed",
                    errorMessage: "GitHub API request failed with status \(httpResponse.statusCode)",
                    httpStatusCode: httpResponse.statusCode
                )
            } else {
                didSucceedHTTP = true
                let decoder = JSONDecoder()
                decoder.dateDecodingStrategy = .iso8601
                let release = try decoder.decode(GitHubRelease.self, from: data)
                let remoteVersion = SemanticVersion(release.tagName)
                let localVersion = SemanticVersion(currentVersion)

                if remoteVersion > localVersion {
                    if !manual && release.tagName == loaded.settings.ignoredUpdateTag {
                        checkResult = CliUpdateCheckResult(status: "skippedIgnored", latestVersion: release.tagName)
                    } else {
                        let preferredAsset = release.assets.first {
                            let lower = $0.name.lowercased()
                            return lower.hasSuffix(".zip") || lower.hasSuffix(".exe")
                        }
                        let downloadURL = preferredAsset?.browserDownloadURL.absoluteString ?? release.htmlURL.absoluteString
                        checkResult = CliUpdateCheckResult(
                            status: "updateAvailable",
                            latestVersion: release.tagName,
                            release: CliReleaseDto(
                                tagName: release.tagName,
                                name: release.name,
                                body: release.body,
                                htmlURL: release.htmlURL.absoluteString,
                                downloadURL: downloadURL
                            )
                        )
                    }
                } else if localVersion > remoteVersion {
                    checkResult = CliUpdateCheckResult(status: "localVersionNewer", remoteVersion: release.tagName)
                } else {
                    checkResult = CliUpdateCheckResult(status: "upToDate", latestVersion: release.tagName)
                }
            }
        } catch {
            checkResult = CliUpdateCheckResult(status: "failed", errorMessage: error.localizedDescription)
        }

        let envelope = try withDataDirLock(in: dataDir) {
            let (channels, currentSettings, statuses) = try loadOrSeedState(in: dataDir)
            var settings = currentSettings
            if didSucceedHTTP {
                settings.lastUpdateCheckDate = now
                let settingsStorage = JSONFileSettingsStorage(fileURL: dataDir.appendingPathComponent("settings.json"))
                try settingsStorage.saveSettings(settings)
            }
            return buildEnvelope(
                channels: channels,
                settings: settings,
                statuses: statuses,
                notifications: [],
                now: now,
                calendar: calendar
            )
        }

        return CliCommandResponse(
            outcome: "ok",
            state: envelope,
            updateResult: checkResult
        )
    }

    // MARK: - Persistence & Projection Helpers

    private func withDataDirLock<T>(in dataDir: URL, _ body: () throws -> T) throws -> T {
        if !FileManager.default.fileExists(atPath: dataDir.path) {
            try FileManager.default.createDirectory(at: dataDir, withIntermediateDirectories: true)
        }
        let lockPath = dataDir.appendingPathComponent(".vremind.lock").path
        #if os(Windows)
        return try lockPath.withCString(encodedAs: UTF16.self) { widePath in
            let handle = CreateFileW(
                widePath,
                GENERIC_READ | UInt32(GENERIC_WRITE),
                UInt32(FILE_SHARE_READ | FILE_SHARE_WRITE),
                nil,
                UInt32(OPEN_ALWAYS),
                UInt32(FILE_ATTRIBUTE_NORMAL),
                nil
            )
            if handle == INVALID_HANDLE_VALUE {
                return try body()
            }
            defer { CloseHandle(handle) }

            var overlapped = OVERLAPPED()
            let locked = LockFileEx(handle, UInt32(LOCKFILE_EXCLUSIVE_LOCK), 0, 1, 0, &overlapped)
            defer {
                if locked {
                    var unlockOverlapped = OVERLAPPED()
                    _ = UnlockFileEx(handle, 0, 1, 0, &unlockOverlapped)
                }
            }
            return try body()
        }
        #else
        let fd = open(lockPath, O_CREAT | O_RDWR, 0o600)
        if fd < 0 {
            return try body()
        }
        defer { close(fd) }
        _ = flock(fd, LOCK_EX)
        defer { _ = flock(fd, LOCK_UN) }
        return try body()
        #endif
    }

    private func loadOrSeedState(in dataDir: URL) throws -> (channels: [WatchedChannel], settings: AppSettings, statuses: [String: StreamStatus]) {
        let channelsURL = dataDir.appendingPathComponent("channels.json")
        let settingsURL = dataDir.appendingPathComponent("settings.json")
        let channelStorage = JSONFileChannelStorage(fileURL: channelsURL)
        let settingsStorage = JSONFileSettingsStorage(fileURL: settingsURL)

        let channels: [WatchedChannel]
        if FileManager.default.fileExists(atPath: channelsURL.path) {
            channels = try channelStorage.loadChannels()
        } else {
            let seed = [
                WatchedChannel(
                    id: "youtube:@bukancaitlyn",
                    platform: .youtube,
                    name: "Caitlyn Momochi",
                    handle: "@bukancaitlyn"
                )
            ]
            try channelStorage.saveChannels(seed)
            channels = seed
        }

        let settings = try settingsStorage.loadSettings()
        let statuses = loadStatuses(in: dataDir)
        return (channels, settings, statuses)
    }

    private func loadStatuses(in dataDir: URL) -> [String: StreamStatus] {
        let url = dataDir.appendingPathComponent("statuses.json")
        guard let data = try? Data(contentsOf: url),
              let decoded = try? JSONDecoder().decode([String: StreamStatus].self, from: data) else {
            return [:]
        }
        return decoded
    }

    private func saveStatuses(_ statuses: [String: StreamStatus], in dataDir: URL) throws {
        let url = dataDir.appendingPathComponent("statuses.json")
        if !FileManager.default.fileExists(atPath: dataDir.path) {
            try FileManager.default.createDirectory(at: dataDir, withIntermediateDirectories: true)
        }
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        let data = try encoder.encode(statuses)
        try data.write(to: url, options: .atomic)
    }

    private func buildEnvelope(
        channels: [WatchedChannel],
        settings: AppSettings,
        statuses: [String: StreamStatus],
        notifications: [CliNotificationItem],
        now: Date,
        calendar: Calendar
    ) -> CliStateEnvelope {
        let split = StreamMonitorEngine.splitSections(channels: channels, statuses: statuses)
        let liveItems = split.live.map { makeItem($0, status: statuses[$0.id] ?? statuses[$0.id.lowercased()] ?? .unknown, now: now, calendar: calendar) }
        let upcomingItems = split.upcoming.map { makeItem($0, status: statuses[$0.id] ?? statuses[$0.id.lowercased()] ?? .unknown, now: now, calendar: calendar) }
        let offlineItems = split.offline.map { makeItem($0, status: statuses[$0.id] ?? statuses[$0.id.lowercased()] ?? .unknown, now: now, calendar: calendar) }

        let anyLiveCelebrating = liveItems.contains { $0.isCelebration }
        let anyTodayUpcomingCelebrating = split.upcoming.contains { ch in
            let st = statuses[ch.id] ?? statuses[ch.id.lowercased()] ?? .unknown
            if case .upcoming(_, let title, let scheduledStart) = st {
                guard CelebrationDetector.detect(title: title).isCelebration else { return false }
                if let scheduledStart {
                    return calendar.isDate(scheduledStart, inSameDayAs: now)
                }
                return true
            }
            return false
        }
        let anyDevBirthdayToday = CelebrationDetector.isDeveloperBirthday(date: now, calendar: calendar) &&
            channels.contains { CelebrationDetector.isTargetDeveloperChannel($0) }
        let isPopoverCelebrating = anyLiveCelebrating || anyTodayUpcomingCelebrating || anyDevBirthdayToday

        return CliStateEnvelope(
            liveCount: liveItems.count,
            isPopoverCelebrating: isPopoverCelebrating,
            appVersion: BackupPayload.defaultAppVersion,
            sections: CliSections(live: liveItems, upcoming: upcomingItems, offline: offlineItems),
            settings: settings,
            notifications: notifications
        )
    }

    private func makeItem(
        _ channel: WatchedChannel,
        status: StreamStatus,
        now: Date,
        calendar: Calendar
    ) -> CliChannelItem {
        let formattedHandle: String
        switch channel.platform {
        case .youtube:
            formattedHandle = channel.handle.hasPrefix("@") ? channel.handle : "@\(channel.handle)"
        case .twitch:
            formattedHandle = channel.handle.hasPrefix("@") ? String(channel.handle.dropFirst()) : channel.handle
        }

        let statusKind: String
        let streamTitle: String?
        let timeLabel: String

        switch status {
        case .live(_, let title, let startedAt):
            statusKind = "live"
            streamTitle = title.isEmpty ? nil : title
            if let startedAt {
                timeLabel = "Live for \(ElapsedTime.short(from: startedAt, to: now))"
            } else {
                timeLabel = "Live now"
            }

        case .upcoming(_, let title, let scheduledStartTime):
            statusKind = "upcoming"
            streamTitle = title.isEmpty ? nil : title
            if let scheduledStartTime {
                let formatter = DateFormatter()
                formatter.calendar = calendar
                formatter.timeStyle = .short
                formatter.dateStyle = calendar.isDate(scheduledStartTime, inSameDayAs: now) ? .none : .short
                timeLabel = "Starts: \(formatter.string(from: scheduledStartTime))"
            } else {
                timeLabel = "Upcoming"
            }

        case .offline(let lastStreamedAt):
            statusKind = "offline"
            streamTitle = nil
            let effectiveDate = lastStreamedAt ?? channel.lastStreamedAt
            if let effectiveDate {
                timeLabel = "Last live: \(ElapsedTime.relative(from: effectiveDate, now: now))"
            } else {
                timeLabel = "Offline"
            }

        case .unknown:
            statusKind = "unknown"
            streamTitle = nil
            if let lastStreamedAt = channel.lastStreamedAt {
                timeLabel = "Last live: \(ElapsedTime.relative(from: lastStreamedAt, now: now))"
            } else {
                timeLabel = "Offline"
            }
        }

        let combinedCelebration = CelebrationDetector.detect(status: status, channel: channel, date: now, calendar: calendar)
        let isCelebration = combinedCelebration.isCelebration
        let celebrationTypeString: String?
        if let type = combinedCelebration.type {
            celebrationTypeString = Self.celebrationTypeName(type)
        } else {
            celebrationTypeString = nil
        }

        return CliChannelItem(
            id: channel.id,
            platform: channel.platform.rawValue,
            platformDisplayName: channel.platform.displayName,
            name: channel.name,
            handle: channel.handle,
            formattedHandle: formattedHandle,
            avatarURL: channel.avatarURL?.absoluteString,
            statusKind: statusKind,
            streamTitle: streamTitle,
            timeLabel: timeLabel,
            watchURL: Self.watchURLString(for: channel, status: status),
            isCelebration: isCelebration,
            celebrationType: celebrationTypeString
        )
    }

    private static func celebrationTypeName(_ type: CelebrationType) -> String {
        switch type {
        case .birthday:
            return "birthday"
        case .debut:
            return "debut"
        case .anniversary:
            return "anniversary"
        case .developerBirthday:
            return "developerBirthday"
        }
    }

    private static func watchURLString(for channel: WatchedChannel, status: StreamStatus) -> String {
        if let url = StreamProviderRegistry.default.watchURL(for: channel, status: status) {
            return url.absoluteString
        }
        switch channel.platform {
        case .youtube:
            return "https://www.youtube.com/\(channel.handle)"
        case .twitch:
            return "https://www.twitch.tv/\(channel.handle)"
        }
    }

    private static func channelIndex(for channelId: String, in channels: [WatchedChannel]) -> Int? {
        let target = channelId.lowercased()
        return channels.firstIndex {
            $0.id.lowercased() == target ||
            (!target.contains(":") && $0.id.lowercased() == "youtube:\(target)")
        }
    }

    // MARK: - CLI Argument Parsing

    private struct ParsedOptions {
        var values: [String: String] = [:]
        var flags: Set<String> = []
    }

    private static let standaloneFlags: Set<String> = ["ignore-secret", "manual"]

    private func parseOptions(_ args: [String]) -> ParsedOptions {
        var parsed = ParsedOptions()
        var i = 0
        while i < args.count {
            let arg = args[i]
            if arg.hasPrefix("--") {
                let key = String(arg.dropFirst(2))
                if Self.standaloneFlags.contains(key) {
                    parsed.flags.insert(key)
                    i += 1
                } else if i + 1 < args.count {
                    parsed.values[key] = args[i + 1]
                    i += 2
                } else {
                    parsed.flags.insert(key)
                    i += 1
                }
            } else {
                i += 1
            }
        }
        return parsed
    }

    private func resolveDataDirectory(from options: ParsedOptions) -> URL {
        if let custom = options.values["data-dir"], !custom.isEmpty {
            return URL(fileURLWithPath: custom, isDirectory: true)
        }
        if let appData = ProcessInfo.processInfo.environment["APPDATA"], !appData.isEmpty {
            return URL(fileURLWithPath: appData, isDirectory: true)
                .appendingPathComponent("VRemind", isDirectory: true)
        }
        return FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".config/vremind", isDirectory: true)
    }

    private func parseBool(_ raw: String, option: String) throws -> Bool {
        switch raw.lowercased() {
        case "true", "1", "yes":
            return true
        case "false", "0", "no":
            return false
        default:
            throw CliRunnerError.invalidOptionValue(option, raw)
        }
    }
}
