import AppKit
import Foundation

let root = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : FileManager.default.currentDirectoryPath)
let output = root.appendingPathComponent("assets/thermal-kun-thumbnail.png")
let socialOutput = root.appendingPathComponent("assets/thermal-kun-social-preview.png")
let screenshotURL = root.appendingPathComponent("assets/screenshots/detail-dark.png")
let iconURL = root.appendingPathComponent("assets/thermal-kun-icon.png")
let width = 1280, height = 640
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: width, pixelsHigh: height,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
let context = NSGraphicsContext(bitmapImageRep: bitmap)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context
context.imageInterpolation = .high
let lime = NSColor(calibratedRed: 0.73, green: 0.98, blue: 0.22, alpha: 1)
let purple = NSColor(calibratedRed: 0.72, green: 0.40, blue: 1, alpha: 1)
let white = NSColor(calibratedWhite: 0.97, alpha: 1)
let secondary = NSColor(calibratedRed: 0.57, green: 0.62, blue: 0.66, alpha: 1)
let background = NSGradient(starting: NSColor(calibratedRed: 0.13, green: 0.075, blue: 0.22, alpha: 1),
    ending: NSColor(calibratedRed: 0.055, green: 0.06, blue: 0.08, alpha: 1))!
background.draw(in: NSRect(x: 0, y: 0, width: width, height: height), angle: 15)

func text(_ string: String, x: CGFloat, y: CGFloat, font: NSFont, color: NSColor,
          width: CGFloat = 1000, height: CGFloat = 80) {
    let paragraph = NSMutableParagraphStyle()
    paragraph.lineBreakMode = .byClipping
    (string as NSString).draw(in: NSRect(x: x, y: y, width: width, height: height),
        withAttributes: [.font: font, .foregroundColor: color, .paragraphStyle: paragraph])
}
func line(_ x1: CGFloat, _ y1: CGFloat, _ x2: CGFloat, _ y2: CGFloat, color: NSColor, thickness: CGFloat = 1) {
    let path = NSBezierPath()
    path.move(to: NSPoint(x: x1, y: y1))
    path.line(to: NSPoint(x: x2, y: y2))
    path.lineWidth = thickness
    color.setStroke()
    path.stroke()
}

// 機能だけを説明し、デモ値や架空の警告は描かない。
text("KUN SERIES  /  MACOS UTILITY", x: 64, y: 600,
    font: .monospacedSystemFont(ofSize: 12, weight: .medium), color: purple, height: 18)
if let icon = NSImage(contentsOf: iconURL) {
    icon.draw(in: NSRect(x: 56, y: 489, width: 116, height: 116))
}
text("thermal kun", x: 184, y: 503, font: .systemFont(ofSize: 64, weight: .bold), color: white,
    width: 530, height: 84)
text("Desktop Thermal Monitor for macOS", x: 64, y: 452,
    font: .systemFont(ofSize: 23, weight: .regular), color: secondary, width: 630, height: 34)
line(64, 423, 645, 423, color: purple.withAlphaComponent(0.45))
let features: [(String, String, CGFloat)] = [
    ("CPU Temperature", "Read real CPU sensor measurements.", 343),
    ("Thermal State", "See the system's current thermal state.", 248),
    ("Live History", "Follow trends and reset the peak.", 153)
]
for (index, feature) in features.enumerated() {
    text(String(format: "%02d", index + 1), x: 64, y: feature.2 + 15,
        font: .monospacedSystemFont(ofSize: 13, weight: .semibold), color: index == 1 ? purple : lime, width: 30, height: 22)
    text(feature.0, x: 112, y: feature.2 + 7, font: .systemFont(ofSize: 27, weight: .semibold),
        color: white, width: 530, height: 42)
    text(feature.1, x: 112, y: feature.2 - 22, font: .systemFont(ofSize: 17, weight: .regular),
        color: secondary, width: 530, height: 28)
}

// 実画面を中央素材として大きく置き、縮小時もCPU温度と状態を読めるようにする。
let screenshotFrame = NSRect(x: 704, y: 104, width: 512, height: 512)
if let screenshot = NSImage(contentsOf: screenshotURL), screenshot.size.width > 0, screenshot.size.height > 0 {
    let scale = min(screenshotFrame.width / screenshot.size.width, screenshotFrame.height / screenshot.size.height)
    let fitted = NSRect(x: screenshotFrame.midX - screenshot.size.width * scale / 2,
        y: screenshotFrame.midY - screenshot.size.height * scale / 2,
        width: screenshot.size.width * scale, height: screenshot.size.height * scale).integral
    let shadow = NSShadow()
    shadow.shadowColor = NSColor.black.withAlphaComponent(0.55)
    shadow.shadowBlurRadius = 28
    shadow.shadowOffset = NSSize(width: 0, height: -12)
    NSGraphicsContext.saveGraphicsState()
    shadow.set()
    NSColor.black.withAlphaComponent(0.45).setFill()
    NSBezierPath(rect: fitted).fill()
    NSGraphicsContext.restoreGraphicsState()
    screenshot.draw(in: fitted)
    purple.withAlphaComponent(0.5).setStroke()
    let border = NSBezierPath(rect: fitted.insetBy(dx: -0.5, dy: -0.5))
    border.lineWidth = 1
    border.stroke()
} else {
    // スクリーンショット前でも完成したブランド画像として使える構図。
    let halo = NSBezierPath(ovalIn: NSRect(x: 758, y: 179, width: 414, height: 414))
    lime.withAlphaComponent(0.035).setFill()
    halo.fill()
    lime.withAlphaComponent(0.14).setStroke()
    halo.lineWidth = 1
    halo.stroke()
    if let icon = NSImage(contentsOf: iconURL) {
        icon.draw(in: NSRect(x: 800, y: 226, width: 330, height: 330))
    }
    text("Small panel. Clear signals.", x: 770, y: 145,
        font: .systemFont(ofSize: 25, weight: .medium), color: white, width: 440, height: 40)
    text("Your Mac, at a glance.", x: 810, y: 109,
        font: .systemFont(ofSize: 18, weight: .regular), color: secondary, width: 380, height: 28)
}
line(64, 77, 1216, 77, color: purple.withAlphaComponent(0.4))
text("DRAGGABLE DESKTOP PANEL", x: 64, y: 38,
    font: .monospacedSystemFont(ofSize: 11, weight: .medium), color: secondary, width: 560, height: 20)
text("macOS  •  Read-only monitoring", x: 931, y: 38,
    font: .systemFont(ofSize: 13, weight: .regular), color: secondary, width: 290, height: 24)
NSGraphicsContext.restoreGraphicsState()
let data = bitmap.representation(using: .png, properties: [:])!
try FileManager.default.createDirectory(at: output.deletingLastPathComponent(), withIntermediateDirectories: true)
try data.write(to: output)
try data.write(to: socialOutput)
print("Generated 1280 × 640 thumbnail and social preview\(FileManager.default.fileExists(atPath: screenshotURL.path) ? " with an actual screenshot" : " using the brand layout").")
