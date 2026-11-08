import AppKit
import Darwin

signal(SIGPIPE, SIG_IGN)

let arguments = Array(CommandLine.arguments.dropFirst())
if arguments.first == "--quit" {
    exit(Lifecycle.quitOthers() ? 0 : 1)
}
if arguments.first == "--configure", arguments.count == 4,
   let variant = Int(arguments[1]), (0...1).contains(variant),
   let show = Int(arguments[2]), (0...2).contains(show),
   let fixed = Int(arguments[3]), (0...1).contains(fixed) {
    do {
        var settings = Settings.load()
        settings.Variant = variant; settings.Show = show; settings.FixedVariant = fixed == 1
        try settings.save()
        exit(0)
    } catch { fputs("No se pudieron guardar los ajustes.\n", stderr); exit(1) }
}
if arguments.first == "--startup", arguments.count == 2, ["on", "off"].contains(arguments[1]) {
    do { try Lifecycle.setStartup(arguments[1] == "on"); exit(0) }
    catch { fputs("No se pudo cambiar el inicio automático.\n", stderr); exit(1) }
}
if !arguments.isEmpty { fputs("Argumentos no válidos.\n", stderr); exit(1) }

let app = NSApplication.shared
app.setActivationPolicy(.accessory)
// El bloqueo evita que dos aperturas simultáneas creen dos paneles.
do { try FileManager.default.createDirectory(at: Settings.directory, withIntermediateDirectories: true) }
catch { fputs("No se pudo abrir la carpeta de ajustes.\n", stderr); exit(1) }
let instanceLock = open(Settings.directory.appendingPathComponent("instance.lock").path, O_CREAT | O_RDWR | O_CLOEXEC, mode_t(0o600))
if instanceLock < 0 { fputs("No se pudo comprobar la instancia de Usage Orbit.\n", stderr); exit(1) }
if flock(instanceLock, LOCK_EX | LOCK_NB) != 0 {
    DistributedNotificationCenter.default().postNotificationName(Notification.Name("com.usageorbit.widget.restore"), object: nil, userInfo: nil, deliverImmediately: true)
    _ = Lifecycle.otherInstances().first?.activate(options: [.activateIgnoringOtherApps])
    close(instanceLock)
    exit(0)
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var widget: WidgetWindow?
    private var terminationSignal: DispatchSourceSignal?
    func applicationDidFinishLaunching(_ notification: Notification) {
        widget = WidgetWindow()
        DistributedNotificationCenter.default().addObserver(self, selector: #selector(restore), name: Notification.Name("com.usageorbit.widget.restore"), object: nil)
        signal(SIGTERM, SIG_IGN)
        let source = DispatchSource.makeSignalSource(signal: SIGTERM, queue: .main)
        source.setEventHandler { DispatchQueue.main.async { NSApp.terminate(nil) } }
        source.resume(); terminationSignal = source
    }
    @objc private func restore() { widget?.restore() }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { widget?.restore(); return true }
    func applicationWillTerminate(_ notification: Notification) {
        widget?.stop(); terminationSignal?.cancel(); DistributedNotificationCenter.default().removeObserver(self)
    }
}

// El código de arranque corre en el hilo principal; se declara así para poder crear el delegado de la interfaz.
MainActor.assumeIsolated {
    let delegate = AppDelegate()
    app.delegate = delegate
    app.run()
}
close(instanceLock)
