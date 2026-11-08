import AppKit

private final class Surface: DragView {
    override func draw(_ dirtyRect: NSRect) {
        let outline = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 18, yRadius: 18)
        color(0x16171B, alpha: 0.975).setFill(); outline.fill()
        NSColor.white.withAlphaComponent(0.16).setStroke(); outline.lineWidth = 1; outline.stroke()
    }
}

@MainActor
private final class DockTab: NSView {
    var side = "left"
    var restore: (() -> Void)?
    var moved: ((CGFloat) -> Void)?
    var finished: (() -> Void)?
    private var start = NSPoint.zero, originalY: CGFloat = 0
    private var dragged = false
    override var acceptsFirstResponder: Bool { true }
    override func draw(_ dirtyRect: NSRect) {
        let path = NSBezierPath(roundedRect: bounds.insetBy(dx: 0.5, dy: 0.5), xRadius: 6, yRadius: 6)
        color(0x16171B, alpha: 0.975).setFill(); path.fill()
        NSColor.white.withAlphaComponent(0.16).setStroke(); path.stroke()
        drawText(side == "left" ? "›" : "‹", in: NSRect(x: 0, y: 28, width: bounds.width, height: 25), size: 22, alignment: .center)
    }
    override func mouseDown(with event: NSEvent) { start = NSEvent.mouseLocation; originalY = window?.frame.minY ?? 0; dragged = false }
    override func mouseDragged(with event: NSEvent) {
        let delta = NSEvent.mouseLocation.y - start.y
        if abs(delta) > 3 { dragged = true }
        if dragged { moved?(originalY + delta) }
    }
    override func mouseUp(with event: NSEvent) { if dragged { finished?() } else { restore?() } }
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 36 || event.keyCode == 49 { restore?() } else { super.keyDown(with: event) }
    }
}

@MainActor
private final class ActionButton: NSButton {
    var actionBlock: (() -> Void)?
    init(_ text: String, hint: String, symbol: String? = nil, action: @escaping () -> Void) {
        super.init(frame: .zero)
        title = text; toolTip = hint; actionBlock = action
        isBordered = false; bezelStyle = .regularSquare; font = .systemFont(ofSize: 10)
        contentTintColor = color(0xADB3C1)
        if let symbol = symbol { image = NSImage(systemSymbolName: symbol, accessibilityDescription: hint); imagePosition = .imageOnly }
        setAccessibilityLabel(hint)
        target = self; self.action = #selector(invoke)
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) no disponible") }
    @objc private func invoke() { actionBlock?() }
}

@MainActor
final class WidgetWindow: NSObject, NSWindowDelegate {
    let panel: NSPanel
    private var settings = Settings.load()
    private let surface = Surface(frame: .zero)
    private let tab = DockTab(frame: .zero)
    private var buttons: [ActionButton] = []
    private var columns: [Assistant: UsageColumn] = [:]
    private var pollers: [Assistant: UsagePoller] = [:]
    private var monitors: [Assistant: ActivityMonitor] = [:]
    private var timer: Timer?, animation: Timer?
    private var positioning = false, stopped = false, uninstalling = false
    private var menuActions: [Int: () -> Void] = [:]
    private var screenObserver: NSObjectProtocol?
    private var saveErrorShown = false

    override init() {
        panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 500, height: 270),
                        styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        super.init()
        panel.title = "Usage Orbit"; panel.level = .floating; panel.isOpaque = false; panel.backgroundColor = .clear
        panel.appearance = NSAppearance(named: .darkAqua)
        panel.hasShadow = true; panel.hidesOnDeactivate = false; panel.isReleasedWhenClosed = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.delegate = self; panel.contentView = surface
        makeToolbar()
        tab.toolTip = "Usage Orbit · clic para restaurar; arrastra para mover"
        tab.setAccessibilityElement(true); tab.setAccessibilityRole(.button); tab.setAccessibilityLabel(tab.toolTip)
        tab.restore = { [weak self] in self?.restore() }
        tab.moved = { [weak self] y in self?.settings.DockY = Double(y); self?.position() }
        tab.finished = { [weak self] in self?.save() }
        configureAssistants()
        let area = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1024, height: 768)
        if settings.X == nil { settings.X = Double(area.maxX - size.width - 24) }
        if settings.Y == nil { settings.Y = Double(area.maxY - size.height - 24) }
        update(); panel.orderFrontRegardless()
        timer = Timer.scheduledTimer(withTimeInterval: 15, repeats: true) { [weak self] _ in
            DispatchQueue.main.async {
                guard let self = self else { return }
                self.pollers.values.forEach { $0.tick() }; self.update()
            }
        }
        screenObserver = NotificationCenter.default.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            DispatchQueue.main.async { self?.position(); self?.save() }
        }
        pollers.values.forEach { $0.tick() }
    }
    private func configureAssistants() {
        for assistant in Assistant.allCases {
            if settings.assistants.contains(assistant) {
                if columns[assistant] == nil {
                    let column = UsageColumn(assistant); columns[assistant] = column; surface.addSubview(column)
                    let poller = UsagePoller(assistant); pollers[assistant] = poller
                    poller.changed = { [weak self] in self?.update() }
                    let monitor = ActivityMonitor(assistant); monitors[assistant] = monitor
                    monitor.changed = { [weak self] in self?.updateAnimation() }
                }
            } else {
                pollers.removeValue(forKey: assistant)?.stop()
                monitors.removeValue(forKey: assistant)?.stop()
                columns.removeValue(forKey: assistant)?.removeFromSuperview()
            }
        }
    }
    private func makeToolbar() {
        buttons = [
            ActionButton("Logos", hint: "Mostrar logotipos") { [weak self] in self?.setVariant(0) },
            ActionButton("Mascotas", hint: "Mostrar mascotas") { [weak self] in self?.setVariant(1) },
            ActionButton("", hint: "Cambiar vista: completa o solo imágenes", symbol: "rectangle") { [weak self] in
                guard let self = self else { return }; self.settings.View = self.settings.View == 0 ? 1 : 0; self.update(); self.save()
            },
            ActionButton("", hint: "Actualizar", symbol: "arrow.clockwise") { [weak self] in self?.refresh() },
            ActionButton("", hint: "Ocultar a la izquierda", symbol: "chevron.left") { [weak self] in self?.dock("left") },
            ActionButton("", hint: "Ocultar a la derecha", symbol: "chevron.right") { [weak self] in self?.dock("right") },
            ActionButton("", hint: "Más", symbol: "ellipsis") { [weak self] in self?.openMenu() },
            ActionButton("", hint: "Cerrar", symbol: "xmark") { NSApp.terminate(nil) }
        ]
        buttons.forEach { surface.addSubview($0) }
        buttons[0].isHidden = settings.FixedVariant
        buttons[1].isHidden = settings.FixedVariant
        let separator = NSTextField(labelWithString: "|")
        separator.frame = NSRect(x: 47, y: 9, width: 8, height: 16); separator.font = .systemFont(ofSize: 10)
        separator.textColor = color(0xADB3C1); surface.addSubview(separator)
    }
    private var size: NSSize {
        let width: CGFloat = settings.assistants.count == 2 && settings.View == 0 ? 500 : 320
        return NSSize(width: width, height: 37 + (columns.values.map { $0.desiredHeight }.max() ?? 230))
    }
    private func update() {
        guard !stopped else { return }
        for assistant in settings.assistants {
            if let poller = pollers[assistant] { columns[assistant]?.update(poller, settings: settings) }
        }
        buttons[0].contentTintColor = settings.Variant == 0 ? .white : color(0x858B99)
        buttons[1].contentTintColor = settings.Variant == 1 ? .white : color(0x858B99)
        position(); updateAnimation()
    }
    private func position() {
        guard !positioning, !stopped else { return }
        positioning = true; defer { positioning = false }
        let full = size, docked = settings.DockSide != nil
        let target = NSRect(x: CGFloat(settings.X ?? 0), y: CGFloat(settings.Y ?? 0), width: full.width, height: full.height)
        let screens = NSScreen.screens
        let screen = screens.max { a, b in
            let ra = a.visibleFrame.intersection(target), rb = b.visibleFrame.intersection(target)
            return (ra.isNull ? 0 : ra.width * ra.height) < (rb.isNull ? 0 : rb.width * rb.height)
        } ?? NSScreen.main
        let area = screen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1024, height: 768)
        let dimensions = docked ? NSSize(width: 22, height: 84) : full
        var x = CGFloat(settings.X ?? Double(area.minX)), y = CGFloat(settings.Y ?? Double(area.minY))
        if let side = settings.DockSide {
            x = side == "left" ? area.minX : area.maxX - dimensions.width
            y = CGFloat(settings.DockY ?? settings.Y ?? Double(area.minY)); tab.side = side
        }
        x = min(max(x, area.minX), max(area.minX, area.maxX - dimensions.width))
        y = min(max(y, area.minY), max(area.minY, area.maxY - dimensions.height))
        let content: NSView = docked ? tab : surface
        if panel.contentView !== content { panel.contentView = content }
        panel.setFrame(NSRect(origin: NSPoint(x: x, y: y), size: dimensions), display: true)
        content.frame = NSRect(origin: .zero, size: dimensions)
        if docked { settings.DockY = Double(y); tab.needsDisplay = true }
        else { settings.X = Double(x); settings.Y = Double(y) }
        let columnWidth = (full.width - 28) / CGFloat(settings.assistants.count)
        for (index, assistant) in settings.assistants.enumerated() {
            columns[assistant]?.frame = NSRect(x: 14 + CGFloat(index) * columnWidth, y: 32, width: columnWidth - 10, height: full.height - 37)
        }
        buttons[0].frame = NSRect(x: 9, y: 5, width: 36, height: 23)
        buttons[1].frame = NSRect(x: 56, y: 5, width: 54, height: 23)
        for index in 2..<buttons.count {
            buttons[index].frame = NSRect(x: full.width - CGFloat(buttons.count - index) * 25 - 8, y: 5, width: 24, height: 23)
        }
    }
    func windowDidMove(_ notification: Notification) {
        guard !positioning, settings.DockSide == nil, !stopped else { return }
        settings.X = Double(panel.frame.minX); settings.Y = Double(panel.frame.minY); save()
    }
    private func dock(_ side: String) {
        settings.DockY = Double(panel.frame.midY - 42); settings.DockSide = side
        update(); save()
    }
    func restore() { settings.DockSide = nil; update(); panel.orderFrontRegardless(); save() }
    private func setVariant(_ variant: Int) { settings.Variant = variant; update(); save() }
    private func setShow(_ show: Int) {
        settings.Show = show; configureAssistants(); update(); save(); pollers.values.forEach { $0.tick() }
    }
    private func refresh() { pollers.values.forEach { $0.refresh() } }
    private func updateAnimation() {
        let visible = !stopped && settings.Variant == 1 && settings.DockSide == nil
        let moving = visible && settings.assistants.contains { assistant in columns[assistant]?.animator.needsTicks(monitors[assistant]?.active ?? false) ?? false }
        if moving && animation == nil {
            animation = Timer.scheduledTimer(withTimeInterval: 0.25, repeats: true) { [weak self] _ in
                DispatchQueue.main.async {
                    guard let self = self, !self.stopped, self.settings.Variant == 1, self.settings.DockSide == nil else { return }
                    for assistant in self.settings.assistants {
                        let active = self.monitors[assistant]?.active ?? false
                        if self.columns[assistant]?.animator.needsTicks(active) == true { self.columns[assistant]?.animate(active) }
                    }
                    self.updateAnimation()
                }
            }
        } else if !moving { animation?.invalidate(); animation = nil }
    }
    // Muestra el autor y ofrece abrir el repositorio del proyecto.
    private func showAbout() {
        let alert = NSAlert()
        alert.messageText = "Usage Orbit"
        alert.informativeText = "Creado por LeobardoVilla88 y LeobardoVillalobos88.\nLicencia MIT."
        alert.addButton(withTitle: "Abrir repositorio")
        alert.addButton(withTitle: "Cerrar")
        NSApp.activate(ignoringOtherApps: true)
        if alert.runModal() == .alertFirstButtonReturn, let url = URL(string: "https://github.com/LeobardoVillalobos88/widget-usage-orbit") {
            NSWorkspace.shared.open(url)
        }
    }
    private func item(_ title: String, checked: Bool = false, action: @escaping () -> Void) -> NSMenuItem {
        let item = NSMenuItem(title: title, action: #selector(menuAction(_:)), keyEquivalent: "")
        item.target = self; item.tag = menuActions.count; menuActions[item.tag] = action; item.state = checked ? .on : .off
        return item
    }
    @objc private func menuAction(_ sender: NSMenuItem) { menuActions[sender.tag]?() }
    private func openMenu() {
        menuActions.removeAll()
        let menu = NSMenu()
        menu.addItem(item("Actualizar ahora") { [weak self] in self?.refresh() })
        func submenu(_ title: String, labels: [String], selected: Int, action: @escaping (Int) -> Void) {
            let parent = NSMenuItem(title: title, action: nil, keyEquivalent: ""), child = NSMenu()
            for (index, label) in labels.enumerated() { child.addItem(item(label, checked: index == selected) { action(index) }) }
            parent.submenu = child; menu.addItem(parent)
        }
        if !settings.FixedVariant {
            submenu("Apariencia", labels: ["Logotipos", "Mascotas"], selected: settings.Variant) { [weak self] in self?.setVariant($0) }
        }
        submenu("Vista", labels: ["Completa", "Solo imágenes"], selected: settings.View) { [weak self] value in self?.settings.View = value; self?.update(); self?.save() }
        submenu("Asistentes", labels: ["Ambos", "Solo Claude", "Solo Codex"], selected: settings.Show) { [weak self] in self?.setShow($0) }
        menu.addItem(.separator())
        menu.addItem(item("Iniciar al iniciar sesión", checked: Lifecycle.startsAtLogin) { [weak self] in self?.toggleStartup() })
        menu.addItem(item("Acerca de Usage Orbit") { [weak self] in self?.showAbout() })
        menu.addItem(item("Desinstalar…") { [weak self] in self?.uninstall() })
        menu.addItem(item("Cerrar") { NSApp.terminate(nil) })
        menu.popUp(positioning: nil, at: NSPoint(x: buttons[6].frame.minX, y: 30), in: surface)
    }
    private func toggleStartup() {
        let enabled = !Lifecycle.startsAtLogin
        DispatchQueue.global(qos: .utility).async { [weak self] in
            do { try Lifecycle.setStartup(enabled) }
            catch { DispatchQueue.main.async { self?.alert("No se pudo cambiar el inicio automático. Comprueba los permisos de ~/Library/LaunchAgents.") } }
        }
    }
    private func uninstall() {
        let alert = NSAlert(); alert.messageText = "¿Desinstalar Usage Orbit?"
        alert.informativeText = "Se eliminarán la app de ~/Applications, el inicio automático y los ajustes. Las sesiones de Claude y Codex se conservan."
        alert.addButton(withTitle: "Desinstalar"); alert.addButton(withTitle: "Cancelar")
        NSApp.activate(ignoringOtherApps: true)
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        uninstalling = true; stop()
        DispatchQueue.global(qos: .utility).async { [weak self] in
            do { try Lifecycle.uninstall(); DispatchQueue.main.async { NSApp.terminate(nil) } }
            catch { DispatchQueue.main.async { self?.alert("No se pudo completar la desinstalación. Ejecuta mac/uninstall.sh para reintentarlo."); NSApp.terminate(nil) } }
        }
    }
    private func alert(_ message: String) {
        let alert = NSAlert(); alert.messageText = "Usage Orbit"; alert.informativeText = message; alert.runModal()
    }
    private func save() {
        guard !uninstalling else { return }
        do { try settings.save() }
        catch {
            if !saveErrorShown { saveErrorShown = true; alert("No se pudieron guardar los ajustes en ~/Library/Application Support/UsageOrbit.") }
        }
    }
    func stop() {
        guard !stopped else { return }
        save(); stopped = true; timer?.invalidate(); animation?.invalidate()
        pollers.values.forEach { $0.stop() }; monitors.values.forEach { $0.stop() }
        if let observer = screenObserver { NotificationCenter.default.removeObserver(observer) }
    }
}
