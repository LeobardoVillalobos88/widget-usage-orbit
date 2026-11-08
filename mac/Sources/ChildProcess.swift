import Foundation
import Darwin

// Cada comando tiene su propio grupo para cerrar también los procesos que lance la CLI.
final class ChildProcess {
    private var pid: pid_t = 0
    private var input: Int32 = -1
    private var output: Int32 = -1
    private var buffer = Data()
    private let deadline: Date
    private let cancellation: Cancellation
    private var registration: UUID?
    private let lock = NSLock()

    init(_ executable: String, _ arguments: [String], timeout: TimeInterval, cancellation: Cancellation) throws {
        self.deadline = Date().addingTimeInterval(timeout)
        self.cancellation = cancellation
        try cancellation.check()
        var incoming: [Int32] = [0, 0], outgoing: [Int32] = [0, 0]
        guard pipe(&incoming) == 0 else { throw SourceError.unavailable }
        guard pipe(&outgoing) == 0 else {
            close(incoming[0]); close(incoming[1]); throw SourceError.unavailable
        }
        var actions: posix_spawn_file_actions_t?
        var attributes: posix_spawnattr_t?
        posix_spawn_file_actions_init(&actions)
        posix_spawnattr_init(&attributes)
        defer {
            posix_spawn_file_actions_destroy(&actions)
            posix_spawnattr_destroy(&attributes)
            close(incoming[0]); close(outgoing[1])
        }
        for fd in incoming + outgoing { _ = fcntl(fd, F_SETFD, FD_CLOEXEC) }
        posix_spawn_file_actions_adddup2(&actions, incoming[0], STDIN_FILENO)
        posix_spawn_file_actions_adddup2(&actions, outgoing[1], STDOUT_FILENO)
        posix_spawn_file_actions_addopen(&actions, STDERR_FILENO, "/dev/null", O_WRONLY, 0)
        posix_spawnattr_setflags(&attributes, Int16(POSIX_SPAWN_SETPGROUP))
        posix_spawnattr_setpgroup(&attributes, 0)
        let argv = ([executable] + arguments).map { strdup($0) } + [nil]
        var environment = ProcessInfo.processInfo.environment
        let executableDirectory = URL(fileURLWithPath: executable).deletingLastPathComponent().path
        environment["PATH"] = ([executableDirectory] + CLI.searchDirectories).joined(separator: ":") + ":/usr/bin:/bin:/usr/sbin:/sbin:" + (environment["PATH"] ?? "")
        let env = environment.map { strdup("\($0.key)=\($0.value)") } + [nil]
        defer { argv.forEach { free($0) }; env.forEach { free($0) } }
        do {
            registration = try cancellation.registerAndStart({ [weak self] in self?.stop() }) {
                let result = argv.withUnsafeBufferPointer { args in
                    env.withUnsafeBufferPointer { vars in
                        posix_spawn(&pid, executable, &actions, &attributes, args.baseAddress!, vars.baseAddress!)
                    }
                }
                guard result == 0 else { throw SourceError.unavailable }
                input = incoming[1]; output = outgoing[0]
                _ = fcntl(input, F_SETFL, O_NONBLOCK)
                _ = fcntl(output, F_SETFL, O_NONBLOCK)
            }
        } catch {
            close(incoming[1]); close(outgoing[0]); throw error
        }
    }

    private func check() throws {
        try cancellation.check()
        if Date() >= deadline { stop(); throw SourceError.timeout }
    }
    func send(_ object: [String: Any]) throws {
        var data = try JSONSerialization.data(withJSONObject: object)
        data.append(10)
        try data.withUnsafeBytes { bytes in
            var offset = 0
            while offset < bytes.count {
                try check()
                let count = Darwin.write(input, bytes.baseAddress!.advanced(by: offset), bytes.count - offset)
                if count > 0 { offset += count }
                else if errno == EAGAIN || errno == EINTR { Thread.sleep(forTimeInterval: 0.01) }
                else { throw SourceError.unavailable }
            }
        }
    }
    private func chunk() throws -> Data? {
        var bytes = [UInt8](repeating: 0, count: 8192)
        while true {
            try check()
            let count = Darwin.read(output, &bytes, bytes.count)
            if count > 0 { return Data(bytes.prefix(count)) }
            if count == 0 { return nil }
            if errno != EAGAIN && errno != EINTR { throw SourceError.unavailable }
            Thread.sleep(forTimeInterval: 0.02)
        }
    }
    func line() throws -> Data? {
        while true {
            try check()
            if let end = buffer.firstIndex(of: 10) {
                let line = Data(buffer[..<end]); buffer.removeSubrange(...end); return line
            }
            guard let next = try chunk() else {
                if buffer.isEmpty { return nil }
                let last = buffer; buffer.removeAll(); return last
            }
            buffer.append(next)
            if buffer.count > 2_097_152 { throw SourceError.invalidData }
        }
    }
    func collect() throws -> (Data, Int32) {
        close(input); input = -1
        var data = Data()
        while let next = try chunk() {
            data.append(next)
            if data.count > 2_097_152 { throw SourceError.invalidData }
        }
        var status: Int32 = 0
        while true {
            try check()
            lock.lock()
            let current = pid
            let result = current > 0 ? waitpid(current, &status, WNOHANG) : -1
            if result == current && current > 0 {
                // El proceso principal terminó; no dejamos descendientes en segundo plano.
                kill(-current, SIGKILL); pid = 0
            }
            lock.unlock()
            if result > 0 { return (data, status) }
            if result < 0 { throw SourceError.unavailable }
            Thread.sleep(forTimeInterval: 0.02)
        }
    }
    func stop() {
        lock.lock(); defer { lock.unlock() }
        if pid > 0 {
            kill(-pid, SIGKILL)
            var status: Int32 = 0
            while waitpid(pid, &status, 0) < 0 && errno == EINTR { }
            pid = 0
        }
    }
    deinit {
        if let id = registration { cancellation.remove(id) }
        stop()
        if input >= 0 { close(input) }
        if output >= 0 { close(output) }
    }
}

enum CLI {
    static var searchDirectories: [String] {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        return ["/opt/homebrew/bin", "/usr/local/bin", home + "/.npm-global/bin", home + "/.local/bin"]
    }
    static func find(_ name: String, cancellation: Cancellation) throws -> String {
        guard name == "claude" || name == "codex" else { throw SourceError.unavailable }
        for directory in searchDirectories {
            let path = directory + "/" + name
            if FileManager.default.isExecutableFile(atPath: path) { return path }
        }
        let child = try ChildProcess("/bin/zsh", ["-lc", "command -v " + name], timeout: 5, cancellation: cancellation)
        defer { child.stop() }
        let (data, status) = try child.collect()
        let path = String(decoding: data, as: UTF8.self).trimmingCharacters(in: .whitespacesAndNewlines)
        guard status == 0, path.hasPrefix("/"), FileManager.default.isExecutableFile(atPath: path) else { throw SourceError.unavailable }
        return path
    }
}
