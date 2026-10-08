import Foundation
import Darwin
import IOKit
import SMCBridge

/// このインスタンスの生成とsample()は同じ直列バックグラウンドキューで行う。
final class HardwareMonitor: @unchecked Sendable {
    private struct Sensor {
        let key: String
        let name: String
    }

    private struct CPUTicks {
        var user: UInt64 = 0
        var system: UInt64 = 0
        var idle: UInt64 = 0
        var nice: UInt64 = 0
        var uptime: TimeInterval = 0
    }

    private var smc: OpaquePointer?
    private var smcOpenResult: Int32 = 0
    private var lastReconnect: TimeInterval = 0
    private var previousTicks: CPUTicks?
    private let cpuSensors: [Sensor]
    private let chipName: String

    init() {
        chipName = Self.sysctlString("machdep.cpu.brand_string") ?? "Unknown CPU"
        #if arch(arm64)
        let isAppleSilicon = true
        #else
        let isAppleSilicon = false
        #endif
        cpuSensors = Self.cpuSensorMapping(chipName: chipName, appleSilicon: isAppleSilicon)
        smc = TKSMCOpen(&smcOpenResult)
        lastReconnect = ProcessInfo.processInfo.systemUptime
        previousTicks = Self.readTicks()
    }

    deinit { TKSMCClose(smc) }

    func sample() -> HardwareSnapshot {
        var cpuTemperature = readCPUTemperature()
        let uptime = ProcessInfo.processInfo.systemUptime
        // スリープ復帰やドライバ再接続で無効になったハンドルを低頻度で再取得する。
        if cpuTemperature.status != .available && uptime - lastReconnect > 30 {
            TKSMCClose(smc)
            smc = TKSMCOpen(&smcOpenResult)
            lastReconnect = uptime
            cpuTemperature = readCPUTemperature()
        }
        let memory = readMemory()
        return HardwareSnapshot(
            timestamp: Date(),
            cpuTemperature: cpuTemperature,
            cpuUsage: readCPUUsage(),
            gpuUsage: readGPUUsage(),
            memoryUsed: memory.used,
            memoryTotal: memory.total,
            memoryPressure: readMemoryPressure(),
            thermalState: Self.thermalStateName(ProcessInfo.processInfo.thermalState),
            thermalStateSource: "ProcessInfo.thermalState (Foundation public API)")
    }

    private func readCPUTemperature() -> MetricReading {
        guard !cpuSensors.isEmpty else {
            return MetricReading(value: nil, status: .unsupported, source: "AppleSMC",
                detail: "No verified CPU temperature sensor mapping is included for \(chipName). Unknown sensor keys are not guessed.")
        }
        guard smc != nil else {
            return MetricReading(value: nil, status: .unavailable, source: "AppleSMC",
                detail: "Read-only AppleSMC connection failed (\(Self.errorCode(smcOpenResult))). No administrator permission is requested.")
        }
        let readings: [(Sensor, Double)] = cpuSensors.compactMap { sensor in
            guard let value = smcValue(sensor.key), value > 0, value < 150 else { return nil }
            return (sensor, value)
        }
        guard !readings.isEmpty else {
            return MetricReading(value: nil, status: .unavailable, source: "AppleSMC",
                detail: "None of the mapped CPU temperature keys returned a valid measurement: \(cpuSensors.map(\.key).joined(separator: ", ")).")
        }
        let mean = readings.map(\.1).reduce(0, +) / Double(readings.count)
        let measurements = readings.map { sensor, value in
            "\(sensor.key) (\(sensor.name)): \(String(format: "%.1f", value)) °C"
        }.joined(separator: "; ")
        return MetricReading(value: mean, status: .available,
            source: "AppleSMC · CPU sensor mean",
            detail: "Arithmetic mean of \(readings.count) readable mapped CPU sensors. Sensor zones do not necessarily correspond to individual cores. \(measurements). Read-only undocumented driver interface; mappings are based on Stats platform research. Temperature alone does not establish throttling.")
    }

    private func readCPUUsage() -> MetricReading {
        let source = "Mach host_statistics · CPU tick delta"
        guard let current = Self.readTicks() else {
            previousTicks = nil
            return MetricReading(value: nil, status: .unavailable, source: source,
                detail: "The system CPU tick counters could not be read.")
        }
        defer { previousTicks = current }
        guard let previous = previousTicks, current.uptime - previous.uptime >= 0.25 else {
            return MetricReading(value: nil, status: .unavailable, source: source,
                detail: "Collecting the first CPU measurement interval.")
        }
        // Machのnatural_tカウンタの巻き戻りを正常な0%として扱わない。
        guard current.user >= previous.user, current.system >= previous.system,
              current.idle >= previous.idle, current.nice >= previous.nice,
              current.uptime - previous.uptime < 90 else {
            return MetricReading(value: nil, status: .unavailable, source: source,
                detail: "CPU counters restarted or the sample interval was interrupted. A new baseline has been collected.")
        }
        let active = (current.user - previous.user) + (current.system - previous.system) + (current.nice - previous.nice)
        let total = active + (current.idle - previous.idle)
        guard total > 0 else {
            return MetricReading(value: nil, status: .unavailable, source: source,
                detail: "No CPU ticks advanced during this measurement interval.")
        }
        return MetricReading(value: Double(active) / Double(total) * 100, status: .available, source: source,
            detail: "System-wide busy CPU ticks divided by total CPU ticks between consecutive samples, normalized across all logical CPUs.")
    }

    private func readMemory() -> (used: MetricReading, total: Double) {
        var usage = TKMemoryUsage()
        let result = TKReadMemoryUsage(&usage)
        guard result == 0, usage.total > 0 else {
            return (MetricReading(value: nil, status: .unavailable,
                source: "Mach host_statistics64",
                detail: "The memory counters could not be read (\(Self.errorCode(result)))."),
                Double(ProcessInfo.processInfo.physicalMemory))
        }
        return (MetricReading(value: Double(usage.used), status: .available,
            source: "Mach VM counters · used memory estimate",
            detail: "Estimated physical memory in use: (active + inactive + speculative + wired + compressor - purgeable - file-backed) pages × page size. Compressed physical footprint is included; reclaimable file cache is excluded. This estimate can differ from Activity Monitor. Total RAM: hw.memsize."), Double(usage.total))
    }

    private func readMemoryPressure() -> MetricReading {
        var raw: UInt32 = 0
        var size = MemoryLayout<UInt32>.size
        let result = sysctlbyname("kern.memorystatus_vm_pressure_level", &raw, &size, nil, 0)
        let source = "XNU kern.memorystatus_vm_pressure_level"
        guard result == 0 else {
            return MetricReading(value: nil, status: .unavailable, source: source,
                detail: "The system memory-pressure state could not be read. Memory usage is not substituted for pressure.")
        }
        let level: Double
        switch raw {
        case 1: level = 0
        case 2: level = 1
        case 4: level = 2
        default:
            return MetricReading(value: nil, status: .unavailable, source: source,
                detail: "The system returned an unrecognized memory-pressure flag (\(raw)).")
        }
        return MetricReading(value: level, status: .available, source: source,
            detail: "Kernel dispatch pressure flags: 1 = Normal, 2 = Warning, 4 = Critical. This is a discrete OS state, not a RAM-use percentage. The read-only sysctl is published in Apple's XNU source but is not a promised stable public API.")
    }

    private func readGPUUsage() -> MetricReading {
        var iterator: io_iterator_t = 0
        let source = "IORegistry · IOAccelerator PerformanceStatistics"
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOAccelerator"), &iterator) == KERN_SUCCESS else {
            return MetricReading(value: nil, status: .unavailable, source: source,
                detail: "GPU accelerator services could not be enumerated.")
        }
        defer { IOObjectRelease(iterator) }
        var readings: [(Double, String)] = []
        var service = IOIteratorNext(iterator)
        var foundService = false
        while service != 0 {
            foundService = true
            if let property = IORegistryEntryCreateCFProperty(service, "PerformanceStatistics" as CFString,
                kCFAllocatorDefault, 0)?.takeRetainedValue(),
               let statistics = property as? [String: Any] {
                // Device utilization is preferred. A renderer-only counter is never relabeled as overall GPU use.
                for key in ["Device Utilization %", "GPU Activity(%)"] {
                    if let number = statistics[key] as? NSNumber {
                        let value = number.doubleValue
                        if value.isFinite, (0...100).contains(value) {
                            readings.append((value, key))
                            break
                        }
                    }
                }
            }
            IOObjectRelease(service)
            service = IOIteratorNext(iterator)
        }
        guard let maximum = readings.max(by: { $0.0 < $1.0 }) else {
            return MetricReading(value: nil, status: foundService ? .unavailable : .unsupported, source: source,
                detail: "No GPU device utilization counter is published by this driver. A missing counter is not reported as 0%.")
        }
        return MetricReading(value: maximum.0, status: .available, source: source,
            detail: "Driver-reported \(maximum.1). Maximum across \(readings.count) readable accelerator(s). This driver counter has its own sampling window and is not inferred from CPU activity. IORegistry statistics are driver-specific and may change across OS versions.")
    }

    private func smcValue(_ key: String) -> Double? {
        guard let smc else { return nil }
        var value: Double = 0
        let result = key.withCString { TKSMCReadValue(smc, $0, &value) }
        return result == 0 && value.isFinite ? value : nil
    }

    private static func readTicks() -> CPUTicks? {
        var ticks = CPUTicks()
        guard TKReadCPUTicks(&ticks.user, &ticks.system, &ticks.idle, &ticks.nice) == 0 else { return nil }
        ticks.uptime = ProcessInfo.processInfo.systemUptime
        return ticks
    }

    private static func sysctlString(_ name: String) -> String? {
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0, size < 4096 else { return nil }
        var bytes = [CChar](repeating: 0, count: size)
        guard sysctlbyname(name, &bytes, &size, nil, 0) == 0 else { return nil }
        return String(cString: bytes)
    }

    private static func errorCode(_ value: Int32) -> String {
        String(format: "0x%08x", UInt32(bitPattern: value))
    }

    private static func thermalStateName(_ state: ProcessInfo.ThermalState) -> String {
        switch state {
        case .nominal: return "Nominal"
        case .fair: return "Fair"
        case .serious: return "Serious"
        case .critical: return "Critical"
        @unknown default: return "Unavailable"
        }
    }

    private static func cpuSensorMapping(chipName: String, appleSilicon: Bool) -> [Sensor] {
        func sensors(_ keys: [String], _ label: String) -> [Sensor] {
            keys.enumerated().map { Sensor(key: $0.element, name: "\(label) zone \($0.offset + 1)") }
        }
        if !appleSilicon {
            return sensors(["TC0D", "TC0E", "TC0F", "TC0P"], "CPU")
        }
        // プラットフォームごとに意味が異なるため、未知のSoCへキーを流用しない。
        if chipName.contains("Apple M3") {
            return sensors(["Te05", "Te0L", "Te0P", "Te0S"], "CPU efficiency") +
                    sensors(["Tf04", "Tf09", "Tf0A", "Tf0B", "Tf0D", "Tf0E", "Tf44", "Tf49", "Tf4A", "Tf4B", "Tf4D", "Tf4E"], "CPU performance")
        }
        if chipName.contains("Apple M2") {
            return sensors(["Tp1h", "Tp1t", "Tp1p", "Tp1l"], "CPU efficiency") +
                    sensors(["Tp01", "Tp05", "Tp09", "Tp0D", "Tp0X", "Tp0b", "Tp0f", "Tp0j"], "CPU performance")
        }
        if chipName.contains("Apple M1") {
            return sensors(["Tp09", "Tp0T"], "CPU efficiency") +
                    sensors(["Tp01", "Tp05", "Tp0D", "Tp0H", "Tp0L", "Tp0P", "Tp0X", "Tp0b"], "CPU performance")
        }
        return []
    }
}
