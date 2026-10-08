import AppKit

// 保存した位置を、現在接続中のディスプレイ内に収める。
enum WindowGeometry {
    // 旧版の縦長フレームは幅を一辺として移行し、保存済みの上端位置を維持する。
    static func squareRestored(_ proposed: NSRect, screens: [NSRect], minimumSide: CGFloat = 350) -> NSRect {
        let finite = [proposed.minX, proposed.minY, proposed.width, proposed.height].allSatisfy(\.isFinite)
        let candidate: NSRect
        if finite, proposed.width > 0, proposed.height > 0 {
            let side = max(minimumSide, proposed.width)
            candidate = NSRect(x: proposed.minX, y: proposed.maxY - side, width: side, height: side)
        } else {
            let screen = screens.first ?? NSRect(x: 0, y: 0, width: 1440, height: 900)
            candidate = NSRect(x: screen.minX + 40, y: screen.maxY - 520, width: 480, height: 480)
        }
        guard !screens.isEmpty else { return candidate }
        let target = screens.max { a, b in
            area(candidate.intersection(a)) < area(candidate.intersection(b))
        } ?? screens[0]
        let side = min(target.width, target.height, candidate.width)
        return NSRect(x: min(max(candidate.minX, target.minX), target.maxX - side),
                      y: min(max(candidate.minY, target.minY), target.maxY - side), width: side, height: side)
    }

    static func restored(_ proposed: NSRect, screens: [NSRect], minimum: NSSize = NSSize(width: 350, height: 280)) -> NSRect {
        guard !screens.isEmpty else { return proposed }
        let finite = [proposed.minX, proposed.minY, proposed.width, proposed.height].allSatisfy(\.isFinite)
        let candidate = finite && proposed.width > 0 && proposed.height > 0 ? proposed : NSRect(x: screens[0].minX + 40, y: screens[0].maxY - 650, width: 400, height: 600)
        let target = screens.max { a, b in
            area(candidate.intersection(a)) < area(candidate.intersection(b))
        } ?? screens[0]
        let width = min(target.width, max(minimum.width, candidate.width))
        let height = min(target.height, max(minimum.height, candidate.height))
        return NSRect(x: min(max(candidate.minX, target.minX), target.maxX - width),
                      y: min(max(candidate.minY, target.minY), target.maxY - height), width: width, height: height)
    }

    private static func area(_ rect: NSRect) -> CGFloat { rect.isNull ? 0 : rect.width * rect.height }
}
