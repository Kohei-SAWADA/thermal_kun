import Foundation

enum ReadingStatus: String, Codable, Sendable {
    case available, unavailable, unsupported
    var label: String { rawValue.capitalized }
}

struct MetricReading: Codable, Sendable {
    var value: Double?
    var status: ReadingStatus
    var source: String
    var detail: String
}

struct HardwareSnapshot: Codable, Sendable {
    var timestamp: Date
    var cpuTemperature: MetricReading
    var cpuUsage: MetricReading
    var gpuUsage: MetricReading
    var memoryUsed: MetricReading
    var memoryTotal: Double
    var memoryPressure: MetricReading
    var thermalState: String
    var thermalStateSource: String
}

struct TemperaturePoint: Sendable {
    var timestamp: Date
    var cpu: Double?
}

struct AppSettings: Codable, Equatable {
    var compact = false
    var alwaysOnTop = false
    var updateInterval = 2.0
    var animations = true
    var appearance = "Dark"

    func sanitized() -> AppSettings {
        var result = self
        result.updateInterval = [1.0, 2, 5, 10].contains(updateInterval) ? updateInterval : 2
        result.appearance = ["Dark", "Light", "System"].contains(appearance) ? appearance : "Dark"
        return result
    }
}

struct MonitorHistory {
    static let capacity = 180
    private(set) var points: [TemperaturePoint] = []
    private(set) var cpuPeak: Double?

    mutating func append(_ snapshot: HardwareSnapshot) {
        let cpu = Self.validTemperature(snapshot.cpuTemperature)
        if let cpu { cpuPeak = max(cpuPeak ?? cpu, cpu) }
        points.append(TemperaturePoint(timestamp: snapshot.timestamp, cpu: cpu))
        if points.count > Self.capacity { points.removeFirst(points.count - Self.capacity) }
    }

    mutating func resetPeaks() { cpuPeak = nil }

    static func validTemperature(_ reading: MetricReading) -> Double? {
        guard reading.status == .available, let value = reading.value, value.isFinite else { return nil }
        return value
    }
}
