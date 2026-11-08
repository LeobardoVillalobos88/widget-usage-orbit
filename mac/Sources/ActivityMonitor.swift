import Foundation
import CoreServices

final class ActivityMonitor {
    private let root: URL
    private var stream: FSEventStreamRef?
    private var pending: DispatchWorkItem?
    private var scanAgain = false
    private let cancellation = Cancellation()
    private var stopped = false
    private(set) var lastWrite = Date.distantPast
    var changed: (() -> Void)?
    var active: Bool { Date().timeIntervalSince(lastWrite) < 90 }

    init(_ assistant: Assistant) {
        root = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(assistant.activityPath)
        var watched = root
        while !FileManager.default.fileExists(atPath: watched.path), watched.path != "/" {
            watched.deleteLastPathComponent()
        }
        var context = FSEventStreamContext(version: 0, info: Unmanaged.passUnretained(self).toOpaque(), retain: nil, release: nil, copyDescription: nil)
        let flags = FSEventStreamCreateFlags(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagUseCFTypes | kFSEventStreamCreateFlagWatchRoot)
        stream = FSEventStreamCreate(nil, { _, info, count, paths, flags, _ in
            guard let info = info else { return }
            let monitor = Unmanaged<ActivityMonitor>.fromOpaque(info).takeUnretainedValue()
            let names = Unmanaged<NSArray>.fromOpaque(paths).takeUnretainedValue()
            for index in 0..<count {
                guard let path = names[index] as? String else { continue }
                let dropped = flags[index] & FSEventStreamEventFlags(kFSEventStreamEventFlagMustScanSubDirs | kFSEventStreamEventFlagRootChanged) != 0
                if dropped || path == monitor.root.path || path.hasPrefix(monitor.root.path + "/") || monitor.root.path.hasPrefix(path + "/") {
                    monitor.scheduleScan(); break
                }
            }
        }, &context, [watched.path] as CFArray, FSEventStreamEventId(kFSEventStreamEventIdSinceNow), 0.5, flags)
        if let stream = stream {
            FSEventStreamSetDispatchQueue(stream, DispatchQueue.main)
            if !FSEventStreamStart(stream) {
                FSEventStreamInvalidate(stream); FSEventStreamRelease(stream); self.stream = nil
            }
        }
        scheduleScan()
    }
    private func scheduleScan() {
        guard !stopped else { return }
        if pending != nil { scanAgain = true; return }
        let root = self.root, cancellation = self.cancellation
        let work = DispatchWorkItem { [weak self] in
            let keys: [URLResourceKey] = [.isRegularFileKey, .contentModificationDateKey]
            let files = FileManager.default.enumerator(at: root, includingPropertiesForKeys: keys, options: [])
            var latest = Date.distantPast
            while let file = files?.nextObject() as? URL {
                if cancellation.isCancelled { return }
                guard file.pathExtension == "jsonl", let values = try? file.resourceValues(forKeys: Set(keys)),
                      values.isRegularFile == true, let date = values.contentModificationDate else { continue }
                latest = max(latest, date)
            }
            DispatchQueue.main.async {
                guard let self = self, !self.stopped else { return }
                self.pending = nil
                self.lastWrite = min(latest, Date())
                self.changed?()
                if self.scanAgain { self.scanAgain = false; self.scheduleScan() }
            }
        }
        pending = work
        DispatchQueue.global(qos: .utility).asyncAfter(deadline: .now() + 0.5, execute: work)
    }
    func stop() {
        stopped = true; cancellation.cancel(); pending?.cancel(); pending = nil; changed = nil
        if let stream = stream {
            FSEventStreamStop(stream); FSEventStreamInvalidate(stream); FSEventStreamRelease(stream)
            self.stream = nil
        }
    }
    deinit { stop() }
}
