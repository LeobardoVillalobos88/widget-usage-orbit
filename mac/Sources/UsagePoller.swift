import Foundation

// El estado del sondeo se usa exclusivamente desde el hilo principal.
final class UsagePoller {
    let assistant: Assistant
    private(set) var reading = UsageReading(message: "Esperando la primera lectura.")
    private(set) var nextAttempt = Date.distantPast
    private(set) var busy = false
    private(set) var notice: String?
    var changed: (() -> Void)?
    private var failures = 0
    private var lastResetQuery = Date.distantPast
    private var requestedSession: Date?
    private var requestedWeekly: Date?
    private let cancellation = Cancellation()

    init(_ assistant: Assistant) { self.assistant = assistant }
    func tick() {
        guard !busy, !cancellation.isCancelled else { return }
        let now = Date()
        let session = reading.session?.resets.flatMap { $0 <= now ? $0 : nil }
        let weekly = reading.weekly?.resets.flatMap { $0 <= now ? $0 : nil }
        let resetDue = ((session != nil && session != requestedSession) || (weekly != nil && weekly != requestedWeekly)) && now.timeIntervalSince(lastResetQuery) >= 60
        guard now >= nextAttempt || resetDue else { return }
        if let retry = reading.retryAfter, retry > now { return }
        if resetDue { requestedSession = session; requestedWeekly = weekly; lastResetQuery = now }
        start()
    }
    func refresh() {
        guard !cancellation.isCancelled else { return }
        if let retry = reading.retryAfter, retry > Date() {
            notice = "Espera el plazo indicado antes de actualizar."; changed?(); return
        }
        if !busy { start() }
    }
    private func start() {
        busy = true; notice = "Actualizando…"; changed?()
        let assistant = self.assistant, token = cancellation
        DispatchQueue.global(qos: .utility).async { [weak self] in
            let result = assistant == .claude ? ClaudeSource.read(token) : CodexSource.read(token)
            DispatchQueue.main.async {
                guard let self = self, !token.isCancelled else { return }
                self.accept(result)
            }
        }
    }
    private func accept(_ result: UsageReading) {
        var result = result
        if result.state == .ok || result.state == .noData {
            failures = 0; nextAttempt = Date().addingTimeInterval(300)
        } else {
            result.session = reading.session; result.weekly = reading.weekly; result.lastSuccess = reading.lastSuccess
            nextAttempt = Date().addingTimeInterval(min(30, pow(2, Double(min(failures, 5)))) * 60)
            failures = min(failures + 1, 5)
        }
        if let retry = result.retryAfter, retry > nextAttempt { nextAttempt = retry }
        reading = result; busy = false; notice = nil; changed?()
    }
    func stop() { cancellation.cancel(); changed = nil }
    deinit { cancellation.cancel() }
}
