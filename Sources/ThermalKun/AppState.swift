import AppKit
import Combine
import Foundation

private final class BackgroundSampler: @unchecked Sendable {
    // sample() is called only on the serial monitoring queue.
    private lazy var hardware = HardwareMonitor()
    func sample() -> HardwareSnapshot { hardware.sample() }
}

@MainActor
final class AppState: ObservableObject {
    @Published var snapshot: HardwareSnapshot?
    @Published var history: [TemperaturePoint] = []
    @Published var cpuPeak: Double?
    @Published var isStale = false
    @Published var settings: AppSettings {
        didSet {
            let validated = settings.sanitized()
            if validated != settings { settings = validated }
            if let data = try? JSONEncoder().encode(settings) { defaults.set(data, forKey: "settings") }
            if oldValue.updateInterval != settings.updateInterval { restartTimer() }
            settingsDidChange?(oldValue, settings)
        }
    }
    var settingsDidChange: ((AppSettings, AppSettings) -> Void)?
    private let defaults: UserDefaults
    private let worker = DispatchQueue(label: "app.thermalkun.monitor", qos: .utility)
    private let monitor = BackgroundSampler()
    private var samples = MonitorHistory()
    private var timer: Timer?
    private var inFlight = false
    private var sleeping = false
    private var generation = 0
    private var observers: [NSObjectProtocol] = []

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        if let data = defaults.data(forKey: "settings"), let saved = try? JSONDecoder().decode(AppSettings.self, from: data) {
            settings = saved.sanitized()
        } else { settings = AppSettings() }
    }

    func start() {
        let center = NSWorkspace.shared.notificationCenter
        observers.append(center.addObserver(forName: NSWorkspace.willSleepNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in
                self?.sleeping = true; self?.generation += 1
                self?.timer?.invalidate(); self?.isStale = true
            }
        })
        observers.append(center.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in
                self?.sleeping = false; self?.restartTimer(); self?.poll()
            }
        })
        restartTimer()
        poll()
    }

    func stop() {
        timer?.invalidate()
        observers.forEach { NSWorkspace.shared.notificationCenter.removeObserver($0) }
        observers.removeAll()
    }

    private func restartTimer() {
        timer?.invalidate()
        guard !sleeping else { return }
        let timer = Timer(timeInterval: settings.updateInterval, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.poll() }
        }
        timer.tolerance = settings.updateInterval * 0.15
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }

    private func poll() {
        if let snapshot { isStale = Date().timeIntervalSince(snapshot.timestamp) > max(6, settings.updateInterval * 3) }
        guard !sleeping, !inFlight else { return }
        inFlight = true
        let expectedGeneration = generation
        worker.async { [weak self, monitor] in
            let measured = monitor.sample()
            DispatchQueue.main.async {
                guard let self else { return }
                self.inFlight = false
                guard !self.sleeping, self.generation == expectedGeneration else { return }
                self.snapshot = measured
                self.samples.append(measured)
                self.history = self.samples.points
                self.cpuPeak = self.samples.cpuPeak
                self.isStale = false
            }
        }
    }

    func resetPeaks() {
        samples.resetPeaks()
        cpuPeak = nil
    }

    func toggleCompact() { settings.compact.toggle() }
}
