import Foundation
import CoreFoundation

enum Assistant: Int, CaseIterable {
    case claude, codex
    var title: String { self == .claude ? "Claude" : "Codex" }
    var login: String { self == .claude ? "claude auth login" : "codex login" }
    var activityPath: String { self == .claude ? ".claude/projects" : ".codex/sessions" }
}

enum ReadState { case ok, needsAuth, connectionError, rateLimited, serviceError, noData }

struct UsageWindow {
    var used: Double?
    var minutes: Int
    var resets: Date?
    func displayed(at now: Date, auth: Bool) -> Double? {
        if auth || (resets.map { $0 <= now } ?? false) { return nil }
        return used
    }
}

struct UsageReading {
    var session: UsageWindow?
    var weekly: UsageWindow?
    var state: ReadState = .noData
    var lastSuccess: Date?
    var retryAfter: Date?
    var message = ""

    static func failure(_ state: ReadState, _ message: String) -> UsageReading {
        UsageReading(state: state, message: message)
    }
    static func success(_ session: UsageWindow?, _ weekly: UsageWindow?) -> UsageReading {
        UsageReading(session: session, weekly: weekly,
                     state: session?.used == nil && weekly?.used == nil ? .noData : .ok,
                     lastSuccess: Date())
    }
}

enum SourceError: Error { case invalidData, unavailable, timeout, cancelled }

func jsonObject(_ data: Data) throws -> [String: Any] {
    guard let value = try JSONSerialization.jsonObject(with: data) as? [String: Any] else {
        throw SourceError.invalidData
    }
    return value
}

func number(_ value: Any?) -> Double? {
    guard let n = value as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite else { return nil }
    return n.doubleValue
}

func epoch(_ value: Any?, milliseconds: Bool = false) -> Date? {
    guard let n = number(value) else { return nil }
    let seconds = n / (milliseconds ? 1000 : 1)
    guard seconds >= -62135596800, seconds <= 253402300799 else { return nil }
    return Date(timeIntervalSince1970: seconds)
}

// Una consulta cancelada no puede iniciar más procesos ni publicar resultados.
final class Cancellation {
    private let lock = NSLock()
    private var stopped = false
    private var actions: [UUID: () -> Void] = [:]
    var isCancelled: Bool { lock.lock(); defer { lock.unlock() }; return stopped }
    func register(_ action: @escaping () -> Void) -> UUID {
        let id = UUID()
        lock.lock()
        if stopped { lock.unlock(); action() } else { actions[id] = action; lock.unlock() }
        return id
    }
    // El proceso queda registrado antes de que una cancelación pueda dar por terminada la consulta.
    func registerAndStart(_ action: @escaping () -> Void, start: () throws -> Void) throws -> UUID {
        lock.lock(); defer { lock.unlock() }
        guard !stopped else { throw SourceError.cancelled }
        try start()
        let id = UUID(); actions[id] = action; return id
    }
    func remove(_ id: UUID) { lock.lock(); actions.removeValue(forKey: id); lock.unlock() }
    func cancel() {
        lock.lock(); stopped = true; let callbacks = Array(actions.values); actions.removeAll(); lock.unlock()
        callbacks.forEach { $0() }
    }
    func check() throws { if isCancelled { throw SourceError.cancelled } }
}
