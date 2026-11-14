import Foundation
import VRemindCoreCLIKit

@main
struct VRemindCoreCLIMain {
    static func main() async {
        do {
            let runner = CliRunner()
            let response = try await runner.execute(arguments: Array(CommandLine.arguments.dropFirst()))
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.sortedKeys]
            encoder.dateEncodingStrategy = .iso8601
            let data = try encoder.encode(response)
            FileHandle.standardOutput.write(data)
            FileHandle.standardOutput.write(Data("\n".utf8))
        } catch {
            let message = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
            FileHandle.standardError.write(Data((message + "\n").utf8))
            exit(1)
        }
    }
}
