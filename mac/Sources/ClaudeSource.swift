import Foundation

// La fuente OAuth de Claude no está documentada públicamente. Su contrato queda aislado aquí.
enum ClaudeSource {
    private struct Credential { var token: String; var expires: Date? }

    static func read(_ cancellation: Cancellation) -> UsageReading {
        do {
            guard var credential = try credentials(cancellation) else { return login() }
            if let expires = credential.expires, expires <= Date() {
                credential.token = ""
                let executable = try CLI.find("claude", cancellation: cancellation)
                let child = try ChildProcess(executable, ["plugin", "list"], timeout: 30, cancellation: cancellation)
                // La CLI renueva sus credenciales; su salida nunca se imprime ni se guarda.
                _ = try child.collect()
                child.stop()
                guard let refreshed = try credentials(cancellation), refreshed.expires.map({ $0 > Date() }) ?? true else { return login() }
                credential = refreshed
            }
            try cancellation.check()
            var request = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
            request.httpMethod = "GET"
            request.timeoutInterval = 25
            request.setValue("Bearer " + credential.token, forHTTPHeaderField: "Authorization")
            credential.token = ""
            request.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
            request.setValue("UsageOrbit/1.0", forHTTPHeaderField: "User-Agent")
            let configuration = URLSessionConfiguration.ephemeral
            configuration.urlCache = nil
            configuration.httpCookieStorage = nil
            configuration.urlCredentialStorage = nil
            configuration.timeoutIntervalForResource = 25
            let session = URLSession(configuration: configuration, delegate: NoRedirect(), delegateQueue: nil)
            defer { session.invalidateAndCancel() }
            let response = ResponseBox()
            let task = session.dataTask(with: request) { data, reply, error in
                response.finish(data, reply as? HTTPURLResponse, error)
            }
            let id = cancellation.register { task.cancel() }
            defer { cancellation.remove(id) }
            task.resume()
            guard response.done.wait(timeout: .now() + 26) == .success else {
                task.cancel(); return .failure(.connectionError, "Claude no respondió a tiempo.")
            }
            try cancellation.check()
            let (data, reply, error) = response.result()
            if error != nil { return .failure(.connectionError, "No se pudo conectar con Claude.") }
            guard let http = reply else { throw SourceError.invalidData }
            if http.statusCode == 401 || http.statusCode == 403 { return login() }
            if !(200...299).contains(http.statusCode) {
                var result = UsageReading.failure(http.statusCode == 429 ? .rateLimited : .serviceError,
                                                  "Claude devolvió HTTP \(http.statusCode).")
                result.retryAfter = retryDate(http.value(forHTTPHeaderField: "Retry-After"))
                return result
            }
            guard let data = data else { throw SourceError.invalidData }
            return try parse(jsonObject(data))
        } catch SourceError.timeout {
            return .failure(.connectionError, "Claude no respondió a tiempo.")
        } catch {
            return .failure(.serviceError, "No se pudo leer el consumo de Claude. Comprueba la CLI y sus permisos.")
        }
    }

    private static func login() -> UsageReading { .failure(.needsAuth, "Ejecuta: claude auth login") }

    private static func credentials(_ cancellation: Cancellation) throws -> Credential? {
        let child = try ChildProcess("/usr/bin/security", ["find-generic-password", "-s", "Claude Code-credentials", "-w"], timeout: 15, cancellation: cancellation)
        defer { child.stop() }
        let (keychain, status) = try child.collect()
        let data: Data
        if status == 0 { data = keychain }
        else {
            // errSecItemNotFound se convierte en el código de salida 44 de security.
            guard status == (44 << 8) else { throw SourceError.unavailable }
            let file = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".claude/.credentials.json")
            guard FileManager.default.fileExists(atPath: file.path) else { return nil }
            data = try Data(contentsOf: file)
        }
        let object = try jsonObject(data)
        guard let oauth = object["claudeAiOauth"] as? [String: Any],
              let token = oauth["accessToken"] as? String, !token.isEmpty else { return nil }
        return Credential(token: token, expires: epoch(oauth["expiresAt"], milliseconds: true))
    }

    static func parse(_ object: [String: Any]) throws -> UsageReading {
        if !object.isEmpty && object["five_hour"] == nil && object["seven_day"] == nil { throw SourceError.invalidData }
        return try .success(window(object["five_hour"], minutes: 300), window(object["seven_day"], minutes: 10080))
    }
    private static func window(_ raw: Any?, minutes: Int) throws -> UsageWindow? {
        guard let raw = raw, !(raw is NSNull) else { return nil }
        guard let object = raw as? [String: Any] else { throw SourceError.invalidData }
        var reset: Date?
        if let text = object["resets_at"] as? String {
            let formatter = ISO8601DateFormatter()
            formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            reset = formatter.date(from: text)
            if reset == nil { formatter.formatOptions = [.withInternetDateTime]; reset = formatter.date(from: text) }
        }
        return UsageWindow(used: number(object["utilization"]), minutes: minutes, resets: reset)
    }
    private static func retryDate(_ value: String?) -> Date? {
        guard let value = value else { return nil }
        if let seconds = Double(value), seconds.isFinite, seconds >= 0 { return Date().addingTimeInterval(seconds) }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(secondsFromGMT: 0)
        formatter.dateFormat = "EEE',' dd MMM yyyy HH':'mm':'ss z"
        return formatter.date(from: value)
    }
}

private final class NoRedirect: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse,
                    newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        completionHandler(nil)
    }
}

private final class ResponseBox {
    let done = DispatchSemaphore(value: 0)
    private let lock = NSLock()
    private var data: Data?
    private var reply: HTTPURLResponse?
    private var error: Error?
    func finish(_ data: Data?, _ reply: HTTPURLResponse?, _ error: Error?) {
        lock.lock(); self.data = data; self.reply = reply; self.error = error; lock.unlock()
        done.signal()
    }
    func result() -> (Data?, HTTPURLResponse?, Error?) {
        lock.lock(); defer { lock.unlock() }; return (data, reply, error)
    }
}
