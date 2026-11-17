import AppKit
// Altyazı Köprüsü simgesi: koyu zemin, köprü yayı, iki altyazı çubuğu. Metin içermez (banner hariç).
func png(_ w: Int, _ h: Int, _ path: String, _ draw: (CGContext, CGFloat, CGFloat) -> Void) {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: w, pixelsHigh: h, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    draw(NSGraphicsContext.current!.cgContext, CGFloat(w), CGFloat(h))
    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: path))
}
let bg = CGColor(red: 0.07, green: 0.11, blue: 0.20, alpha: 1)
let teal = CGColor(red: 0.18, green: 0.78, blue: 0.72, alpha: 1)
let white = CGColor(red: 1, green: 1, blue: 1, alpha: 1)
let yellow = CGColor(red: 1, green: 0.80, blue: 0.25, alpha: 1)
func glyph(_ c: CGContext, _ x: CGFloat, _ y: CGFloat, _ s: CGFloat) {
    // köprü yayı
    c.setStrokeColor(teal); c.setLineWidth(s * 0.07); c.setLineCap(.round)
    c.addArc(center: CGPoint(x: x + s/2, y: y + s*0.30), radius: s*0.34, startAngle: .pi*0.12, endAngle: .pi*0.88, clockwise: false)
    c.strokePath()
    // altyazı çubukları
    c.setFillColor(white)
    c.addPath(CGPath(roundedRect: CGRect(x: x + s*0.16, y: y + s*0.30, width: s*0.68, height: s*0.10), cornerWidth: s*0.05, cornerHeight: s*0.05, transform: nil)); c.fillPath()
    c.setFillColor(yellow)
    c.addPath(CGPath(roundedRect: CGRect(x: x + s*0.28, y: y + s*0.14, width: s*0.44, height: s*0.10), cornerWidth: s*0.05, cornerHeight: s*0.05, transform: nil)); c.fillPath()
}
let out = CommandLine.arguments[1]
png(1024, 1024, out + "/icon-1024.png") { c, w, h in
    c.setFillColor(bg); c.addPath(CGPath(roundedRect: CGRect(x: 0, y: 0, width: w, height: h), cornerWidth: w*0.22, cornerHeight: w*0.22, transform: nil)); c.fillPath()
    glyph(c, w*0.1, h*0.12, w*0.8)
}
png(1280, 720, out + "/banner-1280x720.png") { c, w, h in
    c.setFillColor(bg); c.fill(CGRect(x: 0, y: 0, width: w, height: h))
    glyph(c, w*0.05, h*0.2, h*0.6)
    let attr: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: h*0.11, weight: .bold), .foregroundColor: NSColor.white]
    NSAttributedString(string: "Altyazı Köprüsü", attributes: attr).draw(at: CGPoint(x: w*0.05 + h*0.66, y: h*0.43))
}
