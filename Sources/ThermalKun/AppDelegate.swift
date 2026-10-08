import AppKit
import ServiceManagement
import SwiftUI

final class DesktopPanel: NSPanel {
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { false }
    private var dragStart: NSPoint?
    private var originAtStart: NSPoint?

    override func sendEvent(_ event: NSEvent) {
        let location = event.locationInWindow
        switch event.type {
        case .leftMouseDown:
            let header = NSRect(x: 18, y: contentLayoutRect.height - 56, width: max(0, contentLayoutRect.width - 150), height: 43)
            if header.contains(location) {
                dragStart = convertPoint(toScreen: location)
                originAtStart = frame.origin
                return
            }
        case .leftMouseDragged:
            if let start = dragStart, let origin = originAtStart {
                let cursor = convertPoint(toScreen: location)
                setFrameOrigin(NSPoint(x: origin.x + cursor.x - start.x, y: origin.y + cursor.y - start.y))
                return
            }
        case .leftMouseUp:
            if dragStart != nil { dragStart = nil; originAtStart = nil; return }
        default: break
        }
        super.sendEvent(event)
    }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate {
    private let state = AppState()
    private let login = LoginService()
    private var panel: DesktopPanel!
    private var settingsWindow: NSWindow?
    private var statusItem: NSStatusItem!
    private var showItem: NSMenuItem!
    private var screenObserver: NSObjectProtocol?
    private var saveTimer: Timer?
    private var applyingFrame = false
    private var lastMode = false
    private var testing = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        testing = CommandLine.arguments.contains("--ui-test")
        installMenu()
        createPanel()
        state.settingsDidChange = { [weak self] old, new in self?.applySettings(old: old, new: new) }
        screenObserver = NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.fitToScreens() }
        }
        state.start()
        if CommandLine.arguments.contains("--settings") { openSettings() }
        if testing { runUISelfCheck() }
        if CommandLine.arguments.contains("--export-assets") { exportScreenshots() }
    }

    func applicationWillTerminate(_ notification: Notification) {
        saveFrame()
        state.stop()
        if let screenObserver { NotificationCenter.default.removeObserver(screenObserver) }
    }

    private func installMenu() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = NSImage(systemSymbolName: "thermometer.medium", accessibilityDescription: "thermal kun")
        statusItem.button?.toolTip = "thermal kun — System Monitor"
        let menu = NSMenu(title: "thermal kun")
        menu.delegate = self
        let title = NSMenuItem(title: "thermal kun", action: nil, keyEquivalent: "")
        menu.addItem(title)
        showItem = NSMenuItem(title: "Hide", action: #selector(togglePanel), keyEquivalent: "h")
        showItem.target = self; menu.addItem(showItem)
        add(menu, "Settings…", #selector(openSettings), ",")
        add(menu, "Center Panel on Screen", #selector(centerPanel), "")
        menu.addItem(.separator())
        add(menu, "Quit thermal kun", #selector(quit), "q")
        statusItem.menu = menu
        let main = NSMenu()
        let appItem = NSMenuItem()
        appItem.submenu = menu.copy() as? NSMenu
        appItem.submenu?.delegate = self
        main.addItem(appItem)
        NSApp.mainMenu = main
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        for item in menu.items where item.action == #selector(togglePanel) {
            item.title = panel?.isVisible == true ? "Hide" : "Show"
        }
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        return false
    }

    private func add(_ menu: NSMenu, _ title: String, _ action: Selector, _ key: String) {
        let item = NSMenuItem(title: title, action: action, keyEquivalent: key)
        item.target = self; menu.addItem(item)
    }

    private func createPanel() {
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
        let initialSide: CGFloat = state.settings.compact ? 360 : 480
        let initial = NSRect(x: screen.maxX - initialSide - 30, y: screen.maxY - initialSide - 40, width: initialSide, height: initialSide)
        panel = DesktopPanel(contentRect: initial, styleMask: [.borderless, .resizable, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.title = "thermal kun"
        panel.identifier = NSUserInterfaceItemIdentifier("ThermalKunPanel")
        panel.delegate = self
        panel.isReleasedWhenClosed = false
        panel.isOpaque = true
        panel.alphaValue = 1
        panel.backgroundColor = NSColor(calibratedRed: 0.055, green: 0.06, blue: 0.08, alpha: 1)
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isFloatingPanel = false
        panel.becomesKeyOnlyIfNeeded = true
        panel.isMovableByWindowBackground = false
        panel.isMovable = true
        panel.minSize = NSSize(width: 350, height: 350)
        panel.contentAspectRatio = NSSize(width: 1, height: 1)
        panel.collectionBehavior = [.canJoinAllSpaces, .stationary, .ignoresCycle]
        let content = PanelView(state: state, onSettings: { [weak self] in self?.openSettings() }, onHide: { [weak self] in self?.hidePanel() })
            .overlay(alignment: .bottomTrailing) { ResizeHandle().frame(width: 20, height: 20).padding(5) }
            .environment(\.locale, Locale(identifier: "en_US"))
        panel.contentView = NSHostingView(rootView: content)
        lastMode = state.settings.compact
        let proposed = savedFrame(compact: lastMode) ?? initial
        setPanelFrame(proposed)
        applyAppearance()
        applyLevel()
        panel.orderFrontRegardless()
    }

    private var screenFrames: [NSRect] { NSScreen.screens.map(\.visibleFrame) }
    private func savedFrame(compact: Bool) -> NSRect? {
        guard let values = UserDefaults.standard.array(forKey: compact ? "frame.compact" : "frame.detail") as? [Double], values.count == 4 else { return nil }
        return NSRect(x: values[0], y: values[1], width: values[2], height: values[3])
    }
    private func saveFrame(compact: Bool? = nil) {
        guard panel != nil, !applyingFrame else { return }
        let frame = panel.frame
        UserDefaults.standard.set([frame.minX, frame.minY, frame.width, frame.height], forKey: (compact ?? lastMode) ? "frame.compact" : "frame.detail")
    }
    private func setPanelFrame(_ frame: NSRect) {
        applyingFrame = true
        panel.setFrame(WindowGeometry.squareRestored(frame, screens: screenFrames), display: true)
        applyingFrame = false
    }
    private func fitToScreens() { setPanelFrame(panel.frame); saveFrame() }
    private func applyLevel() {
        panel.level = state.settings.alwaysOnTop ? .floating : NSWindow.Level(rawValue: Int(CGWindowLevelForKey(.desktopIconWindow)) + 1)
        if panel.isVisible { panel.orderFrontRegardless() }
    }
    private func applyAppearance() {
        let name: NSAppearance.Name? = state.settings.appearance == "Dark" ? .darkAqua : state.settings.appearance == "Light" ? .aqua : nil
        panel.appearance = name.flatMap(NSAppearance.init(named:))
    }
    private func applySettings(old: AppSettings, new: AppSettings) {
        if old.compact != new.compact {
            saveFrame(compact: old.compact)
            let previous = panel.frame
            let side = savedFrame(compact: new.compact)?.width ?? (new.compact ? 360 : 480)
            lastMode = new.compact
            setPanelFrame(NSRect(x: previous.minX, y: previous.maxY - side, width: side, height: side))
            saveFrame()
        }
        if old.alwaysOnTop != new.alwaysOnTop { applyLevel() }
        if old.appearance != new.appearance { applyAppearance() }
    }

    func windowDidMove(_ notification: Notification) { scheduleSave() }
    func windowDidResize(_ notification: Notification) {
        if !applyingFrame, abs(panel.frame.width - panel.frame.height) > 0.5 { setPanelFrame(panel.frame) }
        scheduleSave()
    }
    private func scheduleSave() {
        guard !applyingFrame else { return }
        saveTimer?.invalidate()
        saveTimer = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: false) { [weak self] _ in
            Task { @MainActor in self?.saveFrame() }
        }
    }

    @objc private func togglePanel() {
        if panel.isVisible { hidePanel() } else { panel.orderFrontRegardless(); showItem.title = "Hide" }
    }
    private func hidePanel() { panel.orderOut(nil); showItem.title = "Show" }
    @objc private func centerPanel() {
        let screen = NSScreen.main?.visibleFrame ?? screenFrames.first ?? panel.frame
        var frame = panel.frame
        frame.origin = NSPoint(x: screen.midX - frame.width / 2, y: screen.midY - frame.height / 2)
        setPanelFrame(frame); saveFrame()
        panel.orderFrontRegardless(); showItem.title = "Hide"
    }
    @objc private func openSettings() {
        if settingsWindow == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 720), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
            window.title = "thermal kun — Settings"
            window.isReleasedWhenClosed = false
            window.minSize = NSSize(width: 480, height: 600)
            window.contentView = NSHostingView(rootView: SettingsView(state: state, login: login, centerPanel: { [weak self] in self?.centerPanel() }))
            window.center()
            settingsWindow = window
        }
        login.refresh()
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.makeKeyAndOrderFront(nil)
    }
    @objc private func quit() { NSApp.terminate(nil) }

    // 実機検証用: 同じウィンドウ操作経路を通し、通常起動の設定は保存・復元する。
    private func runUISelfCheck() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 5) { [weak self] in
            guard let self else { return }
            let original = self.state.settings
            let originalDetailFrame = UserDefaults.standard.object(forKey: "frame.detail")
            let originalCompactFrame = UserDefaults.standard.object(forKey: "frame.compact")
            let frame = self.panel.frame
            var checks: [String: Bool] = [:]
            checks["receivedLiveSnapshot"] = self.state.snapshot != nil
            checks["squareLayout"] = abs(self.panel.frame.width - self.panel.frame.height) < 0.5
            checks["opaquePanel"] = self.panel.isOpaque && self.panel.alphaValue == 1 && self.panel.backgroundColor.alphaComponent == 1
            self.state.settings.alwaysOnTop = false
            checks["desktopLevel"] = self.panel.level.rawValue < NSWindow.Level.normal.rawValue
            self.hidePanel(); checks["hide"] = !self.panel.isVisible
            self.togglePanel(); checks["show"] = self.panel.isVisible
            self.state.settings.alwaysOnTop = true; checks["alwaysOnTop"] = self.panel.level == .floating
            self.state.settings.compact.toggle(); checks["compactToggle"] = self.state.settings.compact != original.compact
            checks["squareAfterCompactToggle"] = abs(self.panel.frame.width - self.panel.frame.height) < 0.5
            self.state.settings.appearance = "Light"; checks["lightMode"] = self.panel.appearance?.name == .aqua
            self.state.resetPeaks(); checks["resetPeaks"] = self.state.cpuPeak == nil
            self.setPanelFrame(NSRect(x: -100000, y: -100000, width: 440, height: 680))
            checks["offscreenRecovery"] = self.screenFrames.contains { $0.contains(self.panel.frame) }
            checks["squareAfterRecovery"] = abs(self.panel.frame.width - self.panel.frame.height) < 0.5
            self.saveFrame(); checks["framePersistence"] = self.savedFrame(compact: self.lastMode) == self.panel.frame
            self.openSettings(); checks["settings"] = self.settingsWindow?.isVisible == true
            self.settingsWindow?.close()
            self.state.settings = original
            self.setPanelFrame(frame)
            for (key, value) in [("frame.detail", originalDetailFrame), ("frame.compact", originalCompactFrame)] {
                if let value { UserDefaults.standard.set(value, forKey: key) } else { UserDefaults.standard.removeObject(forKey: key) }
            }
            let report: [String: Any] = ["checks": checks, "passed": checks.values.allSatisfy { $0 }, "timestamp": ISO8601DateFormatter().string(from: Date())]
            if let data = try? JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys]) {
                let path = ProcessInfo.processInfo.environment["THERMAL_KUN_UI_REPORT"] ?? "/tmp/thermal-kun-ui-check.json"
                try? data.write(to: URL(fileURLWithPath: path))
            }
        }
    }

    // GitHub用画像には、起動後に取得した実測値と実際のビューを使う。
    private func exportScreenshots() {
        DispatchQueue.main.asyncAfter(deadline: .now() + 12) { [weak self] in
            guard let self, self.state.snapshot != nil,
                  let output = ProcessInfo.processInfo.environment["THERMAL_KUN_ASSET_DIRECTORY"] else {
                fputs("Could not export screenshots: no live sample or output directory.\n", stderr)
                NSApp.terminate(nil)
                return
            }
            let original = self.state.settings
            let frame = self.panel.frame
            let defaults = UserDefaults.standard
            let savedFrames = ["frame.detail", "frame.compact"].map { ($0, defaults.object(forKey: $0)) }
            self.state.settings.animations = false
            let variants: [(String, Bool, String, CGFloat)] = [
                ("detail-dark.png", false, "Dark", 480),
                ("compact-dark.png", true, "Dark", 360),
                ("detail-light.png", false, "Light", 480)
            ]
            let directory = URL(fileURLWithPath: output, isDirectory: true)
            var index = 0
            var failures: [String] = []
            @MainActor func capture(_ view: NSView?, name: String) {
                do {
                    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
                    guard let view else { throw CocoaError(.fileWriteUnknown) }
                    view.layoutSubtreeIfNeeded()
                    view.displayIfNeeded()
                    guard let bitmap = view.bitmapImageRepForCachingDisplay(in: view.bounds) else { throw CocoaError(.fileWriteUnknown) }
                    view.cacheDisplay(in: view.bounds, to: bitmap)
                    guard let data = bitmap.representation(using: .png, properties: [:]) else { throw CocoaError(.fileWriteUnknown) }
                    try data.write(to: directory.appendingPathComponent(name))
                } catch {
                    let failure = error as NSError
                    failures.append("Could not export \(name) (\(failure.domain): \(failure.code)).")
                }
            }
            @MainActor func finish() {
                self.state.settings = original
                self.setPanelFrame(frame)
                for (key, value) in savedFrames {
                    if let value { defaults.set(value, forKey: key) } else { defaults.removeObject(forKey: key) }
                }
                for message in failures { fputs("\(message)\n", stderr) }
                if failures.isEmpty { print("Exported live app screenshots to \(output)") }
                NSApp.terminate(nil)
            }
            @MainActor func exportSettings() {
                self.state.settings.appearance = "Dark"
                self.state.settings.animations = original.animations
                self.openSettings()
                self.settingsWindow?.appearance = NSAppearance(named: .darkAqua)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                    capture(self.settingsWindow?.contentView?.superview, name: "settings.png")
                    self.settingsWindow?.close()
                    finish()
                }
            }
            @MainActor func next() {
                guard index < variants.count else { exportSettings(); return }
                let (name, compact, appearance, side) = variants[index]
                index += 1
                self.state.settings.compact = compact
                self.state.settings.appearance = appearance
                self.setPanelFrame(NSRect(x: frame.minX, y: frame.maxY - side, width: side, height: side))
                self.panel.orderFrontRegardless()
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.6) {
                    capture(self.panel.contentView, name: name)
                    next()
                }
            }
            next()
        }
    }
}

private struct ResizeHandle: NSViewRepresentable {
    func makeNSView(context: Context) -> GripView { GripView() }
    func updateNSView(_ nsView: GripView, context: Context) {}
    final class GripView: NSView {
        private var start: NSPoint = .zero
        private var frameAtStart: NSRect = .zero
        override func resetCursorRects() { addCursorRect(bounds, cursor: .crosshair) }
        override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
        override func draw(_ dirtyRect: NSRect) {
            NSColor(calibratedRed: 0.72, green: 0.40, blue: 1, alpha: 0.7).setStroke()
            let path = NSBezierPath()
            for offset in [4.0, 9.0, 14.0] { path.move(to: NSPoint(x: offset, y: 3)); path.line(to: NSPoint(x: 17, y: 20 - offset)) }
            path.lineWidth = 1; path.stroke()
        }
        override func mouseDown(with event: NSEvent) { start = window?.convertPoint(toScreen: event.locationInWindow) ?? .zero; frameAtStart = window?.frame ?? .zero }
        override func mouseDragged(with event: NSEvent) {
            guard let window else { return }
            let cursor = window.convertPoint(toScreen: event.locationInWindow)
            let delta = NSPoint(x: cursor.x - start.x, y: cursor.y - start.y)
            let adjustment = abs(delta.x) >= abs(delta.y) ? delta.x : -delta.y
            let side = max(window.minSize.width, window.minSize.height, frameAtStart.width + adjustment)
            let frame = NSRect(x: frameAtStart.minX, y: frameAtStart.maxY - side, width: side, height: side)
            window.setFrame(WindowGeometry.squareRestored(frame, screens: NSScreen.screens.map(\.visibleFrame)), display: true)
        }
        override func accessibilityLabel() -> String? { "Resize panel" }
    }
}
