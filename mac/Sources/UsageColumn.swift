import AppKit

final class UsageColumn: DragView {
    let assistant: Assistant
    let rings: Rings
    let animator: MascotAnimator
    private var reading = UsageReading()
    private var nextAttempt = Date()
    private var notice: String?
    private var imagesOnly = false
    private var stale = false
    private var status = "Sin datos", instruction = ""

    init(_ assistant: Assistant) {
        self.assistant = assistant; rings = Rings(assistant); animator = MascotAnimator(assistant)
        super.init(frame: .zero); addSubview(rings)
        setAccessibilityElement(true); setAccessibilityRole(.group)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) no disponible") }
    override func layout() {
        super.layout(); rings.frame.origin = NSPoint(x: (bounds.width - 112) / 2, y: imagesOnly ? 4 : 22)
    }
    var needsAttention: Bool {
        reading.state != .ok || stale || rings.session == nil || rings.weekly == nil
    }
    var desiredHeight: CGFloat { imagesOnly ? (needsAttention ? 164 : 122) : 230 }
    func update(_ poller: UsagePoller, settings: Settings) {
        reading = poller.reading; nextAttempt = poller.nextAttempt; notice = poller.notice; imagesOnly = settings.View == 1
        let now = Date(), auth = reading.state == .needsAuth
        stale = reading.lastSuccess.map { now.timeIntervalSince($0) >= 900 || (reading.state != .ok && reading.state != .noData) } ?? false
        rings.session = reading.session?.displayed(at: now, auth: auth)
        rings.weekly = reading.weekly?.displayed(at: now, auth: auth)
        rings.muted = stale; rings.needsAuth = auth; rings.mascot = settings.Variant == 1
        rings.artwork = settings.Variant == 1 ? animator.current : Logos.image(assistant)
        let age = reading.lastSuccess.map { "hace " + Self.age(now.timeIntervalSince($0)) } ?? ""
        switch reading.state {
        case .ok: status = (stale ? "Antiguo · " : "") + age
        case .needsAuth: status = "Sin sesión"
        case .connectionError: status = "Sin conexión"
        case .rateLimited: status = "Demasiadas consultas"
        case .serviceError: status = "Error del servicio"
        case .noData: status = "Sin datos" + (age.isEmpty ? "" : " · " + age)
        }
        if imagesOnly && reading.state == .ok && (rings.session == nil || rings.weekly == nil) { status = "Falta actualizar" }
        instruction = auth ? assistant.login : (stale && reading.state != .ok && reading.state != .noData ? "Antiguo · " + age : "")
        if reading.state == .connectionError || reading.state == .rateLimited {
            instruction = "Reintenta en \(max(1, Int(ceil(nextAttempt.timeIntervalSince(now) / 60)))) min" + (stale ? " · Antiguo" : "")
        }
        let session = row(reading.session, name: "Sesión"), weekly = row(reading.weekly, name: "Semana")
        let details = "\(assistant.title)\nExterior: Semana. Interior: Sesión. Los anillos indican el porcentaje consumido.\n\(session.0): \(session.1) · \(session.2)\n\(weekly.0): \(weekly.1) · \(weekly.2)\n\(status)\n\(instruction)\n\(reading.message)"
        setAccessibilityLabel(details)
        needsLayout = true; needsDisplay = true; rings.needsDisplay = true
    }
    func animate(_ active: Bool) { animator.advance(active); rings.artwork = animator.current; rings.needsDisplay = true }
    override func draw(_ dirtyRect: NSRect) {
        if !imagesOnly {
            drawText(assistant.title, in: NSRect(x: 0, y: 1, width: bounds.width, height: 18), size: 11, alignment: .center, bold: true)
            drawRow(reading.session, name: "Sesión", y: 141, tint: rings.sessionColor)
            drawRow(reading.weekly, name: "Semana", y: 163, tint: rings.weeklyColor)
        }
        if !imagesOnly || needsAttention {
            let y: CGFloat = imagesOnly ? 122 : 189
            let tint = stale || reading.state == .rateLimited ? color(0xE8BA72) : color(0xADB3C1)
            drawText(!imagesOnly ? (notice ?? status) : status, in: NSRect(x: 0, y: y, width: bounds.width, height: 17), size: 10.5, tint: tint, alignment: .center)
            drawText(instruction, in: NSRect(x: 0, y: y + 17, width: bounds.width, height: 17), size: 10.5, tint: tint, alignment: .center)
        }
    }
    private func row(_ window: UsageWindow?, name: String) -> (String, String, String) {
        let auth = reading.state == .needsAuth, now = Date()
        let value = window?.displayed(at: now, auth: auth)
        let label = name + (window.map { " " + Self.duration($0.minutes) } ?? "")
        let percentage: String
        if let value = value {
            if value > 0 && value < 0.05 { percentage = "<0.1 %" }
            else { percentage = value.formatted(.number.locale(Locale(identifier: "es_MX")).precision(.fractionLength(0...1))) + " %" }
        } else { percentage = "—" }
        let reset: String
        if auth { reset = "Sin sesión" }
        else if let date = window?.resets, date <= now { reset = "Falta actualizar" }
        else if value == nil { reset = "Sin datos" }
        else if let date = window?.resets { reset = "en " + Self.remaining(date.timeIntervalSince(now)) }
        else { reset = "Sin reinicio" }
        return (label, percentage, reset)
    }
    private func drawRow(_ window: UsageWindow?, name: String, y: CGFloat, tint: NSColor) {
        let cells = row(window, name: name)
        let expired = window?.resets.map { $0 <= Date() } ?? false
        let alpha: CGFloat = stale && !expired ? 0.55 : 1
        tint.withAlphaComponent(alpha).setFill(); NSBezierPath(ovalIn: NSRect(x: 0, y: y + 5, width: 5, height: 5)).fill()
        drawText(cells.0, in: NSRect(x: 9, y: y, width: 70, height: 18), size: 11, tint: color(0xF2F4F8, alpha: alpha))
        let used = window?.displayed(at: Date(), auth: reading.state == .needsAuth)
        drawText(cells.1, in: NSRect(x: 80, y: y, width: 48, height: 18), size: 11, tint: ((used ?? 0) > 90 ? color(0xFF6B6B) : tint).withAlphaComponent(alpha), bold: true)
        drawText(cells.2, in: NSRect(x: 130, y: y + 1, width: bounds.width - 130, height: 18), size: 10, tint: color(0xADB3C1, alpha: alpha))
    }
    private static func duration(_ minutes: Int) -> String {
        if minutes % 1440 == 0 { return "\(minutes / 1440) d" }
        if minutes % 60 == 0 { return "\(minutes / 60) h" }
        return "\(minutes) min"
    }
    private static func remaining(_ interval: TimeInterval) -> String {
        let minutes = max(1, Int(ceil(interval / 60)))
        if minutes >= 1440 { return "\(minutes / 1440) d \(minutes % 1440 / 60) h" }
        if minutes >= 60 { return "\(minutes / 60) h \(minutes % 60) min" }
        return "\(minutes) min"
    }
    private static func age(_ interval: TimeInterval) -> String {
        if interval < 60 { return "menos de 1 min" }
        if interval < 3600 { return "\(Int(interval / 60)) min" }
        if interval < 86400 { return "\(Int(interval / 3600)) h" }
        return "\(Int(interval / 86400)) d"
    }
}
