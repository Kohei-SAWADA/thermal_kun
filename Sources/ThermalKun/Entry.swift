import AppKit
import Foundation

@main
struct ThermalKunMain {
    @MainActor
    static func main() {
        if CommandLine.arguments.contains("--diagnose") {
            let monitor = HardwareMonitor()
            _ = monitor.sample()
            Thread.sleep(forTimeInterval: 2)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            encoder.dateEncodingStrategy = .iso8601
            if let data = try? encoder.encode(monitor.sample()) { print(String(decoding: data, as: UTF8.self)) }
        } else {
            let app = NSApplication.shared
            let delegate = AppDelegate()
            app.delegate = delegate
            app.run()
        }
    }
}
