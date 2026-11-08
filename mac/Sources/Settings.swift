import Foundation

struct Settings: Codable {
    var X: Double?
    var Y: Double?
    var Variant = 0
    var View = 0
    var Show = 0
    // Si es verdadero, la apariencia elegida al instalar no se puede cambiar desde el widget.
    var FixedVariant = false
    var DockSide: String?
    var DockY: Double?

    static var directory: URL {
        FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/UsageOrbit")
    }
    static var file: URL { directory.appendingPathComponent("settings.json") }
    static func load() -> Settings {
        guard let data = try? Data(contentsOf: file), let object = try? jsonObject(data) else { return Settings() }
        var s = Settings()
        func coordinate(_ key: String) -> Double? {
            guard let n = number(object[key]), abs(n) <= 1_000_000 else { return nil }; return n
        }
        s.X = coordinate("X"); s.Y = coordinate("Y"); s.DockY = coordinate("DockY")
        s.Variant = number(object["Variant"]) == 1 ? 1 : 0
        s.View = number(object["View"]) == 1 ? 1 : 0
        let show = number(object["Show"])
        s.Show = show == 1 ? 1 : show == 2 ? 2 : 0
        s.FixedVariant = object["FixedVariant"] as? Bool ?? false
        if let side = object["DockSide"] as? String, side == "left" || side == "right" { s.DockSide = side }
        return s
    }
    func save() throws {
        try FileManager.default.createDirectory(at: Self.directory, withIntermediateDirectories: true)
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(self).write(to: Self.file, options: .atomic)
    }
    var assistants: [Assistant] { Show == 1 ? [.claude] : Show == 2 ? [.codex] : Assistant.allCases }
}
