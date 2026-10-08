import Foundation

@main
struct ModelChecks {
    static func main() {
        var checks = 0
        var failures: [String] = []
        func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
            checks += 1
            if !condition() { failures.append(message) }
        }

        func reading(_ value: Double?, status: ReadingStatus = .available) -> MetricReading {
            MetricReading(value: value, status: status, source: "Test sensor", detail: "Model verification")
        }
        func snapshot(_ cpu: MetricReading, timestamp: Date = Date()) -> HardwareSnapshot {
            HardwareSnapshot(
                timestamp: timestamp, cpuTemperature: cpu,
                cpuUsage: reading(nil, status: .unavailable), gpuUsage: reading(nil, status: .unsupported),
                memoryUsed: reading(nil, status: .unavailable), memoryTotal: 0,
                memoryPressure: reading(nil, status: .unavailable),
                thermalState: "Unavailable", thermalStateSource: "Test"
            )
        }

        expect(MonitorHistory.validTemperature(reading(42)) == 42, "Available finite readings must be retained.")
        expect(MonitorHistory.validTemperature(reading(42, status: .unavailable)) == nil, "Unavailable readings must not retain a numeric temperature.")
        expect(MonitorHistory.validTemperature(reading(42, status: .unsupported)) == nil, "Unsupported readings must not retain a numeric temperature.")
        expect(MonitorHistory.validTemperature(reading(nil)) == nil, "Missing values must remain missing.")
        expect(MonitorHistory.validTemperature(reading(.nan)) == nil, "NaN must not enter history.")
        expect(MonitorHistory.validTemperature(reading(.infinity)) == nil, "Infinity must not enter history.")
        expect(MonitorHistory.validTemperature(reading(-.infinity)) == nil, "Negative infinity must not enter history.")

        var history = MonitorHistory()
        expect(history.points.isEmpty && history.cpuPeak == nil, "New history must have no fabricated readings or peak.")
        history.append(snapshot(reading(70)))
        history.append(snapshot(reading(55)))
        expect(history.cpuPeak == 70, "A lower CPU temperature must not reduce the session peak.")
        history.append(snapshot(reading(99, status: .unavailable)))
        expect(history.points.last?.cpu == nil, "Missing CPU temperatures must create gaps instead of zeros or retained readings.")
        expect(history.cpuPeak == 70, "Failed readings must not change the CPU peak.")

        let baseTime = Date(timeIntervalSince1970: 1_000)
        for index in 0..<(MonitorHistory.capacity * 3) {
            history.append(snapshot(reading(40), timestamp: baseTime.addingTimeInterval(Double(index))))
        }
        expect(history.points.count == MonitorHistory.capacity, "History must remain bounded after repeated updates.")
        expect(history.points.first?.timestamp == baseTime.addingTimeInterval(Double(MonitorHistory.capacity * 2)), "History must evict the oldest points first.")
        expect(history.points.last?.timestamp == baseTime.addingTimeInterval(Double(MonitorHistory.capacity * 3 - 1)), "History must retain the most recent sample.")
        expect(history.cpuPeak == 70, "The session CPU peak must survive graph history eviction.")

        let preservedPoints = history.points.count
        history.resetPeaks()
        expect(history.cpuPeak == nil, "Reset must clear the CPU peak.")
        expect(history.points.count == preservedPoints, "Peak reset must preserve graph history.")
        history.append(snapshot(reading(nil, status: .unavailable)))
        expect(history.cpuPeak == nil, "Failed readings after reset must not create a zero CPU peak.")
        history.append(snapshot(reading(35)))
        expect(history.cpuPeak == 35, "The CPU peak must start with the first valid reading after reset.")

        let defaults = AppSettings()
        expect(defaults.updateInterval == 2, "Default polling interval must be two seconds.")
        expect(defaults.sanitized() == defaults, "Default settings must be valid.")
        for interval in [1.0, 2, 5, 10] {
            var settings = defaults
            settings.updateInterval = interval
            expect(settings.sanitized().updateInterval == interval, "Supported polling intervals must be retained.")
        }
        for interval in [-1.0, 0, 0.5, 3, Double.nan, Double.infinity] {
            var settings = defaults
            settings.updateInterval = interval
            expect(settings.sanitized().updateInterval == 2, "Invalid polling intervals must fall back to two seconds.")
        }
        let legacySettings = Data(#"{"compact":true,"alwaysOnTop":true,"opacity":0.45,"updateInterval":5,"animations":false,"appearance":"Light"}"#.utf8)
        let migrated = try? JSONDecoder().decode(AppSettings.self, from: legacySettings)
        expect(migrated?.compact == true && migrated?.alwaysOnTop == true && migrated?.updateInterval == 5 && migrated?.animations == false && migrated?.appearance == "Light",
               "Legacy settings must preserve user choices when the opacity option is removed.")
        for appearance in ["Dark", "Light", "System"] {
            var settings = defaults
            settings.appearance = appearance
            expect(settings.sanitized().appearance == appearance, "Supported appearances must be retained.")
        }
        var invalidAppearance = defaults
        invalidAppearance.appearance = "invalid"
        expect(invalidAppearance.sanitized().appearance == "Dark", "Invalid appearance must fall back to Dark.")

        do {
            let encoded = try JSONEncoder().encode(defaults)
            let decoded = try JSONDecoder().decode(AppSettings.self, from: encoded)
            expect(decoded == defaults, "Settings persistence must round-trip without changing values.")
        } catch {
            failures.append("Settings round-trip failed: \(error)")
        }

        let primary = NSRect(x: 0, y: 0, width: 1440, height: 900)
        let secondary = NSRect(x: -1920, y: 100, width: 1920, height: 1080)
        let ordinaryFrame = NSRect(x: 900, y: 180, width: 400, height: 650)
        expect(WindowGeometry.restored(ordinaryFrame, screens: [primary]) == ordinaryFrame, "An on-screen saved frame must retain its size and position.")
        let offscreen = NSRect(x: -100_000, y: 100_000, width: 400, height: 650)
        expect(primary.contains(WindowGeometry.restored(offscreen, screens: [primary])), "A disconnected-screen frame must return entirely to a visible screen.")
        let secondScreenFrame = NSRect(x: -1000, y: 200, width: 400, height: 650)
        expect(WindowGeometry.restored(secondScreenFrame, screens: [primary, secondary]) == secondScreenFrame, "Negative-origin secondary display coordinates must be preserved.")
        expect(primary.contains(WindowGeometry.restored(secondScreenFrame, screens: [primary])), "Removing the secondary display must recover its saved frame.")
        let spanningFrame = NSRect(x: -100, y: 100, width: 500, height: 650)
        expect(primary.contains(WindowGeometry.restored(spanningFrame, screens: [primary, secondary])), "A frame spanning screens must move into the screen with the largest overlap.")
        let oversizeFrame = NSRect(x: -200, y: -100, width: 4000, height: 3000)
        let fittedOversize = WindowGeometry.restored(oversizeFrame, screens: [primary])
        expect(fittedOversize == primary, "Oversized saved frames must shrink to fit the current screen.")
        let tinyFrame = WindowGeometry.restored(NSRect(x: 1300, y: 800, width: 1, height: 1), screens: [primary])
        expect(tinyFrame.size == NSSize(width: 350, height: 280) && primary.contains(tinyFrame), "Small saved frames must respect the minimum size without leaving the screen.")
        let smallScreen = NSRect(x: 0, y: 0, width: 240, height: 200)
        expect(WindowGeometry.restored(ordinaryFrame, screens: [smallScreen]) == smallScreen, "Screens smaller than the panel minimum must remain usable.")
        for invalidFrame in [
            NSRect(x: Double.nan, y: 0, width: 400, height: 600),
            NSRect(x: 0, y: Double.infinity, width: 400, height: 600),
            NSRect(x: 0, y: 0, width: 0, height: 600),
            NSRect(x: 0, y: 0, width: -400, height: 600),
            NSRect(x: 0, y: 0, width: 400, height: -600)
        ] {
            let restored = WindowGeometry.restored(invalidFrame, screens: [primary])
            expect(primary.contains(restored) && restored.width >= 350 && restored.height >= 280,
                   "Corrupt frame persistence must recover to a finite, visible panel.")
        }
        expect(WindowGeometry.restored(ordinaryFrame, screens: []) == ordinaryFrame, "A temporary lack of displays must not overwrite a valid saved frame.")

        let migratedSquare = WindowGeometry.squareRestored(ordinaryFrame, screens: [primary])
        expect(migratedSquare.width == 400 && migratedSquare.height == 400, "A legacy 400-by-650 panel must migrate to a 400-point square.")
        expect(migratedSquare.minX == ordinaryFrame.minX && migratedSquare.maxY == ordinaryFrame.maxY, "Square migration must preserve the saved left edge and top edge when they fit.")
        let existingSquare = NSRect(x: 500, y: 200, width: 480, height: 480)
        expect(WindowGeometry.squareRestored(existingSquare, screens: [primary]) == existingSquare, "An on-screen square must retain its saved position and size.")
        let secondarySquare = WindowGeometry.squareRestored(secondScreenFrame, screens: [primary, secondary])
        expect(secondary.contains(secondarySquare) && secondarySquare.width == secondarySquare.height,
               "Square panels must support a secondary display with negative coordinates.")
        expect(secondarySquare.minX == secondScreenFrame.minX && secondarySquare.maxY == secondScreenFrame.maxY,
               "Migration on a secondary display must retain the saved top and left edges.")
        let recoveredSquare = WindowGeometry.squareRestored(secondScreenFrame, screens: [primary])
        expect(primary.contains(recoveredSquare) && recoveredSquare.width == recoveredSquare.height,
               "A square on a disconnected display must return entirely to the current screen.")
        let spanningSquare = WindowGeometry.squareRestored(spanningFrame, screens: [primary, secondary])
        expect(primary.contains(spanningSquare) && spanningSquare.width == 500 && spanningSquare.height == 500,
               "A square spanning displays must fit the display with the largest overlap.")
        let offscreenSquare = WindowGeometry.squareRestored(offscreen, screens: [primary, secondary])
        expect([primary, secondary].contains { $0.contains(offscreenSquare) } && offscreenSquare.width == offscreenSquare.height,
               "An entirely off-screen square must recover onto an available display.")
        let minimumSquare = WindowGeometry.squareRestored(NSRect(x: 1300, y: 800, width: 1, height: 1), screens: [primary])
        expect(minimumSquare.size == NSSize(width: 350, height: 350) && primary.contains(minimumSquare),
               "Square restoration must enforce the 350-point minimum while staying on-screen.")
        let customMinimumSquare = WindowGeometry.squareRestored(ordinaryFrame, screens: [primary], minimumSide: 450)
        expect(customMinimumSquare.size == NSSize(width: 450, height: 450) && customMinimumSquare.maxY == ordinaryFrame.maxY,
               "A supplied square minimum must preserve the saved top edge when the enlarged panel fits.")
        let oversizedSquare = WindowGeometry.squareRestored(oversizeFrame, screens: [primary])
        expect(oversizedSquare.width == 900 && oversizedSquare.height == 900 && primary.contains(oversizedSquare),
               "An oversized square must clamp to the shorter dimension of its display.")
        let smallDisplaySquare = WindowGeometry.squareRestored(ordinaryFrame, screens: [smallScreen])
        expect(smallDisplaySquare.width == 200 && smallDisplaySquare.height == 200 && smallScreen.contains(smallDisplaySquare),
               "A display smaller than the minimum must still contain the complete square panel.")
        for invalidFrame in [
            NSRect(x: Double.nan, y: 0, width: 400, height: 600),
            NSRect(x: 0, y: Double.infinity, width: 400, height: 600),
            NSRect(x: 0, y: 0, width: 0, height: 600),
            NSRect(x: 0, y: 0, width: -400, height: 600),
            NSRect(x: 0, y: 0, width: 400, height: -600)
        ] {
            let restored = WindowGeometry.squareRestored(invalidFrame, screens: [primary])
            expect(primary.contains(restored) && restored.width == restored.height && restored.width >= 350,
                   "Corrupt saved geometry must recover to a visible square with a usable size.")
        }
        let squareWithoutDisplays = WindowGeometry.squareRestored(ordinaryFrame, screens: [])
        expect(squareWithoutDisplays.width == 400 && squareWithoutDisplays.height == 400 && squareWithoutDisplays.maxY == ordinaryFrame.maxY,
               "Temporary lack of displays must still preserve square migration and the saved top edge.")

        guard failures.isEmpty else {
            for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
            exit(1)
        }
        print("PASS: \(checks) model checks")
    }
}
