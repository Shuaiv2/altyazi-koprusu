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

/// PlayStation düğme simgelerinin OCR okumalarını cümle içinde kararlı belirteçlere çevirir.
/// Biçim: "L1" "R1" "L2" "R2" "L3" "R3" "OPTIONS" "TOUCHPAD" (çıplak büyük harf kodu) ve yüz düğmeleri için
/// "✕" "○" "□" "△" glif karakterleri. Köşeli parantez kullanılmaz: çevirmen "[L1]" içini çevirip bozabiliyor,
/// oysa L1/R1 gibi harf-rakam kodları ve geometrik simgeler çeviride olduğu gibi kalır.
/// Hangi düğme olduğu okunamayan simge ("oa", "@") dürüstçe "[button]" olur (çeviride "[düğme]").
public enum GlyphNormalizer {
    public static let faceGlyphs: Set<Character> = ["✕", "○", "□", "△"]

    private static func rx(_ p: String, _ o: NSRegularExpression.Options = []) -> NSRegularExpression { try! NSRegularExpression(pattern: p, options: o) }
    private static func sub(_ r: NSRegularExpression, _ s: String, _ t: String) -> String {
        r.stringByReplacingMatches(in: s, range: NSRange(s.startIndex..., in: s), withTemplate: t)
    }

    // Yanlış okunan L1/R1: "LI" "Ll" "L|" "RI" "Rl"
    private static let shoulderL = rx("(?<![A-Za-z0-9])L[1Il|](?![A-Za-z0-9])")
    private static let shoulderR = rx("(?<![A-Za-z0-9])R[1Il|](?![A-Za-z0-9])")
    private static let bracketCode = rx("[\\(\\[\\{]\\s*(L[123]|R[123]|OPTIONS|TOUCHPAD)\\s*[\\)\\]\\}]", .caseInsensitive)
    private static let bracketLetter = rx("[\\(\\[\\{]\\s*([XxOo0])\\s*[\\)\\]\\}]")
    private static let wordCodes = rx("(?<![A-Za-z0-9])(options|touchpad)(?=\\s+button)", .caseInsensitive)
    private static let plusSpacing = rx("((?:L|R)[123]|✕|○|□|△|\\[button\\])\\s*\\+\\s*(?=(?:L|R)[123]\\b|✕|○|□|△|\\[button\\])")

    private static let directMap: [Character: String] = [
        "ⓧ": "✕", "Ⓧ": "✕", "⊗": "✕", "✖": "✕", "✗": "✕", "✘": "✕", "❌": "✕", "⨯": "✕",
        "⭕": "○", "◯": "○", "◎": "○", "ⓞ": "○", "Ⓞ": "○", "⚪": "○",
        "◻": "□", "▢": "□", "☐": "□", "⬜": "□", "▫": "□",
        "▵": "△", "∆": "△", "⛛": "△", "🔺": "△", "▲": "△",
    ]
    private static let blobs: Set<String> = ["oa", "0a", "oo", "@", "©", "®", "ⓐ"]
    private static let leadWords: Set<String> = ["press", "hold", "tap"]
    private static let codeWords: Set<String> = ["L1", "R1", "L2", "R2", "L3", "R3", "OPTIONS", "TOUCHPAD", "✕", "○", "□", "△", "[button]"]

    /// Sözcük çevresindeki noktalama ayrılır: (önek, çekirdek, sonek)
    private static func split(_ t: String) -> (String, String, String) {
        let chars = Array(t)
        var a = 0, b = chars.count
        let edge: Set<Character> = [".", ",", "!", "?", ";", ":", "\"", "'", "”", "’", "“", "‘"]
        while a < b, edge.contains(chars[a]) { a += 1 }
        while b > a, edge.contains(chars[b - 1]) { b -= 1 }
        return (String(chars[0..<a]), String(chars[a..<b]), String(chars[b...]))
    }

    private static func isGlyphLike(_ core: String) -> Bool {
        codeWords.contains(core) || ["X", "x", "O", "o", "0"].contains(core) || blobs.contains(core.lowercased())
    }

    public static func normalize(_ input: String) -> String {
        var s = String(input.map { directMap[$0] ?? String($0) }.joined())
        s = sub(shoulderL, s, "L1"); s = sub(shoulderR, s, "R1")
        s = bracketCode.matches(in: s, range: NSRange(s.startIndex..., in: s)).reversed().reduce(s) { acc, m in
            guard let r = Range(m.range, in: acc), let g = Range(m.range(at: 1), in: acc) else { return acc }
            return acc.replacingCharacters(in: r, with: acc[g].uppercased())
        }
        // Parantezli kodların büyük harfe getirilmesi
        s = s.split(separator: " ", omittingEmptySubsequences: false).map { tok -> String in
            let (l, c, t) = split(String(tok))
            return ["l1", "r1", "l2", "r2", "l3", "r3"].contains(c.lowercased()) ? l + c.uppercased() + t : String(tok)
        }.joined(separator: " ")
        s = bracketLetter.stringByReplacingMatches(in: s, range: NSRange(s.startIndex..., in: s), withTemplate: "§$1§")
        s = s.replacingOccurrences(of: "§X§", with: "✕").replacingOccurrences(of: "§x§", with: "✕")
            .replacingOccurrences(of: "§O§", with: "○").replacingOccurrences(of: "§o§", with: "○").replacingOccurrences(of: "§0§", with: "○")
        s = wordCodes.matches(in: s, range: NSRange(s.startIndex..., in: s)).reversed().reduce(s) { acc, m in
            guard let r = Range(m.range, in: acc) else { return acc }
            return acc.replacingCharacters(in: r, with: acc[r].uppercased())
        }
        s = sub(plusSpacing, s, "$1 + ")

        // Belirsiz tek belirteçler (X, O, "oa", "@"): yalnızca "+" / Press-Hold-Tap yanında ya da "and/or" ile glif arasında.
        var toks = s.split(whereSeparator: \.isWhitespace).map(String.init)
        let parts = toks.map(split)
        func core(_ i: Int) -> String? { (0..<toks.count).contains(i) ? parts[i].1 : nil }
        func raw(_ i: Int) -> String? { (0..<toks.count).contains(i) ? toks[i] : nil }
        var out = toks
        for i in 0..<toks.count {
            let c = parts[i].1
            let lc = c.lowercased()
            let isLetterGlyph = ["x", "o", "0"].contains(lc)
            guard isLetterGlyph || blobs.contains(lc) else { continue }
            if isLetterGlyph && c != "X" && c != "x" && c != "O" && c != "o" && c != "0" { continue }
            let prev = core(i - 1)?.lowercased(), next = core(i + 1)?.lowercased()
            var ctx = false
            if raw(i - 1) == "+" || raw(i + 1) == "+" { ctx = true }
            if let p = prev, leadWords.contains(p) { ctx = true }
            if let p = prev, p == "and" || p == "or", let pp = core(i - 2), isGlyphLike(pp) { ctx = true }
            if let n = next, n == "and" || n == "or", let nn = core(i + 2), isGlyphLike(nn) { ctx = true }
            guard ctx else { continue }
            let g: String
            switch lc { case "x": g = "✕"; case "o", "0": g = "○"; default: g = "[button]" }
            out[i] = parts[i].0 + g + parts[i].2
        }
        toks = out
        return sub(plusSpacing, toks.joined(separator: " "), "$1 + ")
    }

    /// Metindeki düğme belirteçleri sırayla (çeviri sonrası eksik/fazla denetimi için).
    public static func tokens(in s: String) -> [String] {
        var out: [String] = []
        for t in s.split(whereSeparator: \.isWhitespace) {
            let c = split(String(t)).1
            if codeWords.contains(c) { out.append(c) }
        }
        return out
    }
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
            let isOurGlyph = last.count == 1 && GlyphNormalizer.faceGlyphs.contains(last.first!)
            if onlySymbols && !isPunct && !isOurGlyph { tokens.removeLast() }
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
            let text = stripTrailingGlyph(OcrNoise.clean(GlyphNormalizer.normalize(line.text)))
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

extension InfoBox {
    private static let timeRx = try! NSRegularExpression(pattern: "^\\s*\\d{1,2}\\s*[:.]\\s*\\d{2}\\s*([AaPp]\\.?[Mm]\\.?)?\\s*$")
    private static let percentRx = try! NSRegularExpression(pattern: "^\\s*\\d{1,3}\\s*%\\s*$")

    /// Telefon ekranındaki arayüz kırıntısı mı: saat ("12:45", "9.05 PM"), pil yüzdesi, harf içermeyen satır,
    /// tek sözcüklük kısa BÜYÜK HARF başlık ("MESSAGES"). Muhafazakâr: kısa gerçek mesajlar ("OK", "Hey") elenmez
    /// (tek sözcük yalnızca tamamı büyük harf ve ≥4 harfliyse elenir; "OK"/"LOL" gibi kısalar kalır).
    static func isPhoneChrome(_ t: String) -> Bool {
        let r = NSRange(t.startIndex..., in: t)
        if timeRx.firstMatch(in: t, range: r) != nil || percentRx.firstMatch(in: t, range: r) != nil { return true }
        guard t.contains(where: \.isLetter) else { return true }
        let words = t.split(whereSeparator: \.isWhitespace)
        if words.count == 1 {
            let letters = t.filter(\.isLetter)
            if letters.count >= 4, letters.count <= 14, !t.contains(where: \.isLowercase) { return true }
        }
        return false
    }

    /// Telefon mesajları: tüm satırlar üstten alta okunur, arayüz kırıntıları atılır. Satırlar arası dikey boşluk
    /// `splitGap` × satır yüksekliğinden büyükse ayrı balon sayılır ve ayrı blok döner. Mesajlar kısa olabilir:
    /// blok için en az 2 sözcük ve küçük harf içeren bir metin gerekir.
    public static func blocks(from lines: [OCRLine], region: Region, splitGap: Double = 1.2) -> [InfoBlock] {
        struct L { var text: String; var left, top, right, bottom: Double }
        let w = region.right - region.left, h = region.bottom - region.top
        var items: [L] = []
        for line in lines {
            let text = stripTrailingGlyph(OcrNoise.clean(GlyphNormalizer.normalize(line.text)))
            guard !text.isEmpty, !isPhoneChrome(text) else { continue }
            let b = line.box
            items.append(L(text: text,
                           left: region.left + Double(b.minX) * w, top: region.top + (1 - Double(b.maxY)) * h,
                           right: region.left + Double(b.maxX) * w, bottom: region.top + (1 - Double(b.minY)) * h))
        }
        items.sort { $0.top < $1.top }
        var groups: [[L]] = []
        for it in items {
            if let prev = groups.last?.last {
                let lh = max(prev.bottom - prev.top, 0.001)
                if it.top - prev.bottom <= splitGap * lh { groups[groups.count - 1].append(it); continue }
            }
            groups.append([it])
        }
        var out: [InfoBlock] = []
        for g in groups {
            let text = join(g.map(\.text))
            guard wordCount(text) >= 2, text.contains(where: \.isLowercase) else { continue }
            let heights = g.map { $0.bottom - $0.top }.sorted()
            let minX = g.map(\.left).min()!, minY = g.map(\.top).min()!
            let rect = CGRect(x: minX, y: minY, width: g.map(\.right).max()! - minX, height: g.map(\.bottom).max()! - minY)
            out.append(InfoBlock(text: text, box: rect, lineHeight: heights[heights.count / 2]))
        }
        return out
    }

    /// Birden çok balonu izleyiciye tek blok olarak vermek için (metinler satır sonuyla birleşir, kutu birleşimi).
    public static func combined(_ blocks: [InfoBlock]) -> InfoBlock? {
        guard let first = blocks.first else { return nil }
        let rect = blocks.dropFirst().reduce(first.box) { $0.union($1.box) }
        let hs = blocks.map(\.lineHeight).sorted()
        return InfoBlock(text: blocks.map(\.text).joined(separator: "\n"), box: rect, lineHeight: hs[hs.count / 2])
    }
}

/// Bilgi kutusu kararlılığı: metin 2 geçiş boyunca benzer kalınca çevrilir. Kutu ancak kesintisiz `clearAfter` sn
/// kaybolunca temizlenir (kısa OCR titremesi temizleme+yeniden gönderim yapmaz). Ekranda kalan aynı metin yeniden
/// gönderilmez; temizlenmiş kutu geri gelirse TV boş olduğundan yeniden gönderilir.
/// Zaman dışarıdan verilir (saniye, monoton) ki test edilebilsin.
public struct InfoBoxTracker {
    public enum Action: Equatable { case none, translate(InfoBlock), clear }
    public static let clearAfter: TimeInterval = 2.0
    private var candidate: String?
    private var stableCount = 0
    private var shown: String?          // son gösterilen metin (norm)
    private var visible = false         // TV'de şu an görünüyor mu
    private var absentSince: TimeInterval?
    public init() {}

    public mutating func reset() { candidate = nil; stableCount = 0; shown = nil; visible = false; absentSince = nil }

    public mutating func feed(_ block: InfoBlock?, at now: TimeInterval) -> Action {
        guard let block else {
            candidate = nil; stableCount = 0
            if absentSince == nil { absentSince = now }
            if visible, let since = absentSince, now - since >= Self.clearAfter { visible = false; return .clear }
            return .none
        }
        absentSince = nil
        let norm = TextMatch.normalize(block.text)
        if let c = candidate, TextMatch.isSimilar(c, norm, threshold: 0.9) { stableCount += 1 } else { candidate = norm; stableCount = 1 }
        guard stableCount >= 2 else { return .none }
        if visible, let s = shown, TextMatch.isSimilar(s, norm, threshold: 0.9) { return .none }
        shown = norm; visible = true
        return .translate(block)
    }

    /// Çeviri başarısız olduysa yeniden denensin.
    public mutating func forgetShown() { shown = nil; visible = false }
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
