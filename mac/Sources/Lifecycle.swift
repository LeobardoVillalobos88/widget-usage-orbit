import AppKit

enum Lifecycle {
    static let bundleID = "com.usageorbit.widget"
    static var installedApp: URL { FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications/Usage Orbit.app") }
    static var agent: URL { FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/LaunchAgents/" + bundleID + ".plist") }
    static var startsAtLogin: Bool { FileManager.default.fileExists(atPath: agent.path) }
    static func setStartup(_ enabled: Bool) throws {
        if enabled {
            let quoted = "'" + installedApp.path.replacingOccurrences(of: "'", with: "'\\''") + "'"
            let plist: [String: Any] = ["Label": bundleID, "RunAtLoad": true,
                                        "ProgramArguments": ["/bin/sh", "-c", "sleep 180; open -a " + quoted]]
            let data = try PropertyListSerialization.data(fromPropertyList: plist, format: .xml, options: 0)
            try FileManager.default.createDirectory(at: agent.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: agent, options: .atomic)
        } else {
            // bootout cancela también un inicio que siga esperando sus tres minutos.
            let cancellation = Cancellation()
            if let child = try? ChildProcess("/bin/launchctl", ["bootout", "gui/\(getuid())/" + bundleID], timeout: 5, cancellation: cancellation) {
                _ = try? child.collect(); child.stop()
            }
            if startsAtLogin { try FileManager.default.removeItem(at: agent) }
        }
    }
    static func uninstall() throws {
        try setStartup(false)
        // Estas son las únicas rutas que elimina la desinstalación.
        for url in [installedApp, Settings.directory] where FileManager.default.fileExists(atPath: url.path) {
            try FileManager.default.removeItem(at: url)
        }
    }
    static func otherInstances() -> [NSRunningApplication] {
        NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).filter { $0.processIdentifier != getpid() }
    }
    static func quitOthers() -> Bool {
        let others = otherInstances()
        for app in others { _ = app.terminate() }
        let deadline = Date().addingTimeInterval(8)
        while others.contains(where: { !$0.isTerminated }), Date() < deadline {
            RunLoop.current.run(until: Date().addingTimeInterval(0.05))
        }
        return others.allSatisfy { $0.isTerminated }
    }
}
