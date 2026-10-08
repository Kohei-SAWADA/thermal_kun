import AppKit
import ServiceManagement
import SwiftUI

@MainActor
final class LoginService: ObservableObject {
    @Published var enabled = false
    @Published var message = ""
    func refresh() {
        enabled = SMAppService.mainApp.status == .enabled || SMAppService.mainApp.status == .requiresApproval
        if SMAppService.mainApp.status == .requiresApproval {
            message = "Approval required in System Settings > General > Login Items."
        } else if SMAppService.mainApp.status == .notFound {
            message = "Keep this app in Applications before enabling Launch at Login."
        } else { message = "" }
    }
    func setEnabled(_ value: Bool) {
        do {
            if value { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
            refresh()
        } catch {
            refresh()
            message = "Could not update Launch at Login. Move the app to Applications and try again."
        }
    }
}

struct SettingsView: View {
    @ObservedObject var state: AppState
    @ObservedObject var login: LoginService
    var centerPanel: () -> Void
    @State private var sourceExpanded = false
    @Environment(\.colorScheme) private var colorScheme
    private var accent: Color {
        colorScheme == .light ? Color(red: 0.47, green: 0.18, blue: 0.77) : Color(red: 0.72, green: 0.40, blue: 1.0)
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack {
                Image(systemName: "slider.horizontal.3").foregroundStyle(accent)
                Text("thermal kun").font(.system(size: 21, weight: .bold, design: .monospaced))
                Spacer()
                Text("SETTINGS").font(.system(size: 10, weight: .semibold, design: .monospaced)).foregroundStyle(.secondary)
            }
            Form {
                Section("Panel") {
                    Toggle("Always on Top", isOn: $state.settings.alwaysOnTop)
                        .help("Display the panel above normal windows. Disabled: keep it on the desktop.")
                    Toggle("Compact View", isOn: $state.settings.compact)
                    Picker("Appearance", selection: $state.settings.appearance) {
                        Text("Dark").tag("Dark"); Text("Light").tag("Light"); Text("System").tag("System")
                    }
                    Button("Center Panel on Screen", action: centerPanel)
                }
                Section("Monitoring") {
                    Picker("Update Interval", selection: $state.settings.updateInterval) {
                        ForEach([1.0, 2.0, 5.0, 10.0], id: \.self) { Text($0 == 1 ? "1 second" : "\(Int($0)) seconds").tag($0) }
                    }
                    Text("CPU temperature history: up to 180 samples. The peak resets when the app restarts or you select Reset.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Section("Startup & Motion") {
                    Toggle("Launch at Login", isOn: Binding(get: { login.enabled }, set: { login.setEnabled($0) }))
                    if !login.message.isEmpty {
                        Text(login.message).font(.caption).foregroundStyle(.secondary)
                        if SMAppService.mainApp.status == .requiresApproval {
                            Button("Open Login Items Settings") { SMAppService.openSystemSettingsLoginItems() }
                        }
                    }
                    Toggle("Enable Animations", isOn: $state.settings.animations)
                    Text("Animations also respect the system Reduce Motion setting.").font(.caption).foregroundStyle(.secondary)
                }
                Section("Measurement Sources") {
                    Text("Read-only monitoring. No administrator access or system setting changes.").font(.caption)
                    Text("Thermal State is provided by ProcessInfo. It describes system thermal conditions, not a measured performance loss. CPU temperature is a separate measurement; temperature alone does not confirm thermal throttling.")
                        .font(.caption).foregroundStyle(.secondary)
                    DisclosureGroup("Current Sources", isExpanded: $sourceExpanded) {
                        if let snapshot = state.snapshot {
                            source("CPU Temperature", snapshot.cpuTemperature)
                            source("CPU Usage", snapshot.cpuUsage)
                            source("GPU Usage", snapshot.gpuUsage)
                            source("Memory Used", snapshot.memoryUsed)
                            source("Memory Pressure", snapshot.memoryPressure)
                            Text("Thermal State: \(snapshot.thermalStateSource)").font(.caption)
                        } else { Text("Waiting for the first sample.").font(.caption) }
                    }
                }
            }.formStyle(.grouped)
        }
        .padding(20)
        .frame(minWidth: 480, idealWidth: 520, minHeight: 580, idealHeight: 690)
        .tint(accent)
        .environment(\.locale, Locale(identifier: "en_US"))
        .onAppear { login.refresh() }
    }
    private func source(_ title: String, _ reading: MetricReading) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text("\(title) · \(reading.status.label)").font(.system(size: 11, weight: .semibold, design: .monospaced))
            Text(reading.source + "\n" + reading.detail).font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
        }.padding(.vertical, 3)
    }
}
