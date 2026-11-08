import Foundation

enum CodexSource {
    static func read(_ cancellation: Cancellation) -> UsageReading {
        do {
            let executable = try CLI.find("codex", cancellation: cancellation)
            let child = try ChildProcess(executable, ["app-server"], timeout: 25, cancellation: cancellation)
            defer { child.stop() }
            try child.send(["id": 1, "method": "initialize", "params": ["clientInfo": ["name": "usage_orbit", "title": "Usage Orbit", "version": "1.0.0"]]])
            _ = try receive(child, id: 1)
            try child.send(["method": "initialized"])
            try child.send(["id": 2, "method": "account/read", "params": ["refreshToken": false]])
            let account = try receive(child, id: 2)
            if (account["account"] == nil || account["account"] is NSNull), account["requiresOpenaiAuth"] as? Bool == true {
                return .failure(.needsAuth, "Ejecuta: codex login")
            }
            try child.send(["id": 3, "method": "account/rateLimits/read"])
            return try parse(receive(child, id: 3))
        } catch let error as RPCError {
            let text = error.message.lowercased()
            let state: ReadState
            if text.contains("429") || text.contains("too many requests") { state = .rateLimited }
            else if ["error sending request", "connect", "timed out", "dns"].contains(where: { text.contains($0) }) { state = .connectionError }
            else { state = .serviceError }
            return .failure(state, "Codex informó un error al consultar sus límites.")
        } catch SourceError.timeout {
            return .failure(.connectionError, "Codex no respondió en 25 s.")
        } catch {
            return .failure(.serviceError, "No se pudo consultar Codex. Comprueba la instalación y los permisos de la CLI.")
        }
    }
    private struct RPCError: Error { let message: String }
    private static func receive(_ child: ChildProcess, id: Int) throws -> [String: Any] {
        while let line = try child.line() {
            let message = try jsonObject(line)
            if number(message["id"]) != Double(id) { continue }
            if let error = message["error"] as? [String: Any] { throw RPCError(message: error["message"] as? String ?? "") }
            guard let result = message["result"] as? [String: Any] else { throw SourceError.invalidData }
            return result
        }
        throw SourceError.unavailable
    }
    static func parse(_ result: [String: Any]) throws -> UsageReading {
        let byID = result["rateLimitsByLimitId"] as? [String: Any]
        guard let limits = byID?["codex"] as? [String: Any] ?? result["rateLimits"] as? [String: Any] else { throw SourceError.invalidData }
        var session: UsageWindow?, weekly: UsageWindow?
        for key in ["primary", "secondary"] {
            guard let item = limits[key] as? [String: Any], let duration = number(item["windowDurationMins"]),
                  duration > 0, duration <= Double(Int32.max), duration.rounded() == duration else { continue }
            let window = UsageWindow(used: number(item["usedPercent"]), minutes: Int(duration), resets: epoch(item["resetsAt"]))
            if duration >= 10080 {
                if weekly == nil || window.minutes < weekly!.minutes { weekly = window }
            } else if session == nil || window.minutes < session!.minutes { session = window }
        }
        return .success(session, weekly)
    }
}
