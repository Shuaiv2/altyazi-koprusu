// SPDX-License-Identifier: AGPL-3.0-only

import CoreGraphics
import Foundation

/// Tam kare OCR'ından gelen tek satır. Kutu tüm karede normalize (0...1), başlangıç SOL-ÜST.
public struct ScreenLine: Equatable {
    public var text: String
    public var rect: CGRect
    public var confidence: Float
    public init(text: String, rect: CGRect, confidence: Float = 1) {
        self.text = text; self.rect = rect; self.confidence = confidence
    }
}

public enum ScreenMode: String, Equatable { case game, map, menu }

public enum ScreenKind: String, Equatable { case objective, info, notify, description }

/// Sınıflandırılmış ekran metni bloğu.
public struct ScreenBlock: Equatable {
    public var kind: ScreenKind
    /// Çevrilecek gövde metni (satırlar boşlukla birleşmiş, düğme simgeleri normalleşmiş).
    public var text: String
    /// description için BÜYÜK HARF başlık (gövdeden ayrı çevrilir).
    public var title: String?
    /// notify için gönderen adı (çevrilmez).
    public var speaker: String?
    /// Kareye göre normalize, sol-üst. Başlık varsa kutuya dahildir.
    public var box: CGRect
    public var lineHeight: Double
    /// İzleyici anahtarı: tür + yaklaşık yer ("notify-bottom", "notify-top", ...).
    public var slot: String
}

/// Tam kare OCR satırlarından oyun içi metin bloklarını çıkaran saf mantık (Vision/arayüz bağımlılığı yok).
/// Oyun modunda yalnızca objective/info/notify üretilir; serbest metin (tabela, reklam panosu) asla çevrilmez.
public enum ScreenText {
    struct L {
        var text: String
        var raw = ""   // yalnızca düğme normalleşmesi (başlık için; OcrNoise baştaki sayıyı atabiliyor)
        var x0: Double, y0: Double, x1: Double, y1: Double
        var h: Double { y1 - y0 }
        var cx: Double { (x0 + x1) / 2 }
        var words: Int { InfoBox.wordCount(text) }
    }

    private static func rx(_ p: String, _ o: NSRegularExpression.Options = [.caseInsensitive]) -> NSRegularExpression { try! NSRegularExpression(pattern: p, options: o) }
    private static func matches(_ r: NSRegularExpression, _ s: String) -> Bool { r.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) != nil }

    private static let mapPromptRx = rx("(ADD MARKER|RECENTER|CLOSE MENU|2D VIEW|3D VIEW|SET DESTINATION|REMOVE DESTINATION)")
    private static let menuSelectRx = rx("^\\W*SELECT\\b")
    private static let menuBackRx = rx("^\\W*(BACK|EXIT)\\b")
    private static let toastRx = rx("(Uzaktan\\s*Oynama|Remote\\s*Play)")
    private static let moneyRx = rx("^\\W{0,2}[\\$€£]?\\s*\\d[\\d,\\.]*\\W*$")
    private static let clockRx = rx("^\\s*\\d{1,2}\\s*[:.]\\s*\\d{2}\\s*([ap]\\.?m\\.?)?\\s*$")
    private static let distanceRx = rx("^\\s*\\d+([.,]\\d+)?\\s*(ft|mi|m|km|yd|yds)\\.?\\s*$")
    private static let gpsRx = rx("^\\s*(turn|keep|continue|head|take|make a|bear|merge)\\s+(left|right|straight|u-?turn|ahead)\\b")
    private static let callRx = rx("(incoming call|dialing|dialling|calling\\.|call ended|missed call|call failed|^\\s*\\d{1,2}\\s*[:=]\\s*\\d{2}\\b)")
    private static let arrowRx = rx("^\\s*[→➜➔>»]")

    static func isCall(_ t: String) -> Bool { matches(callRx, t) }
    static func hasLower(_ s: String) -> Bool { s.contains { $0.isLowercase } }
    static func isCapsOnly(_ s: String) -> Bool { s.contains(where: \.isLetter) && !hasLower(s) }

    /// Tüm satırlara bakarak ekran modu (ham metinle; BÜYÜK HARF istemleri bu yüzden görülür).
    public static func detectMode(_ lines: [ScreenLine]) -> ScreenMode {
        let texts = lines.filter { $0.confidence >= 0.3 }.map { $0.text.trimmingCharacters(in: .whitespaces) }
        if texts.contains(where: { matches(mapPromptRx, $0) }) { return .map }
        let lowRight = lines.filter { $0.rect.minY > 0.84 && $0.rect.minX > 0.7 }.map { $0.text }
        if lowRight.contains(where: { matches(menuSelectRx, $0) }), lowRight.contains(where: { matches(menuBackRx, $0) }) { return .menu }
        return .game
    }

    /// Ortak elemeler: düşük güven, PS bildirimi, kenara değen (kesik dünya metni), para/saat/mesafe/GPS, arama arayüzü, ok istemi.
    static func prepare(_ lines: [ScreenLine]) -> [L] {
        var out: [L] = []
        for line in lines {
            guard line.confidence >= 0.5 else { continue }
            let raw = line.text.trimmingCharacters(in: .whitespacesAndNewlines)
            if matches(toastRx, raw) { continue }
            let r = line.rect
            guard r.minX >= 0.003, r.maxX <= 0.997, r.minY >= 0, r.maxY <= 1.0 else { continue }
            let text = InfoBox.stripTrailingGlyph(OcrNoise.clean(GlyphNormalizer.normalize(raw)))
            guard !text.isEmpty, text.contains(where: \.isLetter) else { continue }
            if matches(moneyRx, text) || matches(clockRx, text) || matches(distanceRx, text) || matches(gpsRx, text) { continue }
            if isCall(text) || matches(arrowRx, raw) { continue }
            out.append(L(text: text, raw: GlyphNormalizer.normalize(raw), x0: Double(r.minX), y0: Double(r.minY), x1: Double(r.maxX), y1: Double(r.maxY)))
        }
        return out
    }

    /// Satırları bloklara gruplar: sol ya da orta hizalı, dikeyde bitişik (boşluk < 1 satır yüksekliği), benzer yükseklik.
    /// Soldaki öğretici kutularda satır başındaki simge ikinci satırı sağa iter; bu yüzden sol kenarı gruptan en çok 0.035 içeride olan satır da kabul edilir.
    static func group(_ items: [L]) -> [[L]] {
        var groups: [[L]] = []
        for it in items.sorted(by: { $0.y0 < $1.y0 }) {
            var best: (idx: Int, gap: Double)?
            for (i, g) in groups.enumerated() {
                let p = g.last!
                let gap = it.y0 - p.y1
                let ref = max(p.h, it.h, 0.001)
                guard gap >= -0.2 * ref, gap < 1.0 * ref else { continue }
                let ratio = min(p.h, it.h) / ref
                guard ratio >= 0.55 else { continue }
                let gx0 = g.map(\.x0).min()!
                let left = abs(it.x0 - p.x0) < 0.015 || abs(it.x0 - gx0) < 0.015
                let center = abs(it.cx - p.cx) < 0.015
                let indent = gx0 < 0.08 && it.x0 >= gx0 - 0.01 && it.x0 <= gx0 + 0.035
                guard left || center || indent else { continue }
                if best == nil || gap < best!.gap { best = (i, gap) }
            }
            if let b = best { groups[b.idx].append(it) } else { groups.append([it]) }
        }
        return groups
    }

    private struct Cand {
        var lines: [L]
        var text: String { InfoBox.join(lines.map(\.text)) }
        var x0: Double { lines.map(\.x0).min()! }
        var x1: Double { lines.map(\.x1).max()! }
        var y0: Double { lines.map(\.y0).min()! }
        var y1: Double { lines.map(\.y1).max()! }
        var lh: Double { let h = lines.map(\.h).sorted(); return h[h.count / 2] }
        var rect: CGRect { CGRect(x: x0, y: y0, width: x1 - x0, height: y1 - y0) }
    }

    private static func isCapitalizedName(_ s: String) -> Bool {
        let w = s.split(whereSeparator: \.isWhitespace)
        guard (1...2).contains(w.count) else { return false }
        return w.allSatisfy { $0.first?.isUppercase == true && $0.allSatisfy { $0.isLetter || $0 == "'" || $0 == "-" } }
    }

    public static func analyze(_ lines: [ScreenLine]) -> (mode: ScreenMode, blocks: [ScreenBlock]) {
        let mode = detectMode(lines)
        let items = prepare(lines)
        switch mode {
        case .game: return (mode, gameBlocks(items))
        case .map, .menu: return (mode, descriptionBlocks(items))
        }
    }

    // MARK: Oyun modu

    private static func gameBlocks(_ items: [L]) -> [ScreenBlock] {
        // Küçük harfsiz satırlar (tabela) en baştan dışarıda.
        var cands = group(items.filter { hasLower($0.text) }).map { Cand(lines: $0) }
        var out: [ScreenBlock] = []

        // Bildirim: gönderen satırı (1-2 büyük harfli sözcük, mesajdan sağa girintili, daha küçük) + mesaj bloğu
        var used = Set<Int>()
        for (si, s) in cands.enumerated() where s.lines.count == 1 && s.x0 > 0.74 && s.y0 >= 0.75 && s.y0 <= 0.97 && isCapitalizedName(s.text) {
            guard let mi = cands.indices.first(where: { mi in
                let m = cands[mi]
                let gap = m.y0 - s.y1
                let off = s.x0 - m.x0
                return mi != si && !used.contains(mi) && gap >= -0.005 && gap < 1.3 * max(s.lh, m.lh) && off >= 0.012 && off <= 0.06
                    && s.lh <= 0.92 * m.lh && m.x1 >= 0.9 && m.x1 <= 0.997 && m.x0 > 0.74
            }) else { continue }
            let m = cands[mi]
            guard InfoBox.wordCount(m.text) >= 2 else { continue }
            used.insert(si); used.insert(mi)
            let box = m.rect.union(s.rect)
            out.append(ScreenBlock(kind: .notify, text: m.text, title: nil, speaker: s.text, box: box, lineHeight: m.lh, slot: "notify-bottom"))
        }
        cands = cands.enumerated().filter { !used.contains($0.offset) }.map(\.element)

        for c in cands {
            // Küçük harfle başlayan blok bir parçadır (kesik tabela/pano ya da simge arkasından kalan yazı): çevrilmez.
            if c.text.first?.isLowercase == true { continue }
            let words = InfoBox.wordCount(c.text)
            let cxMid = (c.x0 + c.x1) / 2
            if c.lines.count == 1, words >= 2, abs(cxMid - 0.5) <= 0.12, c.y0 > 0.93 {
                out.append(ScreenBlock(kind: .objective, text: c.text, title: nil, speaker: nil, box: c.rect, lineHeight: c.lh, slot: "objective"))
            } else if c.x0 < 0.08, c.y0 >= 0.18, c.y0 <= 0.55, words >= 3, InfoBox.isSentenceLike(c.text) {
                out.append(ScreenBlock(kind: .info, text: c.text, title: nil, speaker: nil, box: c.rect, lineHeight: c.lh, slot: "info"))
            } else if c.x0 > 0.74 {
                if c.y0 >= 0.75, c.y0 <= 0.97, c.x1 >= 0.9, words >= 2 {
                    // gönderensiz bildirim ("Workout Incomplete / Gains lost")
                    out.append(ScreenBlock(kind: .notify, text: c.text, title: nil, speaker: nil, box: c.rect, lineHeight: c.lh, slot: "notify-bottom"))
                } else if c.y0 >= 0.20, c.y0 <= 0.45, c.x1 >= 0.93, c.lines.count <= 2, words >= 2 {
                    out.append(ScreenBlock(kind: .notify, text: c.text, title: nil, speaker: nil, box: c.rect, lineHeight: c.lh, slot: "notify-top"))
                }
            }
        }
        return out.sorted { $0.box.minY < $1.box.minY }
    }

    // MARK: Harita / menü

    private static func descriptionBlocks(_ items: [L]) -> [ScreenBlock] {
        let cands = group(items.filter { hasLower($0.text) }).map { Cand(lines: $0) }
        let caps = items.filter { isCapsOnly($0.text) && $0.text.filter(\.isLetter).count >= 3 && !matches(mapPromptRx, $0.text) && !matches(menuSelectRx, $0.text) && !matches(menuBackRx, $0.text) }
        var out: [ScreenBlock] = []
        for c in cands {
            let text = c.text
            let words = InfoBox.wordCount(text)
            // Cümle gibi: noktalama ile biter (liste öğeleri bitmez), ≥4 sözcük, en az bir küçük harfle başlayan sözcük.
            guard words >= 4, let last = text.last, ".!?".contains(last) || ".!?\"”’".contains(last) else { continue }
            guard c.lines.count >= 2 || words >= 6 else { continue }
            var title: String?
            var box = c.rect
            if let t = caps.filter({ $0.y1 <= c.y0 + 0.004 && c.y0 - $0.y1 < 1.5 * max($0.h, c.lh) && abs($0.x0 - c.x0) <= 0.05 && $0.words >= 1 })
                .max(by: { $0.y1 < $1.y1 }) {
                title = t.raw.trimmingCharacters(in: .whitespaces)
                box = box.union(CGRect(x: t.x0, y: t.y0, width: t.x1 - t.x0, height: t.h))
            }
            out.append(ScreenBlock(kind: .description, text: text, title: title, speaker: nil, box: box, lineHeight: c.lh, slot: "description"))
        }
        return out.sorted { $0.box.minY < $1.box.minY }
    }
}

/// Blok kararlılığı: slot başına izleyici. Aynı metin (≥0.85 benzer) 2 ardışık tam kare geçişinde görülünce çevrilir;
/// kesintisiz `clearAfter` sn kaybolunca temizlenir. Ekranda kalan aynı metin yeniden gönderilmez.
/// Mod değişince (ör. harita kapandı) o modda üretilmeyen türler hemen temizlenir. Zaman dışarıdan verilir.
public struct ScreenTracker {
    public enum Action: Equatable {
        case translate(ScreenBlock)
        case clear(slot: String, kind: ScreenKind)
    }
    public static let clearAfter: TimeInterval = 2.0
    public static let similarity = 0.85

    struct Track {
        var kind: ScreenKind
        var candidate = ""
        var stable = 0
        var shown: String?
        var visible = false
        var absentSince: TimeInterval?
    }
    private var tracks: [String: Track] = [:]
    private var mode: ScreenMode = .game
    public init() {}

    public mutating func reset() { tracks = [:]; mode = .game }

    private static func produces(_ mode: ScreenMode, _ kind: ScreenKind) -> Bool {
        mode == .game ? kind != .description : kind == .description
    }

    /// Bu geçişte görünen blokları ver; yapılacak eylemler döner.
    public mutating func feed(_ blocks: [ScreenBlock], mode newMode: ScreenMode, at now: TimeInterval) -> [Action] {
        var actions: [Action] = []
        if newMode != mode {
            // Mod değişti: yeni modda üretilmeyen türleri hemen kaldır.
            for (slot, t) in tracks where !Self.produces(newMode, t.kind) {
                if t.visible { actions.append(.clear(slot: slot, kind: t.kind)) }
                tracks[slot] = nil
            }
            mode = newMode
        }
        var seen = Set<String>()
        for b in blocks {
            // Aynı yuvada birden fazla blok varsa ilk (en üstteki) alınır.
            guard seen.insert(b.slot).inserted else { continue }
            var t = tracks[b.slot] ?? Track(kind: b.kind)
            t.absentSince = nil
            let norm = TextMatch.normalize((b.title.map { $0 + " " } ?? "") + b.text)
            if !t.candidate.isEmpty, TextMatch.isSimilar(t.candidate, norm, threshold: Self.similarity) { t.stable += 1 } else { t.candidate = norm; t.stable = 1 }
            if t.stable >= 2 {
                if t.visible, let s = t.shown, TextMatch.isSimilar(s, norm, threshold: Self.similarity) {
                    // aynı metin ekranda: yeniden gönderme
                } else {
                    t.shown = norm; t.visible = true
                    actions.append(.translate(b))
                }
            }
            tracks[b.slot] = t
        }
        for (slot, var t) in tracks where !seen.contains(slot) {
            t.candidate = ""; t.stable = 0
            if t.absentSince == nil { t.absentSince = now }
            if t.visible, now - t.absentSince! >= Self.clearAfter {
                actions.append(.clear(slot: slot, kind: t.kind))
                tracks[slot] = nil
            } else if !t.visible, now - t.absentSince! >= Self.clearAfter {
                tracks[slot] = nil
            } else { tracks[slot] = t }
        }
        return actions
    }

    /// Çeviri başarısızsa aynı blok yeniden denensin.
    public mutating func forgetShown(slot: String) {
        if var t = tracks[slot] { t.shown = nil; t.visible = false; tracks[slot] = t }
    }
}
