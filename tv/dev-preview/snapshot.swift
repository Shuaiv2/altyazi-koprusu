// SPDX-License-Identifier: AGPL-3.0-only
// TV arayüzünün 1920x1080 ekran görüntülerini WKWebView ile çeker (yalnız geliştirme; pakete girmez).
// Kullanım: snapshot <url> <adımlar.json> <çıktı_klasörü>
// adımlar.json: [{"js": "...", "wait": 1.0, "shot": "01-ad.png"}, ...]  ("shot" isteğe bağlı)
import AppKit
import WebKit

struct Step: Decodable { let js: String?; let wait: Double?; let shot: String? }

let args = CommandLine.arguments
guard args.count == 4 else { print("kullanım: snapshot <url> <adımlar.json> <çıktı>"); exit(2) }
let steps = try! JSONDecoder().decode([Step].self, from: Data(contentsOf: URL(fileURLWithPath: args[2])))
let outDir = URL(fileURLWithPath: args[3])
try? FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)

let app = NSApplication.shared
app.setActivationPolicy(.prohibited)
let config = WKWebViewConfiguration()
config.websiteDataStore = .nonPersistent()
let web = WKWebView(frame: NSRect(x: 0, y: 0, width: 1920, height: 1080), configuration: config)
let window = NSWindow(contentRect: web.frame, styleMask: [.borderless], backing: .buffered, defer: false)
window.contentView = web
web.load(URLRequest(url: URL(string: args[1])!))

func run(_ i: Int) {
    if i >= steps.count { exit(0) }
    let step = steps[i]
    let next = {
        DispatchQueue.main.asyncAfter(deadline: .now() + (step.wait ?? 0.3)) {
            guard let name = step.shot else { run(i + 1); return }
            let cfg = WKSnapshotConfiguration()
            cfg.rect = web.bounds
            cfg.snapshotWidth = 1920
            web.takeSnapshot(with: cfg) { image, error in
                if let image, let tiff = image.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff),
                   let png = rep.representation(using: .png, properties: [:]) {
                    try! png.write(to: outDir.appendingPathComponent(name))
                    print("çekildi: \(name)")
                } else { print("HATA \(name): \(String(describing: error))") }
                run(i + 1)
            }
        }
    }
    if let js = step.js {
        web.evaluateJavaScript(js) { _, error in
            if let error { print("js hata (adım \(i)): \(error)") }
            next()
        }
    } else { next() }
}
DispatchQueue.main.asyncAfter(deadline: .now() + 2.0) { run(0) }
app.run()
