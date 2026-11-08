import AppKit

@MainActor
enum Mascots {
    static let riseSteps = 4
    static let workFrames = 16
    private static let colors: [Character: NSColor] = [
        "O": color(0xEE7B30), "D": color(0xC25E20), "k": color(0x1C1A1E),
        "#": color(0x232D66), "b": color(0x6E9BFA), "d": color(0x4A6FD0), "c": color(0x8FE6FF),
        "M": color(0x6F7786), "L": color(0xAEB6C2), "B": color(0x4E5563), "g": color(0xEAF1FF), "G": color(0xB9D4FF)
    ]
    private static let codexBody = [
        "........#######........",
        "......##bbbbbbb##......",
        ".....#bbbbbbbbbbb#.....",
        "...###bbbbbbbbbbb###...",
        "..#bbbbbbbbbbbbbbbbb#..",
        ".#bbbbbbbbbbbbbbbbbbb#.",
        "#bbbbbbbbbbbbbbbbbbbbb#",
        "#bbb###############bbb#",
        "#dbb###############bbb#",
        "#dbb###############bbb#",
        "#dbb###############bbb#",
        ".#bb###############bb#.",
        ".#db###############bb#.",
        ".#db###############bb#.",
        ".#db###############bb#.",
        "..#dbb###########bbb#..",
        "...#ddbbbbbbbbbbbdd#...",
        "....##ddddddddddd##....",
        ".....#bbbbbbbbbbb#.....",
        "....#b#bbbbbbbbb#b#....",
        "...#bb#bcbbbbbbb#bb#...",
        "...#bb#bbcbbbbbb#bb#...",
        "...#dd#bcbbccbbb#dd#...",
        "....##.#ddddddd#.##....",
        ".......#bb#.#bb#.......",
        ".......#dd#.#dd#.......",
        ".......####.####......."
    ]
    private static let openLid = [(0, 0), (0, -1), (-1, -2), (-1, -3), (-2, -4), (-2, -5), (-3, -6), (-3, -7)]
    private static let claudeRise = rise(true), codexRise = rise(false)
    private static let claudeWork = work(true), codexWork = work(false)
    static func rising(_ claude: Bool, _ step: Int) -> NSImage { (claude ? claudeRise : codexRise)[step] }
    static func working(_ claude: Bool, _ frame: Int) -> NSImage { (claude ? claudeWork : codexWork)[frame] }

    private static func rise(_ claude: Bool) -> [NSImage] {
        [render(claude ? claudeFront() : codexFront())] + (1...riseSteps).map { render(side(claude, $0 - 1, -1, false, 0)) }
    }
    private static func work(_ claude: Bool) -> [NSImage] {
        (0..<workFrames).map { render(side(claude, 3, $0 % 2, $0 % 4 < 2, gaze($0))) }
    }
    private static func canvas(_ size: Int) -> [[Character]] { Array(repeating: Array(repeating: ".", count: size), count: size) }
    private static func fill(_ canvas: inout [[Character]], _ x: Int, _ y: Int, _ width: Int, _ height: Int, _ cell: Character) {
        for row in y..<(y + height) { for column in x..<(x + width) { canvas[row][column] = cell } }
    }
    private static func stamp(_ canvas: inout [[Character]], _ art: [String], _ x: Int, _ y: Int) {
        for (row, line) in art.enumerated() {
            for (column, cell) in line.enumerated() where cell != "." { canvas[y + row][x + column] = cell }
        }
    }
    // Mirada al teclear: casi siempre a la pantalla, con un parpadeo y vistazos al teclado y a la laptop.
    // 0 normal, 1 parpadeo, 2 hacia abajo, 3 hacia la laptop.
    private static func gaze(_ frame: Int) -> Int {
        if frame == 6 { return 1 }
        if (10...13).contains(frame) { return 2 }
        return frame >= 14 ? 3 : 0
    }
    private static func claudeFront() -> [[Character]] {
        var c = canvas(24)
        for x in [4, 8, 13, 17] { fill(&c, x, 16, 2, 4, "O"); fill(&c, x + 2, 16, 1, 4, "D") }
        fill(&c, 2, 4, 20, 12, "O"); fill(&c, 2, 15, 20, 1, "D")
        fill(&c, 0, 8, 2, 4, "O"); fill(&c, 22, 8, 2, 4, "O")
        fill(&c, 5, 7, 2, 3, "k"); fill(&c, 17, 7, 2, 3, "k")
        return c
    }
    private static func codexFront() -> [[Character]] {
        var c = canvas(27); stamp(&c, codexBody, 2, 0); face(&c, 8, 9, true, false); return c
    }
    // La tapa pasa de ausente a cerrada, a medio abrir y abierta; las manos alternan al teclear.
    private static func side(_ claude: Bool, _ lid: Int, _ arm: Int, _ glow: Bool, _ gaze: Int) -> [[Character]] {
        var c = canvas(claude ? 24 : 27)
        if claude {
            for x in [9, 12, 17, 20] { fill(&c, x, 17, 1, 3, "O"); fill(&c, x + 1, 17, 1, 3, "D") }
            fill(&c, 8, 7, 16, 10, "O"); fill(&c, 8, 16, 16, 1, "D")
            if gaze == 1 {
                fill(&c, 11, 10, 2, 1, "k"); fill(&c, 16, 10, 2, 1, "k")
            } else {
                let dx = gaze == 3 ? -1 : 0, dy = gaze == 2 ? 1 : 0
                fill(&c, 11 + dx, 8 + dy, 2, 3, "k"); fill(&c, 16 + dx, 8 + dy, 2, 3, "k")
            }
            laptop(&c, 3, 19, 1, lid, glow, 6)
            // Teclea con las dos manos: la cercana en naranja y la lejana en sombra suben y bajan por turnos.
            if arm >= 0 {
                fill(&c, 6, arm == 0 ? 14 : 16, 2, 2, "D")
                fill(&c, 4, arm == 0 ? 16 : 14, 2, 2, "O")
            }
        } else {
            stamp(&c, codexBody, 0, 0)
            // El cursor parpadea y la cara sigue la misma mirada que Claude, hacia su laptop a la derecha.
            let dx = gaze == 3 ? 1 : 0, dy = gaze == 2 ? 1 : 0
            face(&c, 8 + dx, 9 + dy, arm < 0 || glow, gaze == 1)
            if arm >= 0 { fill(&c, 19, 22 + arm, 2, 2, "b") }
            laptop(&c, 23, 26, -1, lid, glow, 8)
        }
        return c
    }
    // Con blink el símbolo > se cierra en una línea, como un parpadeo.
    private static func face(_ c: inout [[Character]], _ x: Int, _ y: Int, _ cursor: Bool, _ blink: Bool) {
        if blink { fill(&c, x, y + 2, 3, 1, "c") } else { stamp(&c, ["c..", ".c.", "..c", ".c.", "c.."], x, y) }
        if cursor { fill(&c, x + 6, y + 4, 4, 1, "c") }
    }
    private static func laptop(_ c: inout [[Character]], _ hingeX: Int, _ floorY: Int, _ toward: Int, _ lid: Int, _ glow: Bool, _ length: Int) {
        if lid == 0 { return }
        for i in 0..<length {
            c[floorY][hingeX + i * toward] = "B"
            if i > 0 { c[floorY - 1][hingeX + i * toward] = "L" }
        }
        let hingeY = floorY - 1
        if lid == 1 { for i in 0..<(length - 1) { c[hingeY - 1][hingeX + i * toward] = "M" }; return }
        if lid == 2 { for i in 0..<5 { c[hingeY - i][hingeX + i * toward] = "M" }; return }
        for (i, point) in openLid.enumerated() {
            let x = hingeX + point.0 * toward, y = hingeY + point.1
            c[y][x] = "M"
            if i > 0 { c[y][x + toward] = glow ? "g" : "G" }
        }
    }
    private static func render(_ canvas: [[Character]]) -> NSImage {
        let size = canvas.count * 4
        let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size,
                                      bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                                      colorSpaceName: .deviceRGB, bytesPerRow: size * 4, bitsPerPixel: 32)!
        for y in 0..<size { for x in 0..<size { bitmap.setColor(colors[canvas[y / 4][x / 4]] ?? .clear, atX: x, y: y) } }
        let image = NSImage(size: NSSize(width: size, height: size)); image.addRepresentation(bitmap); return image
    }
}

@MainActor
final class MascotAnimator {
    let claude: Bool
    private var step = 0, frame = -1
    init(_ assistant: Assistant) { claude = assistant == .claude }
    func needsTicks(_ active: Bool) -> Bool { active || step > 0 }
    var current: NSImage { step == Mascots.riseSteps && frame >= 0 ? Mascots.working(claude, frame) : Mascots.rising(claude, step) }
    func advance(_ active: Bool) {
        if !active { frame = -1; step = max(0, step - 1) }
        else if step < Mascots.riseSteps { frame = -1; step += 1 }
        else { frame = (frame + 1) % Mascots.workFrames }
    }
}
