import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif
import Testing
@testable import VRemindCoreCLIKit
import VRemindCore

@Suite struct CliRunnerTests {
    private static let secretHandleBytes: [UInt8] = [55, 59, 46, 41, 47, 51, 48, 47, 54, 51, 59, 52, 57, 50]
    private static var secretHandle: String {
        String(decoding: secretHandleBytes.map { $0 ^ 0x5a }, as: UTF8.self)
    }

    private func makeTempDir() throws -> URL {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent("VRemindCoreCLITests-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir
    }

    private func makeLiveHTML(videoId: String, channelTitle: String, streamTitle: String) -> Data {
        let html = """
        <!DOCTYPE html>
        <html>
        <head><title>\(streamTitle) - YouTube</title></head>
        <body>
        <script>
        var ytInitialPlayerResponse = {
            "videoDetails": {
                "videoId": "\(videoId)",
                "title": "\(streamTitle)",
                "author": "\(channelTitle)",
                "isLive": true
            },
            "microformat": {
                "playerMicroformatRenderer": {
                    "liveBroadcastDetails": {
                        "isLiveNow": true,
                        "startTimestamp": "2026-11-11T12:00:00Z"
                    }
                }
            }
        };
        </script>
        </body>
        </html>
        """
        return Data(html.utf8)
    }

    @Test func testFirstRunSeedsDefaultCaitlynChannelWhenFileMissing() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        let response = try await runner.execute(arguments: ["state", "--data-dir", dir.path])

        #expect(response.outcome == "ok")
        let state = try #require(response.state)
        #expect(state.sections.live.isEmpty)
        #expect(state.sections.upcoming.isEmpty)
        #expect(state.sections.offline.count == 1)
        #expect(state.sections.offline[0].id == "youtube:@bukancaitlyn")
        #expect(state.sections.offline[0].name == "Caitlyn Momochi")
        #expect(FileManager.default.fileExists(atPath: dir.appendingPathComponent("channels.json").path))
    }

    @Test func testRemovingLastChannelPreservesEmptyWatchlistWithoutReseeding() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path])
        let removeResponse = try await runner.execute(
            arguments: ["remove", "--id", "youtube:@bukancaitlyn", "--data-dir", dir.path]
        )
        #expect(removeResponse.outcome == "removed")

        let secondState = try await runner.execute(arguments: ["state", "--data-dir", dir.path])
        let state = try #require(secondState.state)
        #expect(state.sections.live.isEmpty)
        #expect(state.sections.upcoming.isEmpty)
        #expect(state.sections.offline.isEmpty)
    }

    @Test func testCorruptStatusesJsonFallsBackGracefully() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let statusesURL = dir.appendingPathComponent("statuses.json")
        try Data("{not valid json".utf8).write(to: statusesURL)

        let runner = CliRunner()
        let response = try await runner.execute(arguments: ["state", "--data-dir", dir.path])

        #expect(response.outcome == "ok")
        let state = try #require(response.state)
        #expect(state.sections.offline.count == 1)
    }

    @Test func testAddValidationAndDuplicateAndSecretPrompt() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path])

        let invalidResp = try await runner.execute(
            arguments: ["add", "--platform", "youtube", "--input", "bad handle", "--data-dir", dir.path]
        )
        #expect(invalidResp.outcome == "validation_error")
        #expect(invalidResp.errorMessage == "Please enter a valid channel handle or YouTube URL")

        let dupResp = try await runner.execute(
            arguments: ["add", "--platform", "youtube", "--input", "@bukancaitlyn", "--data-dir", dir.path]
        )
        #expect(dupResp.outcome == "validation_error")
        #expect(dupResp.errorMessage == "Channel is already in your watchlist")

        let secretPromptResp = try await runner.execute(
            arguments: ["add", "--platform", "youtube", "--input", "@\(Self.secretHandle)", "--data-dir", dir.path]
        )
        #expect(secretPromptResp.outcome == "secret_prompt")
        let dialog = try #require(secretPromptResp.secretDialog)
        #expect(!dialog.title.isEmpty)

        let secretAddedResp = try await runner.execute(
            arguments: ["add", "--platform", "youtube", "--input", "@\(Self.secretHandle)", "--ignore-secret", "--data-dir", dir.path]
        )
        #expect(secretAddedResp.outcome == "added")
        let addedState = try #require(secretAddedResp.state)
        #expect(addedState.sections.offline.count == 2)
        #expect(addedState.sections.offline.last?.handle == "@\(Self.secretHandle)")
    }

    @Test func testRefreshPassEmitsNotificationsDeduplicatesAndPreservesMidRefreshDeletion() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path])
        _ = try await runner.execute(arguments: ["add", "--platform", "youtube", "--input", "@tobedeleted", "--data-dir", dir.path])

        let htmlData = makeLiveHTML(videoId: "vid123", channelTitle: "Caitlyn Momochi", streamTitle: "Test Title")
        let mockFetcher: HTTPFetcher = { request in
            let urlStr = request.url?.absoluteString ?? ""
            if urlStr.contains("@tobedeleted") {
                // Simulate concurrent delete while network refresh is in flight
                let storage = JSONFileChannelStorage(fileURL: dir.appendingPathComponent("channels.json"))
                let current = try storage.loadChannels().filter { $0.id != "youtube:@tobedeleted" }
                try storage.saveChannels(current)
            }
            let response = HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!
            return (htmlData, response)
        }

        let firstRefresh = try await runner.execute(
            arguments: ["refresh", "--data-dir", dir.path],
            fetcher: mockFetcher,
            now: Date(timeIntervalSince1970: 1_794_487_320) // 2026-11-11T12:42:00Z
        )
        #expect(firstRefresh.outcome == "ok")
        let firstState = try #require(firstRefresh.state)
        #expect(firstState.liveCount == 1)
        #expect(firstState.sections.live.count == 1)
        #expect(firstState.sections.live[0].id == "youtube:@bukancaitlyn")
        #expect(firstState.notifications.count == 1)
        #expect(firstState.notifications[0].kind == "live")
        #expect(firstState.notifications[0].body == "🔴 LIVE: Test Title")
        #expect(firstState.notifications[0].targetURL == "https://www.youtube.com/watch?v=vid123")

        // Ensure deleted channel stayed deleted on disk
        let stored = try JSONFileChannelStorage(fileURL: dir.appendingPathComponent("channels.json")).loadChannels()
        #expect(stored.map(\.id) == ["youtube:@bukancaitlyn"])

        // Second refresh with same videoId must deduplicate notifications
        let secondRefresh = try await runner.execute(
            arguments: ["refresh", "--data-dir", dir.path],
            fetcher: mockFetcher
        )
        #expect(secondRefresh.state?.notifications.isEmpty == true)
    }

    @Test func testRefreshDoesNotClobberSettingsChangedMidFlight() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path])

        let htmlData = makeLiveHTML(videoId: "vid999", channelTitle: "Caitlyn Momochi", streamTitle: "Bday Stream")
        let mockFetcher: HTTPFetcher = { request in
            // Simulate user toggling settings while refresh is in flight
            let settingsStorage = JSONFileSettingsStorage(fileURL: dir.appendingPathComponent("settings.json"))
            var updated = try settingsStorage.loadSettings()
            updated.notifyOnUpcoming = true
            updated.playNotificationSound = false
            try settingsStorage.saveSettings(updated)

            let response = HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!
            return (htmlData, response)
        }

        var dec31Components = DateComponents()
        dec31Components.year = 2026
        dec31Components.month = 12
        dec31Components.day = 31
        dec31Components.hour = 12
        var utcCal = Calendar(identifier: .gregorian)
        utcCal.timeZone = TimeZone(secondsFromGMT: 0)!
        let dec31Date = utcCal.date(from: dec31Components)!

        let refreshResp = try await runner.execute(
            arguments: ["refresh", "--data-dir", dir.path],
            fetcher: mockFetcher,
            now: dec31Date,
            calendar: utcCal
        )
        let persisted = try JSONFileSettingsStorage(fileURL: dir.appendingPathComponent("settings.json")).loadSettings()
        #expect(persisted.notifyOnUpcoming == true)
        #expect(persisted.playNotificationSound == false)
        #expect(persisted.lastNotifiedDevBirthdayYear == 2026)
        #expect(refreshResp.state?.settings.notifyOnUpcoming == true)
    }

    @Test func testCelebrationMetadataAndFutureUpcomingSuppression() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        var utcCal = Calendar(identifier: .gregorian)
        utcCal.timeZone = TimeZone(secondsFromGMT: 0)!
        let now = Date(timeIntervalSince1970: 1_794_487_200) // 2026-11-11T12:40:00Z
        let twoDaysLater = now.addingTimeInterval(2 * 86_400)
        let laterToday = now.addingTimeInterval(3_600)

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path], now: now, calendar: utcCal)

        let statusesURL = dir.appendingPathComponent("statuses.json")
        let encoder = JSONEncoder()

        // 1. Upcoming celebration stream scheduled 2 days in the future: row is celebration, popover confetti suppressed
        let futureStatuses: [String: StreamStatus] = [
            "youtube:@bukancaitlyn": .upcoming(videoId: "up1", title: "3D Debut Stream!", scheduledStartTime: twoDaysLater)
        ]
        try encoder.encode(futureStatuses).write(to: statusesURL)

        let futureResp = try await runner.execute(arguments: ["state", "--data-dir", dir.path], now: now, calendar: utcCal)
        let futureState = try #require(futureResp.state)
        #expect(futureState.sections.upcoming.count == 1)
        #expect(futureState.sections.upcoming[0].isCelebration == true)
        #expect(futureState.sections.upcoming[0].celebrationType == "debut")
        #expect(futureState.isPopoverCelebrating == false)
        #expect(futureState.appVersion == "1.3.2")

        // 2. Upcoming celebration stream scheduled later today: popover confetti active
        let todayStatuses: [String: StreamStatus] = [
            "youtube:@bukancaitlyn": .upcoming(videoId: "up2", title: "3D Debut Stream!", scheduledStartTime: laterToday)
        ]
        try encoder.encode(todayStatuses).write(to: statusesURL)

        let todayResp = try await runner.execute(arguments: ["state", "--data-dir", dir.path], now: now, calendar: utcCal)
        let todayState = try #require(todayResp.state)
        #expect(todayState.sections.upcoming[0].isCelebration == true)
        #expect(todayState.isPopoverCelebrating == true)

        // 3. Live birthday stream: both row and popover celebration active
        let liveStatuses: [String: StreamStatus] = [
            "youtube:@bukancaitlyn": .live(videoId: "lv1", title: "Happy Birthday Stream!", startedAt: now)
        ]
        try encoder.encode(liveStatuses).write(to: statusesURL)

        let liveResp = try await runner.execute(arguments: ["state", "--data-dir", dir.path], now: now, calendar: utcCal)
        let liveState = try #require(liveResp.state)
        #expect(liveState.sections.live[0].isCelebration == true)
        #expect(liveState.sections.live[0].celebrationType == "birthday")
        #expect(liveState.isPopoverCelebrating == true)
    }

    @Test func testAddCaitlynOnDec31TriggersBirthdayNotificationOnce() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        var utcCal = Calendar(identifier: .gregorian)
        utcCal.timeZone = TimeZone(secondsFromGMT: 0)!
        var dec31Components = DateComponents()
        dec31Components.year = 2026
        dec31Components.month = 12
        dec31Components.day = 31
        dec31Components.hour = 15
        let dec31Date = utcCal.date(from: dec31Components)!

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path], now: dec31Date, calendar: utcCal)
        _ = try await runner.execute(
            arguments: ["remove", "--id", "youtube:@bukancaitlyn", "--data-dir", dir.path],
            now: dec31Date,
            calendar: utcCal
        )

        let addResp = try await runner.execute(
            arguments: ["add", "--platform", "youtube", "--input", "@bukancaitlyn", "--data-dir", dir.path],
            now: dec31Date,
            calendar: utcCal
        )
        #expect(addResp.outcome == "added")
        let addState = try #require(addResp.state)
        #expect(addState.notifications.count == 1)
        #expect(addState.notifications[0].kind == "developerBirthday")
        #expect(addState.settings.lastNotifiedDevBirthdayYear == 2026)
        #expect(addState.isPopoverCelebrating == true)

        // Removing and adding again in the same year must not re-fire
        _ = try await runner.execute(
            arguments: ["remove", "--id", "youtube:@bukancaitlyn", "--data-dir", dir.path],
            now: dec31Date,
            calendar: utcCal
        )
        let secondAdd = try await runner.execute(
            arguments: ["add", "--platform", "youtube", "--input", "@bukancaitlyn", "--data-dir", dir.path],
            now: dec31Date,
            calendar: utcCal
        )
        #expect(secondAdd.state?.notifications.isEmpty == true)
    }

    @Test func testExtendedSettingsAndCompleteOnboardingAndFactoryReset() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path])

        let onboardResp = try await runner.execute(
            arguments: [
                "complete-onboarding",
                "--platform", "twitch",
                "--input", "ironmouse",
                "--update-consent", "allowed",
                "--data-dir", dir.path
            ]
        )
        #expect(onboardResp.outcome == "completed")
        let onboardState = try #require(onboardResp.state)
        #expect(onboardState.settings.hasCompletedOnboarding == true)
        #expect(onboardState.settings.updateConsent == .allowed)
        #expect(onboardState.sections.offline.map(\.id) == ["twitch:ironmouse"])

        let settingsResp = try await runner.execute(
            arguments: [
                "settings",
                "--reduce-motion", "true",
                "--ignored-update-tag", "v1.4.0",
                "--data-dir", dir.path
            ]
        )
        #expect(settingsResp.outcome == "updated")
        #expect(settingsResp.state?.settings.reduceMotion == true)
        #expect(settingsResp.state?.settings.ignoredUpdateTag == "v1.4.0")

        let resetResp = try await runner.execute(arguments: ["reset", "--data-dir", dir.path])
        #expect(resetResp.outcome == "reset")
        let resetState = try #require(resetResp.state)
        #expect(resetState.settings.reduceMotion == false)
        #expect(resetState.settings.hasCompletedOnboarding == true)
        #expect(resetState.sections.offline.map(\.id) == ["youtube:@bukancaitlyn"])
    }

    @Test func testBackupExportValidateAndSanitizedRestore() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path])
        _ = try await runner.execute(arguments: ["add", "--platform", "twitch", "--input", "ironmouse", "--data-dir", dir.path])

        // Simulate notified IDs and uncompleted onboarding in saved files before export
        let channelStorage = JSONFileChannelStorage(fileURL: dir.appendingPathComponent("channels.json"))
        var currentChannels = try channelStorage.loadChannels()
        currentChannels[0].lastNotifiedVideoId = "stale-vid"
        try channelStorage.saveChannels(currentChannels)

        let backupFile = dir.appendingPathComponent("test-backup.json")
        let exportResp = try await runner.execute(
            arguments: ["export-backup", "--file", backupFile.path, "--data-dir", dir.path]
        )
        #expect(exportResp.outcome == "exported")
        #expect(FileManager.default.fileExists(atPath: backupFile.path))

        let validateResp = try await runner.execute(
            arguments: ["validate-backup", "--file", backupFile.path, "--data-dir", dir.path]
        )
        #expect(validateResp.outcome == "valid_backup")
        #expect(validateResp.backupChannelCount == 2)

        // Reset to 1 channel, then restore backup
        _ = try await runner.execute(arguments: ["reset", "--data-dir", dir.path])
        let restoreResp = try await runner.execute(
            arguments: ["restore-backup", "--file", backupFile.path, "--data-dir", dir.path]
        )
        #expect(restoreResp.outcome == "restored")
        let restoredState = try #require(restoreResp.state)
        #expect(restoredState.sections.offline.count == 2)
        #expect(restoredState.settings.hasCompletedOnboarding == true)

        let restoredChannels = try channelStorage.loadChannels()
        #expect(restoredChannels.allSatisfy { $0.lastNotifiedVideoId == nil && $0.lastNotifiedUpcomingId == nil })

        // Corrupt backup validation fails gracefully
        let badFile = dir.appendingPathComponent("bad.json")
        try Data("not-json".utf8).write(to: badFile)
        let badValidate = try await runner.execute(
            arguments: ["validate-backup", "--file", badFile.path, "--data-dir", dir.path]
        )
        #expect(badValidate.outcome == "backup_error")
    }

    @Test func testCheckUpdateOutcomes() async throws {
        let dir = try makeTempDir()
        defer { try? FileManager.default.removeItem(at: dir) }

        let runner = CliRunner()
        _ = try await runner.execute(arguments: ["state", "--data-dir", dir.path])

        let releaseJSON = """
        {
          "tag_name": "v1.4.0",
          "name": "VRemind 1.4.0",
          "body": "### Highlights\\n- Feature parity",
          "html_url": "https://github.com/momochisoundpost/VRemind-win/releases/tag/v1.4.0",
          "assets": [
            {
              "name": "VRemindWin-x64.zip",
              "browser_download_url": "https://github.com/momochisoundpost/VRemind-win/releases/download/v1.4.0/VRemindWin-x64.zip"
            }
          ]
        }
        """
        let availableFetcher: HTTPFetcher = { request in
            let resp = HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!
            return (Data(releaseJSON.utf8), resp)
        }

        let checkAvailable = try await runner.execute(
            arguments: ["check-update", "--manual", "--data-dir", dir.path],
            fetcher: availableFetcher
        )
        #expect(checkAvailable.outcome == "ok")
        let result = try #require(checkAvailable.updateResult)
        #expect(result.status == "updateAvailable")
        #expect(result.release?.tagName == "v1.4.0")
        #expect(result.release?.downloadURL == "https://github.com/momochisoundpost/VRemind-win/releases/download/v1.4.0/VRemindWin-x64.zip")

        // 404 from private repo
        let notFoundFetcher: HTTPFetcher = { request in
            let resp = HTTPURLResponse(url: request.url!, statusCode: 404, httpVersion: nil, headerFields: nil)!
            return (Data("{}".utf8), resp)
        }
        let check404 = try await runner.execute(
            arguments: ["check-update", "--manual", "--data-dir", dir.path],
            fetcher: notFoundFetcher
        )
        #expect(check404.updateResult?.status == "failed")
        #expect(check404.updateResult?.httpStatusCode == 404)
    }
}
