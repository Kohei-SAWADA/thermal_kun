import AppKit
import SwiftUI

struct PanelView: View {
    @ObservedObject var state: AppState
    let onSettings: () -> Void
    let onHide: () -> Void

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var appeared = false

    private var theme: InstrumentTheme {
        let light = state.settings.appearance == "Light" || (state.settings.appearance == "System" && colorScheme == .light)
        return InstrumentTheme(light: light)
    }
    private var thermalState: String { state.snapshot?.thermalState ?? "Unavailable" }
    private var animation: Animation? { state.settings.animations && !reduceMotion ? .easeOut(duration: 0.25) : nil }

    var body: some View {
        GeometryReader { geometry in
            panelContent(chartHeight: min(260, max(48, geometry.size.height - 380)))
        }
        .foregroundStyle(theme.text)
        .background(theme.background)
        .overlay(alignment: .topLeading) {
            Rectangle().fill(theme.support).frame(width: 82, height: 3).padding(.leading, 25)
        }
        .overlay { Rectangle().stroke(theme.rule, lineWidth: 1) }
        .overlay(alignment: .bottomTrailing) {
            ResizeMark().stroke(theme.support.opacity(0.6), lineWidth: 1)
                .frame(width: 10, height: 10).padding(6).allowsHitTesting(false)
        }
        .animation(animation, value: appeared)
        .animation(animation, value: thermalState)
        .onAppear { appeared = true }
    }

    private func panelContent(chartHeight: CGFloat) -> some View {
        VStack(spacing: 0) {
            header.padding(.horizontal, 15).padding(.top, 10).padding(.bottom, 9)
                .background(theme.header)
            Rectangle().fill(theme.rule).frame(height: 1).padding(.horizontal, 15)
            ScrollView(.vertical) {
                VStack(spacing: 8) {
                    temperatureSection
                    thermalSection
                    loadSection
                    if !state.settings.compact {
                        historySection(chartHeight: chartHeight)
                        peakSection
                    }
                }
                .padding(.horizontal, 15)
                .padding(.vertical, 10)
            }
            .scrollIndicators(state.settings.compact ? .never : .automatic)
            footer.padding(.horizontal, 15).padding(.bottom, 12).padding(.top, 5)
        }
    }

    private var header: some View {
        HStack(spacing: 9) {
            WindowDragRegion {
                HStack(spacing: 9) {
                    ThermometerGlyph().trim(from: 0, to: appeared ? 1 : 0.1)
                        .stroke(theme.accent, style: StrokeStyle(lineWidth: 1.5, lineCap: .round, lineJoin: .round))
                        .frame(width: 22, height: 26).accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 2) {
                        Text("thermal kun").font(.system(size: 15, weight: .bold, design: .monospaced)).tracking(0.5).foregroundStyle(theme.support)
                        Text("SYSTEM TELEMETRY").font(.system(size: 8, weight: .semibold, design: .monospaced)).tracking(1.8).foregroundStyle(theme.secondary)
                    }
                    Spacer(minLength: 0)
                }
            }
            .frame(maxWidth: .infinity)
            .frame(height: 32)
            .help("Drag here to move the panel")
            panelButton(state.settings.compact ? "arrow.up.left.and.arrow.down.right" : "arrow.down.right.and.arrow.up.left", label: state.settings.compact ? "Expand details" : "Compact view", action: state.toggleCompact)
            panelButton("slider.horizontal.3", label: "Settings", action: onSettings)
            panelButton("minus", label: "Hide panel", action: onHide)
        }
    }

    private var temperatureSection: some View {
        VStack(spacing: 7) {
            HStack(alignment: .center, spacing: 10) {
                VStack(alignment: .leading, spacing: 7) {
                    sectionLabel("01", "CPU TEMP", accent: theme.accent)
                    if let reading = state.snapshot?.cpuTemperature, reading.source.lowercased().contains("smc"), reading.source.lowercased().contains("mean") {
                        Text(reading.detail.contains("CPU efficiency") && !reading.detail.contains("CPU performance") ? "SMC · E-ZONE MEAN" : "SMC · SENSOR MEAN")
                            .font(.system(size: 8, weight: .medium, design: .monospaced))
                            .tracking(0.4).foregroundStyle(theme.secondary)
                            .lineLimit(1).minimumScaleFactor(0.8)
                    }
                }
                Spacer(minLength: 0)
                HStack(alignment: .firstTextBaseline, spacing: 4) {
                    if let value = availableValue(state.snapshot?.cpuTemperature) {
                        Text(String(format: "%.1f", value))
                            .font(.system(size: 48, weight: .light, design: .monospaced)).tracking(-2)
                            .foregroundStyle(theme.accent)
                        Text("°C").font(.system(size: 15, weight: .medium, design: .monospaced)).foregroundStyle(theme.secondary)
                    } else {
                        Text(unavailableLabel(state.snapshot?.cpuTemperature))
                            .font(.system(size: 15, weight: .medium, design: .monospaced))
                            .foregroundStyle(theme.secondary).padding(.vertical, 14)
                    }
                }
                .lineLimit(1).minimumScaleFactor(0.7)
            }
            Rectangle().fill(theme.accent.opacity(0.7)).frame(height: 2)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .help(readingHelp(state.snapshot?.cpuTemperature))
        .accessibilityElement(children: .combine)
    }

    private var thermalSection: some View {
        HStack(spacing: 10) {
            Rectangle().fill(thermalColor).frame(width: 3)
            VStack(alignment: .leading, spacing: 4) {
                sectionLabel("02", "THERMAL STATUS", accent: thermalColor)
                Text(thermalState.uppercased())
                    .font(.system(size: state.settings.compact ? 22 : 25, weight: .bold, design: .monospaced))
                    .tracking(1).foregroundStyle(thermalColor).lineLimit(1).minimumScaleFactor(0.65)
                Text("THERMAL STATE · PROCESSINFO")
                    .font(.system(size: 8, weight: .medium, design: .monospaced)).tracking(0.6).foregroundStyle(theme.secondary)
            }
            Spacer(minLength: 0)
            ThermalSignalShape().stroke(thermalColor.opacity(0.8), lineWidth: 1.5).frame(width: 36, height: 31)
                .accessibilityHidden(true)
        }
        .padding(9)
        .background(thermalColor.opacity(theme.light ? 0.07 : 0.055))
        .overlay { Rectangle().stroke(thermalColor.opacity(0.4), lineWidth: 1) }
        .help("Source: \(state.snapshot?.thermalStateSource ?? "ProcessInfo.thermalState")\nThermal State describes system thermal conditions. It is not a percentage of performance loss. CPU temperature is a separate measurement and does not confirm thermal throttling by itself.")
    }

    private var loadSection: some View {
        HStack(alignment: .top, spacing: state.settings.compact ? 14 : 11) {
            loadMetric("CPU LOAD", reading: state.snapshot?.cpuUsage, accent: theme.accent)
            if !state.settings.compact {
                loadMetric("GPU LOAD", reading: state.snapshot?.gpuUsage, accent: theme.support)
            }
            VStack(alignment: .leading, spacing: 5) {
                HStack(spacing: 3) {
                    compactLabel("MEMORY")
                    Spacer(minLength: 0)
                    Text(memoryPressureText).font(.system(size: 8, weight: .semibold, design: .monospaced)).foregroundStyle(memoryPressureColor).lineLimit(1)
                }
                Text(memoryText).font(.system(size: 17, weight: .medium, design: .monospaced))
                    .foregroundStyle(theme.support).lineLimit(1).minimumScaleFactor(0.7)
                utilizationBar(memoryFraction, accent: theme.support)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .help("\(readingHelp(state.snapshot?.memoryUsed))\nMemory pressure: \(readingHelp(state.snapshot?.memoryPressure))")
        }
    }

    private func loadMetric(_ label: String, reading: MetricReading?, accent: Color) -> some View {
        VStack(alignment: .leading, spacing: 5) {
            compactLabel(label)
            Text(readingText(reading, format: "%.1f", suffix: "%"))
                .font(.system(size: 17, weight: .medium, design: .monospaced))
                .foregroundStyle(accent).lineLimit(1).minimumScaleFactor(0.7)
            utilizationBar(availableValue(reading).map { $0 / 100 }, accent: accent)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .help(readingHelp(reading)).accessibilityElement(children: .combine)
    }

    private func historySection(chartHeight: CGFloat) -> some View {
        VStack(alignment: .leading, spacing: 7) {
            sectionLabel("03", "CPU TEMP HISTORY", accent: theme.support)
            TemperatureHistoryView(points: state.history, theme: theme).frame(height: chartHeight)
        }
    }

    private var peakSection: some View {
        HStack(spacing: 10) {
            compactLabel("CPU PEAK")
            Text(peakText(state.cpuPeak)).foregroundStyle(theme.accent)
            Spacer(minLength: 0)
            Button(action: state.resetPeaks) {
                HStack(spacing: 3) { Image(systemName: "arrow.counterclockwise"); Text("RESET") }
                    .font(.system(size: 9, weight: .semibold, design: .monospaced))
            }
            .buttonStyle(InstrumentButtonStyle(theme: theme))
            .help("Reset the peak CPU temperature measured since monitoring began or the previous reset")
            .accessibilityLabel("Reset peak CPU temperature")
        }
        .font(.system(size: 9, weight: .medium, design: .monospaced))
        .lineLimit(1).minimumScaleFactor(0.7)
    }

    private var footer: some View {
        HStack(spacing: 5) {
            Circle().fill(state.isStale ? theme.amber : (state.snapshot == nil ? theme.secondary : theme.accent)).frame(width: 4, height: 4)
            Text(state.isStale ? "STALE · UPDATES PAUSED" : (state.snapshot == nil ? "WAITING FOR DATA" : "LIVE · \(intervalText)s"))
                .foregroundStyle(state.isStale ? theme.amber : theme.secondary)
            Spacer(minLength: 1)
            if let timestamp = state.snapshot?.timestamp {
                Text(InstrumentTime.string(timestamp))
                    .foregroundStyle(theme.secondary)
            }
        }
        .font(.system(size: 8, weight: .medium, design: .monospaced)).tracking(0.6)
        .help(state.isStale ? "Displayed readings are from the last successful sample. Monitoring is paused or delayed." : "Last successful sample time. All values are measured; unavailable items are explicitly marked.")
    }

    private func panelButton(_ symbol: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) { Image(systemName: symbol).font(.system(size: 11, weight: .medium)).frame(width: 22, height: 24) }
            .buttonStyle(InstrumentButtonStyle(theme: theme))
            .help(label).accessibilityLabel(label)
    }

    private func sectionLabel(_ number: String, _ title: String, accent: Color) -> some View {
        HStack(spacing: 5) {
            Text(number).foregroundStyle(accent)
            Text(title).foregroundStyle(theme.secondary)
        }
        .font(.system(size: 9, weight: .semibold, design: .monospaced)).tracking(0.8)
    }

    private func compactLabel(_ label: String) -> some View {
        Text(label).font(.system(size: 9, weight: .semibold, design: .monospaced)).tracking(0.7).foregroundStyle(theme.secondary)
    }

    private func utilizationBar(_ fraction: Double?, accent: Color) -> some View {
        GeometryReader { proxy in
            ZStack(alignment: .leading) {
                Rectangle().fill(theme.rule)
                if let fraction { Rectangle().fill(accent.opacity(0.9)).frame(width: proxy.size.width * min(1, max(0, fraction))) }
            }
        }
        .frame(height: 2).accessibilityHidden(true)
    }

    private func availableValue(_ reading: MetricReading?) -> Double? {
        guard let reading, reading.status == .available, let value = reading.value, value.isFinite else { return nil }
        return value
    }
    private func unavailableLabel(_ reading: MetricReading?) -> String {
        reading?.status == .unsupported ? "Unsupported" : "Unavailable"
    }
    private func readingText(_ reading: MetricReading?, format: String, suffix: String) -> String {
        guard let value = availableValue(reading) else { return unavailableLabel(reading) }
        return String(format: format, value) + suffix
    }
    private func readingHelp(_ reading: MetricReading?) -> String {
        guard let reading else { return "Waiting for a measured sample." }
        return "Source: \(reading.source)\n\(reading.detail)"
    }
    private func peakText(_ value: Double?) -> String {
        guard let value, value.isFinite else { return "Unavailable" }
        return String(format: "%.1f°", value)
    }
    private var intervalText: String { String(format: "%g", state.settings.updateInterval) }
    private var memoryText: String {
        guard let bytes = availableValue(state.snapshot?.memoryUsed) else { return unavailableLabel(state.snapshot?.memoryUsed) }
        return String(format: "%.1f GiB", bytes / 1_073_741_824)
    }
    private var memoryFraction: Double? {
        guard let used = availableValue(state.snapshot?.memoryUsed), let total = state.snapshot?.memoryTotal, total > 0 else { return nil }
        return used / total
    }
    private var memoryPressureText: String {
        guard let pressure = availableValue(state.snapshot?.memoryPressure) else { return unavailableLabel(state.snapshot?.memoryPressure) }
        switch Int(pressure) {
        case 0: return "Normal"
        case 1: return "Warning"
        case 2: return "Critical"
        default: return "Unavailable"
        }
    }
    private var memoryPressureColor: Color {
        guard let pressure = availableValue(state.snapshot?.memoryPressure) else { return theme.secondary }
        if pressure >= 2 { return theme.red }
        if pressure >= 1 { return theme.amber }
        return theme.accent
    }
    private var thermalColor: Color {
        switch thermalState.lowercased() {
        case "nominal": return theme.accent
        case "fair": return theme.amber
        case "serious", "critical": return theme.red
        default: return theme.secondary
        }
    }
}

private struct TemperatureHistoryView: View {
    let points: [TemperaturePoint]
    let theme: InstrumentTheme
    @State private var hoveredIndex: Int?

    private var values: [Double] { points.compactMap(\.cpu).filter(\.isFinite) }
    private var lowerBound: Double { floor((values.min() ?? 20) / 10) * 10 }
    private var upperBound: Double { max(lowerBound + 10, ceil((values.max() ?? 30) / 10) * 10) }
    private var selected: TemperaturePoint? {
        guard let index = hoveredIndex, points.indices.contains(index) else { return nil }
        return points[index]
    }

    var body: some View {
        VStack(spacing: 3) {
            HStack(spacing: 5) {
                VStack(alignment: .trailing) {
                    Text(values.isEmpty ? "" : String(format: "%.0f°", upperBound))
                    Spacer()
                    Text(values.isEmpty ? "" : String(format: "%.0f°", lowerBound))
                }
                .font(.system(size: 8, weight: .regular, design: .monospaced)).foregroundStyle(theme.secondary).frame(width: 24)
                GeometryReader { proxy in
                    Canvas { context, size in
                        drawGrid(context: &context, size: size)
                        drawLine(context: &context, size: size, color: theme.accent)
                        if let index = hoveredIndex, points.indices.contains(index) {
                            let x = xPosition(points[index].timestamp, width: size.width)
                            var cursor = Path(); cursor.move(to: CGPoint(x: x, y: 0)); cursor.addLine(to: CGPoint(x: x, y: size.height))
                            context.stroke(cursor, with: .color(theme.secondary.opacity(0.6)), style: StrokeStyle(lineWidth: 1, dash: [2, 3]))
                        }
                    }
                    .onContinuousHover { phase in
                        switch phase {
                        case .active(let location):
                            guard !points.isEmpty else { return }
                            hoveredIndex = points.indices.min { abs(xPosition(points[$0].timestamp, width: proxy.size.width) - location.x) < abs(xPosition(points[$1].timestamp, width: proxy.size.width) - location.x) }
                        case .ended: hoveredIndex = nil
                        }
                    }
                    if values.isEmpty {
                        Text("Unavailable").font(.system(size: 11, weight: .medium, design: .monospaced)).foregroundStyle(theme.secondary)
                            .frame(maxWidth: .infinity, maxHeight: .infinity).allowsHitTesting(false)
                    }
                }
            }
            HStack(spacing: 6) {
                if let point = selected {
                    Text(InstrumentTime.string(point.timestamp))
                    Text("CPU \(sampleText(point.cpu))").foregroundStyle(theme.accent)
                } else {
                    Text(historyDuration)
                    Spacer()
                    Text("NOW")
                }
            }
            .font(.system(size: 8, weight: .medium, design: .monospaced)).foregroundStyle(theme.secondary)
            .frame(height: 12).padding(.leading, 29)
        }
        .accessibilityLabel("Recent CPU temperature history")
        .help("Hover over the graph to inspect recorded CPU temperatures. Missing measurements leave gaps. The scale follows the measured temperature range.")
    }

    private var historyDuration: String {
        guard let first = points.first?.timestamp, let last = points.last?.timestamp else { return "NO SAMPLES" }
        let seconds = max(0, last.timeIntervalSince(first))
        return seconds >= 60 ? String(format: "LAST %.1f MIN", seconds / 60) : String(format: "LAST %.0f SEC", seconds)
    }
    private func sampleText(_ value: Double?) -> String {
        guard let value, value.isFinite else { return "Unavailable" }
        return String(format: "%.1f°C", value)
    }
    private func xPosition(_ time: Date, width: CGFloat) -> CGFloat {
        guard let first = points.first?.timestamp, let last = points.last?.timestamp, last > first else { return width }
        return width * time.timeIntervalSince(first) / last.timeIntervalSince(first)
    }
    private func drawGrid(context: inout GraphicsContext, size: CGSize) {
        var grid = Path()
        for row in 0...2 {
            let y = size.height * Double(row) / 2
            grid.move(to: CGPoint(x: 0, y: y)); grid.addLine(to: CGPoint(x: size.width, y: y))
        }
        for column in 0...5 {
            let x = size.width * Double(column) / 5
            grid.move(to: CGPoint(x: x, y: 0)); grid.addLine(to: CGPoint(x: x, y: size.height))
        }
        context.stroke(grid, with: .color(theme.rule.opacity(0.65)), style: StrokeStyle(lineWidth: 0.5, dash: [2, 3]))
    }
    private func drawLine(context: inout GraphicsContext, size: CGSize, color: Color) {
        var path = Path()
        var connected = false
        var lastPoint: CGPoint?
        let lower = lowerBound
        let upper = upperBound
        for sample in points {
            guard let value = sample.cpu, value.isFinite else { connected = false; continue }
            let point = CGPoint(x: xPosition(sample.timestamp, width: size.width), y: size.height * (1 - (value - lower) / (upper - lower)))
            if connected { path.addLine(to: point) } else { path.move(to: point) }
            connected = true
            lastPoint = point
        }
        context.stroke(path, with: .color(color.opacity(0.13)), style: StrokeStyle(lineWidth: 4, lineCap: .round, lineJoin: .round))
        context.stroke(path, with: .color(color), style: StrokeStyle(lineWidth: 1.2, lineCap: .round, lineJoin: .round))
        if let point = lastPoint {
            context.fill(Path(ellipseIn: CGRect(x: point.x - 1.7, y: point.y - 1.7, width: 3.4, height: 3.4)), with: .color(color))
        }
    }
}

private struct InstrumentTheme {
    let light: Bool
    var background: Color { light ? Color(red: 0.94, green: 0.94, blue: 0.96) : Color(red: 0.055, green: 0.06, blue: 0.08) }
    var header: Color { light ? Color(red: 0.90, green: 0.88, blue: 0.95) : Color(red: 0.095, green: 0.075, blue: 0.14) }
    var surface: Color { light ? Color(red: 0.88, green: 0.88, blue: 0.92) : Color(red: 0.10, green: 0.105, blue: 0.14) }
    var surfaceHover: Color { light ? Color(red: 0.83, green: 0.79, blue: 0.91) : Color(red: 0.19, green: 0.12, blue: 0.27) }
    var text: Color { light ? Color(red: 0.10, green: 0.09, blue: 0.15) : Color(red: 0.90, green: 0.93, blue: 0.92) }
    var secondary: Color { light ? Color(red: 0.32, green: 0.33, blue: 0.40) : Color(red: 0.57, green: 0.62, blue: 0.66) }
    var rule: Color { light ? Color(red: 0.70, green: 0.70, blue: 0.78).opacity(0.65) : Color(red: 0.33, green: 0.36, blue: 0.43).opacity(0.55) }
    var accent: Color { light ? Color(red: 0.30, green: 0.42, blue: 0.015) : Color(red: 0.73, green: 0.98, blue: 0.22) }
    var support: Color { light ? Color(red: 0.47, green: 0.18, blue: 0.77) : Color(red: 0.72, green: 0.40, blue: 1.0) }
    var amber: Color { light ? Color(red: 0.57, green: 0.34, blue: 0.01) : Color(red: 1.0, green: 0.70, blue: 0.20) }
    var red: Color { light ? Color(red: 0.77, green: 0.12, blue: 0.17) : Color(red: 1.0, green: 0.29, blue: 0.34) }
}

private struct InstrumentButtonStyle: ButtonStyle {
    let theme: InstrumentTheme
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(configuration.isPressed ? theme.accent : theme.secondary)
            .padding(.horizontal, 3).padding(.vertical, 2)
            .background(configuration.isPressed ? theme.surfaceHover : theme.surface)
            .overlay { Rectangle().stroke(theme.rule, lineWidth: 0.6) }
            .contentShape(Rectangle())
    }
}

private struct ThermometerGlyph: Shape {
    func path(in rect: CGRect) -> Path {
        let centerX = rect.midX
        let radius = min(rect.width * 0.23, rect.height * 0.20)
        let stemRadius = radius * 0.38
        let bulbY = rect.maxY - radius - 1
        let topY = rect.minY + 1
        let joinY = bulbY - radius * 0.85
        var path = Path()
        path.move(to: CGPoint(x: centerX - stemRadius, y: topY + stemRadius))
        path.addQuadCurve(to: CGPoint(x: centerX, y: topY), control: CGPoint(x: centerX - stemRadius, y: topY))
        path.addQuadCurve(to: CGPoint(x: centerX + stemRadius, y: topY + stemRadius), control: CGPoint(x: centerX + stemRadius, y: topY))
        path.addLine(to: CGPoint(x: centerX + stemRadius, y: joinY))
        path.addCurve(to: CGPoint(x: centerX + radius, y: bulbY), control1: CGPoint(x: centerX + radius, y: bulbY - radius * 0.9), control2: CGPoint(x: centerX + radius, y: bulbY - radius * 0.35))
        path.addCurve(to: CGPoint(x: centerX, y: bulbY + radius), control1: CGPoint(x: centerX + radius, y: bulbY + radius * 0.55), control2: CGPoint(x: centerX + radius * 0.55, y: bulbY + radius))
        path.addCurve(to: CGPoint(x: centerX - radius, y: bulbY), control1: CGPoint(x: centerX - radius * 0.55, y: bulbY + radius), control2: CGPoint(x: centerX - radius, y: bulbY + radius * 0.55))
        path.addCurve(to: CGPoint(x: centerX - stemRadius, y: joinY), control1: CGPoint(x: centerX - radius, y: bulbY - radius * 0.35), control2: CGPoint(x: centerX - radius, y: bulbY - radius * 0.9))
        path.closeSubpath()
        path.move(to: CGPoint(x: centerX, y: rect.minY + rect.height * 0.35))
        path.addLine(to: CGPoint(x: centerX, y: bulbY + radius * 0.3))
        return path
    }
}

private struct ThermalSignalShape: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: 0, y: rect.height * 0.8))
        path.addLines([CGPoint(x: rect.width * 0.25, y: rect.height * 0.8), CGPoint(x: rect.width * 0.43, y: rect.height * 0.2), CGPoint(x: rect.width * 0.62, y: rect.height * 0.8), CGPoint(x: rect.width, y: rect.height * 0.8)])
        path.move(to: CGPoint(x: rect.width * 0.1, y: rect.height))
        path.addLine(to: CGPoint(x: rect.width * 0.9, y: rect.height))
        return path
    }
}

private struct ResizeMark: Shape {
    func path(in rect: CGRect) -> Path {
        var path = Path()
        path.move(to: CGPoint(x: 0, y: rect.height)); path.addLine(to: CGPoint(x: rect.width, y: 0))
        path.move(to: CGPoint(x: rect.width * 0.45, y: rect.height)); path.addLine(to: CGPoint(x: rect.width, y: rect.height * 0.45))
        return path
    }
}

private struct WindowDragRegion<Content: View>: View {
    @ViewBuilder var content: () -> Content
    var body: some View {
        content()
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            .contentShape(Rectangle())
            .onHover { hovering in (hovering ? NSCursor.openHand : NSCursor.arrow).set() }
    }
}

private enum InstrumentTime {
    private static let formatter: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = .autoupdatingCurrent
        formatter.dateFormat = "HH:mm:ss"
        return formatter
    }()

    static func string(_ date: Date) -> String { formatter.string(from: date) }
}
