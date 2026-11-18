// SPDX-License-Identifier: AGPL-3.0-only

import CoreGraphics
import Foundation

/// Oyundaki bilgi/öğretici kutusu (sol üst köşedeki çok satırlı açıklama yazıları) için saf mantık:
/// satırları bloğa gruplama, temizleme ve kararlılık izleme. Vision/arayüz bağımlılığı yok.
public struct InfoBlock: Equatable {
    public var text: String
    /// Kare genelinde normalize (0...1), başlangıç sol-üst.
    public var box: CGRect
    /// Tipik satır yüksekliği (normalize).
    public var lineHeight: Double
}

public enum InfoBox {
    public struct Region: Equatable {
        public var left: Double, top: Double, right: Double, bottom: Double
        public init(left: Double, top: Double, right: Double, bottom: Double) {
            self.left = left; self.top = top; self.right = right; self.bottom = bottom
        }
    }

    /// Satır sonuna yapışan tek başına simge/glif belirteci ("wardrobe &", "advance time =") atılır.
    public static func stripTrailingGlyph(_ line: String) -> String {
        var tokens = line.split(whereSeparator: \.isWhitespace).map(String.init)
        let keep = CharacterSet(charactersIn: ".!?…,;:'\"”’)")
        if tokens.count > 1, let last = tokens.last {
            let onlySymbols = last.unicodeScalars.allSatisfy { !CharacterSet.alphanumerics.contains($0) }
            let isPunct = last.unicodeScalars.allSatisfy { keep.contains($0) }
            if onlySymbols && !isPunct { tokens.removeLast() }
        }
        return tokens.joined(separator: " ")
    }

    static func wordCount(_ s: String) -> Int { s.split(whereSeparator: \.isWhitespace).filter { $0.contains(where: \.isLetter) }.count }

    static func isSentenceLike(_ s: String) -> Bool {
        wordCount(s) >= 3 && s.contains { $0.isLowercase }
    }

    /// Satırları birleştirir; satır sonu tireli sözcük ("main-" + "tenance") bitiştirilir.
    public static func join(_ lines: [String]) -> String {
        var out = ""
        for l in lines {
            let t = l.trimmingCharacters(in: .whitespaces)
            guard !t.isEmpty else { continue }
            if out.isEmpty { out = t; continue }
            if out.hasSuffix("-"), out.dropLast().last?.isLetter == true, t.first?.isLowercase == true {
                out.removeLast(); out += t
            } else { out += " " + t }
        }
        return out
    }

    /// Vision satırları (kırpma içinde normalize, başlangıç sol-alt) -> en iyi bilgi bloğu ya da nil.
    public static func bestBlock(from lines: [OCRLine], region: Region) -> InfoBlock? {
        struct L { var text: String; var left, top, right, bottom: Double }
        let w = region.right - region.left, h = region.bottom - region.top
        var items: [L] = []
        for line in lines {
            let text = stripTrailingGlyph(OcrNoise.clean(line.text))
            guard !text.isEmpty else { continue }
            let b = line.box
            items.append(L(text: text,
                           left: region.left + Double(b.minX) * w, top: region.top + (1 - Double(b.maxY)) * h,
                           right: region.left + Double(b.maxX) * w, bottom: region.top + (1 - Double(b.minY)) * h))
        }
        items.sort { $0.top < $1.top }

        var groups: [[L]] = []
        for it in items {
            if var cur = groups.last, let prev = cur.last {
                let lh = prev.bottom - prev.top
                if abs(it.left - prev.left) <= 0.02, it.top - prev.bottom < 1.6 * lh {
                    cur.append(it); groups[groups.count - 1] = cur; continue
                }
            }
            groups.append([it])
        }

        var best: (InfoBlock, Int)?
        for g in groups {
            let text = join(g.map(\.text))
            let words = wordCount(text)
            guard words >= 4, g.contains(where: { isSentenceLike($0.text) }) else { continue }
            let heights = g.map { $0.bottom - $0.top }.sorted()
            let rect = CGRect(x: g.map(\.left).min()!, y: g.map(\.top).min()!,
                              width: g.map(\.right).max()! - g.map(\.left).min()!,
                              height: g.map(\.bottom).max()! - g.map(\.top).min()!)
            if best == nil || words > best!.1 {
                best = (InfoBlock(text: text, box: rect, lineHeight: heights[heights.count / 2]), words)
            }
        }
        return best?.0
    }
}

/// Bilgi kutusu kararlılığı: metin 2 geçiş boyunca benzer kalınca çevrilir; kutu 3 boş geçiş kaybolunca temizlenir.
public struct InfoBoxTracker {
    public enum Action: Equatable { case none, translate(InfoBlock), clear }
    private var candidate: String?
    private var stableCount = 0
    private var shown: String?
    private var empties = 0
    public init() {}

    public mutating func reset() { candidate = nil; stableCount = 0; shown = nil; empties = 0 }

    public mutating func feed(_ block: InfoBlock?) -> Action {
        guard let block else {
            candidate = nil; stableCount = 0
            empties += 1
            if empties >= 3, shown != nil { shown = nil; empties = 0; return .clear }
            return .none
        }
        empties = 0
        let norm = TextMatch.normalize(block.text)
        if let c = candidate, TextMatch.isSimilar(c, norm, threshold: 0.9) { stableCount += 1 } else { candidate = norm; stableCount = 1 }
        guard stableCount >= 2 else { return .none }
        if let s = shown, TextMatch.isSimilar(s, norm, threshold: 0.9) { return .none }
        shown = norm
        return .translate(block)
    }

    /// Çeviri başarısız olduysa yeniden denensin.
    public mutating func forgetShown() { shown = nil }
}

/// Aynı (ya da ≥0.9 benzer) Türkçe/İngilizce satırı kısa sürede yeniden göndermemek için.
public struct ResendGuard {
    public static let window: TimeInterval = 8
    private var recent: [(text: String, at: TimeInterval)] = []
    public init() {}

    public mutating func reset() { recent = [] }

    /// true: gönder (ve kaydet). false: son 8 sn içinde aynısı gitti.
    public mutating func shouldSend(_ texts: [String], at time: TimeInterval) -> Bool {
        recent.removeAll { time - $0.at > Self.window }
        let norms = texts.map(TextMatch.normalize).filter { !$0.isEmpty }
        for n in norms where recent.contains(where: { TextMatch.isSimilar($0.text, n, threshold: 0.9) }) { return false }
        for n in norms { recent.append((n, time)) }
        return true
    }
}
