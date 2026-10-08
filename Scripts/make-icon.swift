import AppKit
import Foundation

// 同シリーズの黒いタイルに、温度計だけを置く。値・警告などの装飾は加えない。
let output = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "assets/thermal-kun-icon.png"
let size = 1024
let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
    bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
    colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
let context = NSGraphicsContext(bitmapImageRep: bitmap)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = context
context.imageInterpolation = .high
let tile = NSBezierPath(roundedRect: NSRect(x: 48, y: 48, width: 928, height: 928),
    xRadius: 210, yRadius: 210)
let tileColor = NSColor(calibratedRed: 0.055, green: 0.06, blue: 0.08, alpha: 1)
tileColor.setFill()
tile.fill()
NSColor.white.withAlphaComponent(0.12).setStroke()
tile.lineWidth = 2
tile.stroke()

let stem = NSBezierPath(roundedRect: NSRect(x: 410, y: 330, width: 112, height: 480),
    xRadius: 56, yRadius: 56)
let bulb = NSBezierPath(ovalIn: NSRect(x: 348, y: 196, width: 236, height: 236))
NSColor.white.setFill()
stem.fill()
bulb.fill()
let innerStem = NSBezierPath(roundedRect: NSRect(x: 442, y: 323, width: 48, height: 454),
    xRadius: 24, yRadius: 24)
let innerBulb = NSBezierPath(ovalIn: NSRect(x: 380, y: 228, width: 172, height: 172))
tileColor.setFill()
innerStem.fill()
innerBulb.fill()
NSColor.white.setFill()
NSBezierPath(ovalIn: NSRect(x: 412, y: 260, width: 108, height: 108)).fill()
NSBezierPath(roundedRect: NSRect(x: 455, y: 308, width: 22, height: 306),
    xRadius: 11, yRadius: 11).fill()
for (index, y) in [690.0, 590.0, 490.0].enumerated() {
    let tick = NSBezierPath(roundedRect: NSRect(x: 560, y: y, width: index == 1 ? 70 : 96, height: 22),
        xRadius: 11, yRadius: 11)
    tick.fill()
}
NSGraphicsContext.restoreGraphicsState()
let destination = URL(fileURLWithPath: output)
try FileManager.default.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
try bitmap.representation(using: .png, properties: [:])!.write(to: destination)
