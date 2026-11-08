import AppKit

func color(_ rgb: UInt32, alpha: CGFloat = 1) -> NSColor {
    NSColor(srgbRed: CGFloat((rgb >> 16) & 255) / 255, green: CGFloat((rgb >> 8) & 255) / 255,
            blue: CGFloat(rgb & 255) / 255, alpha: alpha)
}

@MainActor
class DragView: NSView {
    override var isFlipped: Bool { true }
    override var mouseDownCanMoveWindow: Bool { true }
    override func mouseDown(with event: NSEvent) { window?.performDrag(with: event) }
}

final class Rings: DragView {
    let assistant: Assistant
    var session: Double?, weekly: Double?
    var muted = false, needsAuth = false, mascot = false
    var artwork: NSImage?
    var sessionColor: NSColor { color(assistant == .claude ? 0xE8835F : 0x6C9BFF) }
    var weeklyColor: NSColor { color(assistant == .claude ? 0xF4C7AC : 0xB7CDFF) }
    init(_ assistant: Assistant) { self.assistant = assistant; super.init(frame: NSRect(x: 0, y: 0, width: 112, height: 112)) }
    required init?(coder: NSCoder) { fatalError("init(coder:) no disponible") }
    override func draw(_ dirtyRect: NSRect) {
        ring(radius: 51, value: weekly, tint: weeklyColor)
        ring(radius: 40, value: session, tint: sessionColor)
        let size: CGFloat = mascot ? 64 : 56
        NSGraphicsContext.current?.imageInterpolation = mascot ? .none : .high
        artwork?.draw(in: NSRect(x: 56 - size / 2, y: 56 - size / 2, width: size, height: size),
                      from: .zero, operation: .sourceOver, fraction: 1, respectFlipped: true, hints: nil)
    }
    private func ring(radius: CGFloat, value: Double?, tint: NSColor) {
        let track = NSBezierPath(ovalIn: NSRect(x: 56 - radius, y: 56 - radius, width: radius * 2, height: radius * 2))
        track.lineWidth = 7; track.lineCapStyle = .round
        if value == nil && !needsAuth { track.setLineDash([2.1, 8.4], count: 2, phase: 0) }
        NSColor.white.withAlphaComponent(muted ? 0.06 : 0.13).setStroke(); track.stroke()
        guard let value = value, value > 0 else { return }
        let path = NSBezierPath()
        path.appendArc(withCenter: NSPoint(x: 56, y: 56), radius: radius, startAngle: -90,
                       endAngle: -90 + CGFloat(min(value, 100)) * 3.6, clockwise: false)
        path.lineWidth = 7; path.lineCapStyle = .round
        (value > 90 ? color(0xFF6B6B) : tint).withAlphaComponent(muted ? 0.45 : 1).setStroke(); path.stroke()
    }
}

@MainActor
func drawText(_ text: String, in rect: NSRect, size: CGFloat, tint: NSColor = color(0xF2F4F8), alignment: NSTextAlignment = .left, bold: Bool = false) {
    let paragraph = NSMutableParagraphStyle(); paragraph.alignment = alignment; paragraph.lineBreakMode = .byTruncatingTail
    (text as NSString).draw(in: rect, withAttributes: [.font: NSFont.systemFont(ofSize: size, weight: bold ? .semibold : .regular),
                                                     .foregroundColor: tint, .paragraphStyle: paragraph])
}
