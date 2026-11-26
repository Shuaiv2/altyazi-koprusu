// SPDX-License-Identifier: AGPL-3.0-only

import CoreGraphics
import Foundation

/// Tam kare OCR'ından gelen tek satır. Kutu tüm karede normalize (0...1), başlangıç SOL-ÜST.
public struct ScreenLine: Equatable {
    public var text: String
    public var rect: CGRect
    public var confidence: Float
    /// Yalnızca telefon balonu: zemin MAVİ mi (oyuncunun balonu)? nil: bilinmiyor (konum kuralı kullanılır).
    public var blueBubble: Bool?
    public init(text: String, rect: CGRect, confidence: Float = 1, blueBubble: Bool? = nil) {
        self.text = text; self.rect = rect; self.confidence = confidence; self.blueBubble = blueBubble
    }
}

public enum ScreenMode: String, Equatable { case game, map, menu, phone }

public enum ScreenKind: String, Equatable { case objective, info, notify, description, phone, vehicle, prompt, menu }

/// Araç bilgi panelindeki tek hücre (etiket ya da sözcük değer). Sayı/para/puan değerleri hücre olmaz (dokunulmaz).
public struct VehicleCell: Equatable {
    public var box: CGRect
    public var source: String
    /// Sözlükten gelen BÜYÜK HARF Türkçe; nil ise normal çevirmenle çevrilip büyük harfe çevrilir.
    public var translated: String?
    public var isValue: Bool
    /// Yalnızca etiket: Türkçenin sağa uzayabileceği en uzak kenar (değerin sol kenarından 0.02 önce), normalize.
    public var maxRight: Double?
    /// Yalnızca liste (.menu): BÜYÜK HARF başlık hücresi mi (ör. STORY) — yoksa görev/öğe adı.
    public var isHeader: Bool
    /// Yalnızca liste (.menu): hücre zemin/yazı rengi ("#rrggbb"); NativeApp kareden örnekler, testte nil.
    public var bg: String?
    public var fg: String?
    public init(box: CGRect, source: String, translated: String?, isValue: Bool, maxRight: Double? = nil, isHeader: Bool = false) {
        self.box = box; self.source = source; self.translated = translated; self.isValue = isValue; self.maxRight = maxRight; self.isHeader = isHeader
    }
}

/// Sınıflandırılmış ekran metni bloğu.
public struct ScreenBlock: Equatable {
    public var kind: ScreenKind
    /// Çevrilecek gövde metni (satırlar boşlukla birleşmiş, düğme simgeleri normalleşmiş).
    public var text: String
    /// description için BÜYÜK HARF başlık (gövdeden ayrı çevrilir). Gövde "\n" ile ayrılmış birden çok parça içerebilir
    /// (başlık/gövde ayrımı); her parça ayrı çevrilir.
    public var title: String?
    /// notify için gönderen adı (çevrilmez).
    public var speaker: String?
    /// Kareye göre normalize, sol-üst. Başlık varsa kutuya dahildir.
    public var box: CGRect
    public var lineHeight: Double
    /// İzleyici anahtarı: tür + yaklaşık yer ("notify-bottom", "notify-top", ...).
    public var slot: String
    /// Yalnızca açık telefon konuşması: sıralı mesajlar (text = hepsi "\n" ile birleşik). speaker = konuşma başlığındaki ad.
    public var messages: [PhoneMessage] = []
    /// Yalnızca araç paneli (.vehicle) ve düğme istemleri (.prompt): satırlar halinde hücreler.
    public var rows: [[VehicleCell]] = []
    /// Araç paneli / istemler için izleyici anahtarı: her hücrenin kanonik (sözlük çevirisi ya da sadeleşmiş kaynak) metni.
    var vehicleSignature: String {
        let base = rows.flatMap { $0 }.map { $0.translated ?? TextMatch.normalize($0.source) }.joined(separator: "|")
        // Liste kayınca aynı satırlar başka yere gelir: ilk satırın yaklaşık yeri imzaya girer (kaba kutu, titreme tetiklemesin).
        if kind == .menu, let y = rows.first?.first?.box.minY {
            // Seçili satır (açık zeminli hücre) değişince de yeniden gönderilmeli; yoksa beyaz vurgu eski satırda kalır.
            let cells = rows.flatMap { $0 }
            let sel = cells.indices.filter { i in
                guard let h = cells[i].bg, h.count == 7, let v = Int(h.dropFirst(), radix: 16) else { return false }
                return Double((v >> 16) & 255) * 0.2126 + Double((v >> 8) & 255) * 0.7152 + Double(v & 255) * 0.0722 > 140
            }.map(String.init).joined(separator: ",")
            return base + "@" + String(Int((Double(y) / 0.03).rounded())) + "#" + sel
        }
        return base
    }
}

/// Telefon konuşmasındaki tek balon. `fromMe`: sağa yaslı (oyuncunun kendi yanıtı).
public struct PhoneMessage: Equatable {
    public var fromMe: Bool
    public var text: String
    public init(fromMe: Bool, text: String) { self.fromMe = fromMe; self.text = text }
}

/// Elde tutulan telefon (mesaj listesi ya da açık konuşma). Kutu tüm karede normalize, sol-üst başlangıç.
public struct PhoneInfo: Equatable {
    public var box: CGRect
    /// true: kişi/mesaj listesi (hiçbir şey çevrilmez); false: açık konuşma (balon başına .phone bloğu).
    public var isList: Bool
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
            let text = InfoBox.stripLeadingIcon(InfoBox.stripTrailingGlyph(OcrNoise.clean(GlyphNormalizer.normalize(raw))))
            guard !text.isEmpty, text.contains(where: \.isLetter) else { continue }
            if matches(moneyRx, text) || matches(clockRx, text) || matches(distanceRx, text) || matches(gpsRx, text) { continue }
            if isCall(text) || matches(arrowRx, raw) { continue }
            out.append(L(text: text, raw: InfoBox.stripLeadingIcon(GlyphNormalizer.normalize(raw)), x0: Double(r.minX), y0: Double(r.minY), x1: Double(r.maxX), y1: Double(r.maxY)))
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
        var text: String { GlyphNormalizer.stripIconRemnants(InfoBox.join(lines.map(\.text))) }
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

    /// `bubbleBlue`: telefon konuşmasında bir satır kutusunun balon zemini mavi mi (oyuncunun balonu)? nil döner ya da verilmezse
    /// konum kuralı (isMine) kullanılır. Yalnızca açık konuşma bulunduğunda çağrılır.
    public static func analyze(_ lines: [ScreenLine], bubbleBlue: ((CGRect) -> Bool?)? = nil) -> (mode: ScreenMode, blocks: [ScreenBlock], phone: PhoneInfo?) {
        let base = detectMode(lines)
        var lines = lines
        var vehicle: ScreenBlock?
        if base == .game, let v = detectVehicle(lines) {
            // Araç paneli satırlarını (başlık, noktalar, R3, logo gürültüsü dahil) diğer sınıflayıcılardan al.
            vehicle = v.block
            lines = lines.enumerated().filter { !v.claimed.contains($0.offset) }.map(\.element)
        }
        // Düğme istemleri (sağ alt): tüm modlarda; kullanılan satırlar başka bloğa girmez.
        let promptDet = detectPrompts(lines)
        if base != .map, let (info, chain) = detectPhone(lines) {
            var blocks = info.isList ? [] : phoneBlocks(chain, box: info.box, bubbleBlue: bubbleBlue)
            if let p = promptDet?.block { blocks.append(p); blocks.sort { $0.box.minY < $1.box.minY } }
            return (.phone, blocks, info)
        }
        if let p = promptDet { lines = lines.enumerated().filter { !p.claimed.contains($0.offset) }.map(\.element) }
        let items = prepare(lines)
        switch base {
        case .game:
            var blocks = gameBlocks(items)
            if let v = vehicle { blocks.append(v) }
            if let p = promptDet?.block { blocks.append(p) }
            blocks.sort { $0.box.minY < $1.box.minY }
            return (.game, blocks, nil)
        default:
            var blocks = descriptionBlocks(items)
            // Sol sütundaki liste (görev/öğe adları): açıklama kartının kullandığı satırlar listeye girmez.
            if let m = detectMenu(items, avoid: blocks.map(\.box)) { blocks.append(m) }
            if let p = promptDet?.block { blocks.append(p) }
            blocks.sort { $0.box.minY < $1.box.minY }
            return (base, blocks, nil)
        }
    }

    // MARK: Düğme istemleri (sağ alt)

    /// İngilizce istem -> BÜYÜK HARF Türkçe. Anahtarlar TextMatch.normalize biçimindedir.
    static let promptGlossary: [String: String] = [
        "add marker": "İŞARET EKLE", "set destination": "HEDEF BELİRLE", "remove destination": "HEDEFİ KALDIR", "recenter": "ORTALA",
        "close menu": "MENÜYÜ KAPAT", "2d view": "2B GÖRÜNÜM", "3d view": "3B GÖRÜNÜM", "back": "GERİ", "exit": "ÇIK",
        "select": "SEÇ", "scroll": "KAYDIR", "send": "GÖNDER", "rerack": "YERİNE KOY", "put down": "BIRAK", "put away": "KALDIR",
        "lower": "İNDİR", "lift": "KALDIR", "throw": "FIRLAT", "move hands": "ELLERİ OYNAT", "leave and dress": "GİYİN VE ÇIK",
        "leave": "AYRIL", "zoom": "YAKINLAŞTIR", "scrub": "OVALA", "adjust": "AYARLA", "greet": "SELAMLA",
        "open buckme": "BUCKME'Yİ AÇ", "get up": "KALK", "buy": "SATIN AL", "close": "KAPAT", "take": "AL", "pick up": "AL",
        "drop": "BIRAK", "enter": "GİR", "talk": "KONUŞ", "interact": "ETKİLEŞİM", "skip": "ATLA", "confirm": "ONAYLA",
        "cancel": "İPTAL", "continue": "DEVAM", "retry": "TEKRAR DENE", "sit": "OTUR", "stand up": "AYAĞA KALK", "hold": "BASILI TUT",
        "aim": "NİŞAN AL", "crouch": "ÇÖMEL", "jump": "ZIPLA", "sprint": "KOŞ", "hide": "SAKLAN", "search": "ARA", "call": "ARA",
        "answer": "YANITLA", "hang up": "KAPAT", "reply": "YANITLA", "change": "DEĞİŞTİR", "equip": "KUŞAN", "remove": "ÇIKAR",
        "try on": "DENE", "pay": "ÖDE", "deposit": "YATIR", "withdraw": "ÇEK", "previous": "ÖNCEKİ", "next": "SONRAKİ",
        "stow longarms": "UZUN SİLAHLARI KALDIR", "manage": "YÖNET",
        // Kayıtlı GTA 6 karelerinde görülen ek istem/etiketler.
        "reps": "TEKRAR", "drink": "İÇ", "eat": "YE", "sleep": "UYU", "open": "AÇ", "remove marker": "İŞARETİ KALDIR",
        "options": "SEÇENEKLER", "defuse": "ETKİSİZ HALE GETİR", "provoke": "KIŞKIRT", "taunt": "ALAY ET",
        "view comments": "YORUMLARI GÖR", "damage": "HASAR", "range": "MENZİL", "accuracy": "İSABET", "handling": "KULLANIM",
        "capacity": "KAPASİTE", "very low": "ÇOK DÜŞÜK", "low": "DÜŞÜK", "medium": "ORTA", "high": "YÜKSEK", "very high": "ÇOK YÜKSEK",
        "phone": "TELEFON", "reminders": "HATIRLATICILAR", "photo studio": "FOTOĞRAF STÜDYOSU", "marked bills": "İŞARETLİ PARA",
        "jewelry": "MÜCEVHER", "narcotics": "UYUŞTURUCU", "stash": "ZULA", "stash bag": "ZULA ÇANTASI", "teller": "VEZNEDAR",
        "local": "YEREL", "vehicle": "ARAÇ", "mods": "MODİFİYE", "you okay": "İYİ MİSİN?", "deadlifts": "DEADLIFT",
        "tactical rifle": "TAKTİK TÜFEK", "wear": "GİY", "look": "BAK", "travel": "YOLCULUK YAP", "take photo": "FOTOĞRAF ÇEK", "examine": "İNCELE", "flip": "ÇEVİR", "undo": "GERİ AL", "show text": "METNİ GÖSTER", "previous tab": "ÖNCEKİ SEKME", "next tab": "SONRAKİ SEKME", "pilot": "PİLOT", "alarm": "ALARM", "tab": "SEKME",
    ]

    /// Marka/karakter adları ve tabela parçaları: istem değildir, çevrilmez.
    static let promptSkip: Set<String> = ["fleeca", "apoc", "ubermacht", "übermacht", "vapid", "lucia", "jason", "moses", "ckr",
                                          "b a n k", "glory", "kingfishers", "hawk little mediator", "willard", "buckme"]

    private static let counterRx = rx("\\s*(\\(\\s*\\d+\\s*/\\s*\\d+\\s*\\))\\s*$")
    private static let glyphTokenRx = rx("^(?:[OXx0QDAΔ©®◯○●×✕△□▽⊗⊙]|\\(|\\)|\\(\\w{1,2}\\)?|\\w\\)|[LR][123]|[^\\p{L}\\p{N}]+)$")

    /// "TAKE AMMO PACK (0/3)" -> ("TAKE AMMO PACK", "(0/3)"); sayaç yoksa ikinci değer nil.
    public static func splitCounter(_ text: String) -> (base: String, counter: String?) {
        let t = text.trimmingCharacters(in: .whitespaces)
        guard let m = counterRx.firstMatch(in: t, range: NSRange(t.startIndex..., in: t)), let r = Range(m.range, in: t), let c = Range(m.range(at: 1), in: t) else { return (t, nil) }
        return (String(t[..<r.lowerBound]).trimmingCharacters(in: .whitespaces), String(t[c]).replacingOccurrences(of: " ", with: ""))
    }

    /// OCR'ın istem satırına eklediği denetleyici simgesi belirteçlerini (O, ©, ®, X, (, L3, R3 ...) baştan/sondan atar.
    public static func stripPromptGlyphs(_ text: String) -> String {
        var toks = text.split(whereSeparator: \.isWhitespace).map(String.init)
        // Sayaç ("(0/3)") sondaki simge sayılmaz.
        func isGlyph(_ t: String) -> Bool { !matches(counterRx, t) && matches(glyphTokenRx, t) }
        while toks.count > 1, let l = toks.last, isGlyph(l) { toks.removeLast() }
        while toks.count > 1, let f = toks.first, isGlyph(f) { toks.removeFirst() }
        // Son sözcüğe yapışık simge ("BACK©")
        if var l = toks.last, l.count > 3, let c = l.last, "©®◯○●×".contains(c) { l.removeLast(); toks[toks.count - 1] = l }
        return toks.joined(separator: " ")
    }

    /// İstem sözlük çevirisi (sayaç korunur); yoksa nil.
    public static func promptTranslation(_ text: String) -> String? {
        let (base, counter) = splitCounter(text)
        let n = TextMatch.normalize(base)
        guard !n.isEmpty else { return nil }
        var found = promptGlossary[n]
        if found == nil {
            var best: (String, Int)?
            for (k, v) in promptGlossary where TextMatch.isSimilar(n, k, threshold: 0.85) {
                let d = abs(k.count - n.count)
                if best == nil || d < best!.1 { best = (v, d) }
            }
            found = best?.0
        }
        guard let f = found else { return nil }
        return counter.map { f + " " + $0 } ?? f
    }

    /// Sağ alttaki (x≥0.55, y≥0.55) BÜYÜK HARF 1-5 sözcüklü satırlar; her satır bir hücre. Açıklama kartı başlıkları
    /// (hemen altında küçük harfli gövde satırı olan BÜYÜK HARF satırlar) istem değildir.
    static func detectPrompts(_ lines: [ScreenLine]) -> (block: ScreenBlock, claimed: Set<Int>)? {
        var claimed = Set<Int>()
        var rows: [[VehicleCell]] = []
        for (i, l) in lines.enumerated() where l.confidence >= 0.5 && l.rect.minX >= 0.55 && l.rect.minY >= 0.55 && l.rect.maxX <= 0.997 && l.rect.maxY <= 1.0 {
            let raw = l.text.trimmingCharacters(in: .whitespacesAndNewlines)
            let stripped = stripPromptGlyphs(raw)
            let (base, _) = splitCounter(stripped)
            guard isCapsOnly(base), letterCount(base) >= 3, (1...5).contains(base.split(whereSeparator: \.isWhitespace).count) else { continue }
            if promptSkip.contains(TextMatch.normalize(base)) { continue }
            if matches(moneyRx, base) || matches(clockRx, base) || matches(distanceRx, base) || matches(moneyRx, stripped) { continue }
            // Açıklama başlığı: hemen altında (≤1.5 satır) sola hizalı küçük harfli gövde satırı var.
            let isTitle = lines.enumerated().contains { (j, o) in
                guard j != i, o.confidence >= 0.5, hasLower(o.text) else { return false }
                let gap = Double(o.rect.minY - l.rect.maxY)
                return gap >= -0.004 && gap < 1.5 * Double(max(l.rect.height, o.rect.height)) && abs(Double(o.rect.minX - l.rect.minX)) <= 0.05
            }
            if isTitle { continue }
            claimed.insert(i)
            rows.append([VehicleCell(box: l.rect, source: stripped, translated: promptTranslation(stripped), isValue: false)])
        }
        guard !rows.isEmpty else { return nil }
        rows.sort { $0[0].box.minY < $1[0].box.minY }
        let cells = rows.flatMap { $0 }
        let box = cells.dropFirst().reduce(cells[0].box) { $0.union($1.box) }
        let hs = cells.map { Double($0.box.height) }.sorted()
        return (ScreenBlock(kind: .prompt, text: cells.map(\.source).joined(separator: "\n"), title: nil, speaker: nil, box: box,
                            lineHeight: hs[hs.count / 2], slot: "prompt", rows: rows), claimed)
    }

    // MARK: Para kesri ("$1,400 / $5,000")

    private static let moneyFracRx = rx("([\\$€£]\\s?\\d[\\d,\\.]*)\\s*(?:\\s[1|Il!/]\\s|[|/])\\s*([\\$€£]\\s?\\d[\\d,\\.]*)")

    /// OCR'ın "<para> 1 <para>" / "<para> | <para>" okumasını "<para> / <para>" yapar.
    public static func normalizeMoneyFraction(_ text: String) -> String {
        moneyFracRx.stringByReplacingMatches(in: text, range: NSRange(text.startIndex..., in: text), withTemplate: "$1 / $2")
    }

    /// Harf içermeyen (para/sayı/eğik çizgi) parça çevirmene gitmez, olduğu gibi geçer.
    public static func isPassThroughSegment(_ seg: String) -> Bool { !seg.contains(where: \.isLetter) }

    /// "GOAL $1,400 / $5,000" -> ("GOAL", "$1,400 / $5,000"): yalnızca sözcük kısmı çevrilir, sayılar aynen kalır.
    public static func splitWordsAndAmount(_ seg: String) -> (words: String, amount: String)? {
        let r = rx("^([\\p{L}][\\p{L}' \\-]*?)\\s+([\\$€£]\\s?\\d.*|\\d[\\d,\\.]*(?:\\s*/.*)?)$")
        let t = seg.trimmingCharacters(in: .whitespaces)
        guard let m = r.firstMatch(in: t, range: NSRange(t.startIndex..., in: t)), let a = Range(m.range(at: 1), in: t), let b = Range(m.range(at: 2), in: t) else { return nil }
        let amount = String(t[b])
        // Tutar kısmında harf olmamalı
        guard !amount.contains(where: \.isLetter) else { return nil }
        return (String(t[a]), amount)
    }

    // MARK: Araç bilgi paneli (GTA 6)

    /// İngilizce -> BÜYÜK HARF Türkçe. Anahtarlar TextMatch.normalize biçimindedir.
    static let vehicleGlossary: [String: String] = [
        "access": "ERİŞİM", "locked": "KİLİTLİ", "unlocked": "AÇIK", "security": "GÜVENLİK", "alarmed": "ALARMLI",
        "tracker": "TAKİP CİHAZI", "none": "YOK", "sell value": "SATIŞ DEĞERİ", "cost to register": "KAYIT ÜCRETİ",
        "speed": "HIZ", "acceleration": "HIZLANMA", "asphalt handling": "ASFALT YOL TUTUŞU", "off road handling": "ARAZİ YOL TUTUŞU",
        "strength": "DAYANIKLILIK", "rating": "PUAN", "key cloner": "ANAHTAR KOPYALAYICI", "standard": "STANDART",
        "basic": "TEMEL", "advanced": "GELİŞMİŞ", "owned": "SAHİPLİ", "stolen": "ÇALINTI", "registered": "KAYITLI",
        "unregistered": "KAYITSIZ",
    ]

    /// Sözlük çevirisi (OCR gürültüsüne karşı ≥0.85 bulanık eşleşme); yoksa nil.
    public static func vehicleTranslation(_ text: String) -> String? {
        let n = TextMatch.normalize(text)
        guard !n.isEmpty else { return nil }
        if let t = vehicleGlossary[n] { return t }
        var best: (String, Int)?
        for (k, v) in vehicleGlossary where TextMatch.isSimilar(n, k, threshold: 0.85) {
            let d = abs(k.count - n.count)
            if best == nil || d < best!.1 { best = (v, d) }
        }
        return best?.0
    }

    private static func letterCount(_ s: String) -> Int { s.filter(\.isLetter).count }

    /// Sağ yarıda sol hizalı (x0 ±0.006) ≥4 BÜYÜK HARF etiket sütunu, en az 2'si sözlükte; aynı satırlarda sağda değerler.
    /// İlk bilinen etiketin üstündeki sütun satırları marka/model başlığıdır (çevrilmez ama panelin parçası sayılır).
    static func detectVehicle(_ lines: [ScreenLine]) -> (block: ScreenBlock, claimed: Set<Int>)? {
        struct C { var i: Int; var r: CGRect; var text: String }
        var cands: [C] = []
        for (i, l) in lines.enumerated() where l.confidence >= 0.5 && l.rect.minX >= 0.5 && l.rect.maxX <= 0.997 {
            let t = l.text.trimmingCharacters(in: .whitespacesAndNewlines)
            if isCapsOnly(t), letterCount(t) >= 3 { cands.append(C(i: i, r: l.rect, text: t)) }
        }
        guard cands.count >= 4 else { return nil }
        var best: [C] = []
        for a in cands {
            let m = cands.filter { abs(Double($0.r.minX - a.r.minX)) <= 0.006 }
            let known = m.filter { vehicleTranslation($0.text) != nil }.count
            let bestKnown = best.filter { vehicleTranslation($0.text) != nil }.count
            if m.count > best.count || (m.count == best.count && known > bestKnown) { best = m }
        }
        guard best.count >= 4, best.filter({ vehicleTranslation($0.text) != nil }).count >= 2 else { return nil }
        best.sort { $0.r.minY < $1.r.minY }
        guard let firstKnown = best.firstIndex(where: { vehicleTranslation($0.text) != nil }) else { return nil }
        let header = Array(best[..<firstKnown])
        var rowsC: [C] = []
        for c in best[firstKnown...] {
            if let p = rowsC.last, Double(c.r.minY - p.r.maxY) > 0.08 { break }
            rowsC.append(c)
        }
        let colX = Double(best[0].r.minX)
        let memberIdx = Set(best.map(\.i))
        var claimed = Set(header.map(\.i))
        var rows: [[VehicleCell]] = []
        var rowBoxes: [CGRect] = []
        var withValues = 0
        for lab in rowsC {
            let values = lines.enumerated().filter { (i, l) in
                guard !memberIdx.contains(i), l.confidence >= 0.3, l.rect.minX >= 0.5, l.rect.minX >= lab.r.maxX - 0.002 else { return false }
                let ov = min(l.rect.maxY, lab.r.maxY) - max(l.rect.minY, lab.r.minY)
                return Double(ov) >= 0.5 * Double(min(l.rect.height, lab.r.height))
            }.sorted { $0.element.rect.minX < $1.element.rect.minX }
            let known = vehicleTranslation(lab.text)
            if known == nil && values.isEmpty { continue }   // bilinmeyen ve değersiz satır panel satırı değil
            claimed.insert(lab.i)
            var cells = [VehicleCell(box: lab.r, source: lab.text, translated: known, isValue: false,
                                     maxRight: values.first.map { Double($0.element.rect.minX) - 0.02 })]
            for (i, l) in values {
                claimed.insert(i)
                let t = l.text.trimmingCharacters(in: .whitespacesAndNewlines)
                // Sayı, para, puan: dokunulmaz. Küçük harfli gürültü (logo) da hücre olmaz.
                guard isCapsOnly(t), letterCount(t) >= 3, letterCount(t) > t.filter(\.isNumber).count else { continue }
                cells.append(VehicleCell(box: l.rect, source: t, translated: vehicleTranslation(t), isValue: true))
            }
            rows.append(cells)
            rowBoxes.append(lab.r)
            if !values.isEmpty { withValues += 1 }
        }
        guard rows.count >= 2, withValues >= 2, rows.flatMap({ $0 }).contains(where: { $0.translated != nil }) else { return nil }
        // Panel bölgesindeki artakalan satırlar (sayfa noktaları, R3, logo gürültüsü): panele aittir, başka blok olmaz.
        let top = Double(best[0].r.minY) - 0.12, bottom = Double(rowsC.last!.r.maxY) + 0.01
        for (i, l) in lines.enumerated() where Double(l.rect.minX) >= colX - 0.03 && Double(l.rect.minY) >= top && Double(l.rect.maxY) <= bottom { claimed.insert(i) }
        let cells = rows.flatMap { $0 }
        let box = cells.dropFirst().reduce(cells[0].box) { $0.union($1.box) }
        let hs = rowBoxes.map { Double($0.height) }.sorted()
        let block = ScreenBlock(kind: .vehicle, text: cells.map(\.source).joined(separator: "\n"), title: nil, speaker: nil, box: box,
                                lineHeight: hs[hs.count / 2], slot: "vehicle", rows: rows)
        return (block, claimed)
    }

    // MARK: Telefon

    private static let phoneHeaders: Set<String> = ["suggested", "messages", "message", "contacts", "recent", "recents", "favorites", "favourites", "calls"]
    private static let truncRx = rx("(\\.\\.\\.|…|\\s\\.)\\s*$")

    /// Dar dikey sütun: ≥5 üst üste kısa satır (eğik tutulan telefonda merkez kayar). Liste yapısı (başlık sözcüğü ya da ≥2 "..." ile
    /// biten önizleme) varsa liste; yoksa ancak sağ altta BACK istemi, ≥4 cümle benzeri satır ve kesik satırsız ise açık konuşma
    /// (konuşma gerçek karede doğrulanmadı).
    static func detectPhone(_ lines: [ScreenLine]) -> (PhoneInfo, [L])? {
        var cand: [L] = []
        for line in lines {
            guard line.confidence >= 0.3, line.rect.width <= 0.16, line.rect.minX >= 0.003, line.rect.maxX <= 0.997 else { continue }
            let raw = line.text.trimmingCharacters(in: .whitespacesAndNewlines)
            if matches(toastRx, raw) || matches(mapPromptRx, raw) || matches(menuSelectRx, raw) || matches(menuBackRx, raw) { continue }
            let t = InfoBox.stripLeadingIcon(GlyphNormalizer.normalize(raw))
            guard t.contains(where: \.isLetter) else { continue }
            if matches(moneyRx, t) || matches(clockRx, t) || matches(distanceRx, t) { continue }
            let r = line.rect
            cand.append(L(text: t, raw: t, x0: Double(r.minX), y0: Double(r.minY), x1: Double(r.maxX), y1: Double(r.maxY)))
        }
        var chains: [[L]] = []
        for it in cand.sorted(by: { $0.y0 < $1.y0 }) {
            var best: (Int, Double)?
            for (i, c) in chains.enumerated() {
                let p = c.last!
                let gap = it.y0 - p.y1
                guard gap >= -0.6 * max(p.h, it.h), gap <= max(0.05, 3.2 * max(p.h, it.h)), abs(it.cx - p.cx) <= 0.10 else { continue }
                if best == nil || gap < best!.1 { best = (i, gap) }
            }
            if let b = best { chains[b.0].append(it) } else { chains.append([it]) }
        }
        guard let chain = chains.max(by: { $0.count < $1.count }), chain.count >= 5,
              chain.last!.y1 - chain.first!.y0 >= 0.18 else { return nil }
        let texts = chain.map(\.text)
        let hasHeader = texts.contains { phoneHeaders.contains($0.lowercased().trimmingCharacters(in: CharacterSet.letters.inverted)) }
        let truncated = texts.filter { matches(truncRx, $0) }.count
        let rect = chain.dropFirst().reduce(CGRect(x: chain[0].x0, y: chain[0].y0, width: chain[0].x1 - chain[0].x0, height: chain[0].h)) {
            $0.union(CGRect(x: $1.x0, y: $1.y0, width: $1.x1 - $1.x0, height: $1.h))
        }
        let box = rect.insetBy(dx: -0.03, dy: -0.02).intersection(CGRect(x: 0, y: 0, width: 1, height: 1))
        if hasHeader || truncated >= 2 { return (PhoneInfo(box: box, isList: true), chain) }
        // Açık konuşma
        let backPrompt = lines.contains { $0.rect.minY > 0.84 && $0.rect.minX > 0.7 && matches(menuBackRx, $0.text) }
        let sentences = chain.filter { $0.words >= 3 && hasLower($0.text) }.count
        let words = chain.reduce(0) { $0 + $1.words }
        let cx = chain.map(\.cx).reduce(0, +) / Double(chain.count)
        guard backPrompt, truncated == 0, chain.count >= 4, sentences >= 2, words >= 8, cx >= 0.3 else { return nil }
        return (PhoneInfo(box: box, isList: false), chain)
    }

    /// Açık konuşma: dikey boşluğu ≥0.8 satır yüksekliği olan ya da sol kenarı ≥0.03 kayan satırlar ayrı balondur.
    private static let phoneSystemLabels: Set<String> = ["new message", "new messages", "today", "yesterday", "scroll", "send", "reply"]

    private static func phoneBlocks(_ chainIn: [L], box: CGRect, bubbleBlue: ((CGRect) -> Bool?)?) -> [ScreenBlock] {
        var chain = chainIn.sorted { $0.y0 < $1.y0 }
        chain.removeAll { phoneSystemLabels.contains($0.text.lowercased().trimmingCharacters(in: CharacterSet.letters.inverted.subtracting(.whitespaces))) }
        // Üstteki kısa özel ad satırı konuşmanın başlığıdır (karşı taraf): gönderen olarak taşınır, gövdeye girmez.
        var speaker: String?
        if chain.count >= 2, isCapitalizedName(chain[0].text), chain[1].y0 - chain[0].y1 > 0.02 {
            speaker = chain[0].text; chain.removeFirst()
        }
        // Renk ipucu (satır başına): mavi = oyuncu. Yan yana iki satırın rengi farklıysa ya da (renk biliniyorsa) aralarında boşluk
        // varsa ayrı balondur (gerçek karede balon içi satır kutuları üst üste biner, ayrı balonlar arasında küçük boşluk kalır).
        let hint: [Bool?] = chain.map { bubbleBlue?(CGRect(x: $0.x0, y: $0.y0, width: $0.x1 - $0.x0, height: $0.h)) }
        var groups: [[L]] = []
        var groupHints: [[Bool?]] = []
        for (i, it) in chain.enumerated() {
            if let p = groups.last?.last, let ph = groupHints.last?.last {
                let sameColor = (ph == nil || hint[i] == nil) || ph == hint[i]
                let hasColor = ph != nil && hint[i] != nil
                let gap = it.y0 - p.y1
                let close = gap < 0.8 * max(p.h, 0.001) && (!hasColor || gap < 0.2 * max(p.h, 0.001))
                if sameColor, close, abs(it.x0 - p.x0) < 0.03 { groups[groups.count - 1].append(it); groupHints[groupHints.count - 1].append(hint[i]); continue }
            }
            groups.append([it]); groupHints.append([hint[i]])
        }
        var msgs: [PhoneMessage] = []
        var rect: CGRect?
        var lhs: [Double] = []
        for (gi, g) in groups.enumerated() {
            let c = Cand(lines: g)
            guard InfoBox.wordCount(c.text) >= 2, hasLower(c.text) else { continue }
            let known = groupHints[gi].compactMap { $0 }
            let mine = known.isEmpty ? isMine(c.rect, in: box) : known.filter { $0 }.count * 2 > known.count
            msgs.append(PhoneMessage(fromMe: mine, text: c.text))
            rect = rect.map { $0.union(c.rect) } ?? c.rect
            lhs.append(c.lh)
        }
        guard let r = rect, !msgs.isEmpty else { return [] }
        let lh = lhs.sorted()[lhs.count / 2]
        return [ScreenBlock(kind: .phone, text: msgs.map(\.text).joined(separator: "\n"), title: nil, speaker: speaker, box: r,
                            lineHeight: lh, slot: "phone", messages: msgs)]
    }

    /// Balon telefon kutusunun merkezinin sağındaysa ve sağ kenara sola olduğundan yakınsa oyuncunundur (sağa yaslı).
    static func isMine(_ bubble: CGRect, in box: CGRect) -> Bool {
        bubble.midX > box.midX && (box.maxX - bubble.maxX) < (bubble.minX - box.minX)
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
            // Salt ad/etiket ("Key Lento Coffee Morning"): çevrilmez.
            let nameLike = InfoBox.isNameLabel(c.text)
            if c.lines.count == 1, words >= 2, abs(cxMid - 0.5) <= 0.12, c.y0 > 0.93 {
                if nameLike { continue }
                out.append(ScreenBlock(kind: .objective, text: c.text, title: nil, speaker: nil, box: c.rect, lineHeight: c.lh, slot: "objective"))
            } else if c.x0 < 0.08, c.y0 >= 0.18, c.y0 <= 0.55, words >= 3, InfoBox.isSentenceLike(c.text) {
                if nameLike { continue }
                // Tüm sözcükleri büyük harfle başlayan satır cümle değil, ad/ürün adıdır ("Bandeau, Shrug & Cargos").
                if !c.text.split(whereSeparator: { $0.isWhitespace }).contains(where: { $0.first?.isLowercase == true }) { continue }
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

    // MARK: Harita / menü listesi (sol sütun, yerinde çeviri)

    /// Sol sütun BÜYÜK HARF başlıkları: İngilizce -> BÜYÜK HARF Türkçe. Anahtarlar TextMatch.normalize biçimindedir.
    static let menuGlossary: [String: String] = [
        "story": "HİKÂYE", "optional": "İSTEĞE BAĞLI", "lucia only": "YALNIZCA LUCIA", "jason only": "YALNIZCA JASON",
        "personal": "KİŞİSEL", "vehicles": "ARAÇLAR", "weapons": "SİLAHLAR", "items": "EŞYALAR", "services": "HİZMETLER",
        "transport": "ULAŞIM",
    ]

    /// Başlık sözlük çevirisi (OCR gürültüsüne karşı ≥0.85 bulanık eşleşme); yoksa nil.
    public static func menuTranslation(_ text: String) -> String? {
        let n = TextMatch.normalize(text)
        guard !n.isEmpty else { return nil }
        if let t = menuGlossary[n] { return t }
        var best: (String, Int)?
        for (k, v) in menuGlossary where TextMatch.isSimilar(n, k, threshold: 0.85) {
            let d = abs(k.count - n.count)
            if best == nil || d < best!.1 { best = (v, d) }
        }
        return best?.0
    }

    /// Simge artığı sayılmayacak gerçek kısa sözcükler ("Go Home", "To Do").
    private static let shortRealWords: Set<String> = ["go", "to", "be", "do", "on", "in", "at", "no", "up", "as", "an", "if", "it", "is", "me", "my", "we", "so", "or", "of", "by", "i"]

    /// Satır başındaki simge artıklarını (madde işareti, "-", "a"/"Er" gibi 1-2 harfli belirteç) atar; kutunun sol kenarı
    /// atılan karakter oranında sağa kayar (simge yazının solundadır).
    static func menuClean(_ text: String, x0: Double, x1: Double) -> (text: String, x0: Double) {
        let orig = text.trimmingCharacters(in: .whitespaces)
        var t = String(orig.drop(while: { !$0.isLetter && !$0.isNumber && $0 != "$" }))
        let toks = t.split(separator: " ", maxSplits: 1, omittingEmptySubsequences: true).map(String.init)
        if toks.count == 2, toks[1].first?.isUppercase == true,
           (toks[0].count <= 2 && (toks[0].first?.isLowercase == true || !shortRealWords.contains(toks[0].lowercased())))
            || (toks[0].count <= 3 && toks[0].contains { !$0.isLetter }) {
            t = toks[1]
        }
        t = t.trimmingCharacters(in: .whitespaces)
        guard !orig.isEmpty, t.count < orig.count else { return (t, x0) }
        return (t, x0 + (x1 - x0) * Double(orig.count - t.count) / Double(orig.count))
    }

    /// Haritanın/menünün sol sütunundaki liste: BÜYÜK HARF başlıklar (STORY, OPTIONAL) ve görev/öğe adları. Her satır bir hücre.
    /// Ad satırlarından ≥3'ü sol kenarda (±0.03) hizalıysa liste sayılır; değilse nil. `avoid`: açıklama kartı kutuları.
    static func detectMenu(_ items: [L], avoid: [CGRect]) -> ScreenBlock? {
        struct C { var text: String; var x0: Double; var y0: Double; var x1: Double; var y1: Double; var header: Bool; var h: Double { y1 - y0 } }
        var titles: [C] = [], headers: [C] = []
        for it in items where it.x0 < 0.40 {
            let r = CGRect(x: it.x0, y: it.y0, width: it.x1 - it.x0, height: it.h)
            if avoid.contains(where: { $0.insetBy(dx: -0.005, dy: -0.005).intersects(r) }) { continue }
            let (t, x0) = menuClean(it.text, x0: it.x0, x1: it.x1)
            guard letterCount(t) >= 3, let first = t.first, !matches(mapPromptRx, t), !matches(menuSelectRx, t), !matches(menuBackRx, t) else { continue }
            let words = t.split(whereSeparator: \.isWhitespace).count
            if isCapsOnly(t) {
                if words <= 4 { headers.append(C(text: t, x0: x0, y0: it.y0, x1: it.x1, y1: it.y1, header: true)) }
            } else if words <= 8, first.isUppercase, let last = t.last, !".!".contains(last) {
                titles.append(C(text: t, x0: x0, y0: it.y0, x1: it.x1, y1: it.y1, header: false))
            }
        }
        // Ad satırlarının en kalabalık sol hizası.
        var ref: (x: Double, n: Int)?
        for a in titles {
            let n = titles.filter { abs($0.x0 - a.x0) <= 0.03 }.count
            if ref == nil || n > ref!.n || (n == ref!.n && a.x0 < ref!.x) { ref = (a.x0, n) }
        }
        guard let r = ref, r.n >= 3, r.x <= 0.20 else { return nil }
        let col = titles.filter { abs($0.x0 - r.x) <= 0.03 }
        let top = col.map(\.y0).min()! - 0.12, bottom = col.map(\.y1).max()! + 0.02
        let heads = headers.filter { $0.x0 <= r.x + 0.03 && $0.y0 >= top && $0.y1 <= bottom }
        var cells = (col + heads).sorted { $0.y0 < $1.y0 }
        // Üst üste binen satır (çift okuma) tek hücre olur.
        var kept: [C] = []
        for c in cells {
            if let p = kept.last, min(p.y1, c.y1) - max(p.y0, c.y0) > 0.5 * min(p.h, c.h) { continue }
            kept.append(c)
        }
        cells = kept
        let maxRight = min(0.42, (cells.map(\.x1).max() ?? 0) + 0.02)
        let rows = cells.map { c -> [VehicleCell] in
            let box = CGRect(x: c.x0, y: c.y0, width: c.x1 - c.x0, height: c.h)
            return [VehicleCell(box: box, source: c.text, translated: c.header ? menuTranslation(c.text) : nil, isValue: false,
                                maxRight: maxRight, isHeader: c.header)]
        }
        let flat = rows.flatMap { $0 }
        let box = flat.dropFirst().reduce(flat[0].box) { $0.union($1.box) }
        let hs = flat.map { Double($0.box.height) }.sorted()
        return ScreenBlock(kind: .menu, text: flat.map(\.source).joined(separator: "\n"), title: nil, speaker: nil, box: box,
                           lineHeight: hs[hs.count / 2], slot: "menu", rows: rows)
    }

    // MARK: Harita / menü

    private static func descriptionBlocks(_ items: [L]) -> [ScreenBlock] {
        let cands = group(items.filter { hasLower($0.text) }).map { Cand(lines: $0) }
        let caps = items.filter { isCapsOnly($0.text) && $0.text.filter(\.isLetter).count >= 3 && !matches(mapPromptRx, $0.text) && !matches(menuSelectRx, $0.text) && !matches(menuBackRx, $0.text) }
        var out: [ScreenBlock] = []
        for c in cands {
            // İlk satır başlık gibiyse (≤6 sözcük, noktalamasız, ardından BÜYÜK harfle başlayan satır) gövdeden ayrı parça olur.
            var segments: [[L]] = [c.lines]
            if c.lines.count >= 2, let first = c.lines.first, first.words <= 6, let l = first.text.last, !".!?\"”’:".contains(l),
               c.lines[1].text.first?.isUppercase == true {
                segments = [[first], Array(c.lines.dropFirst())]
            }
            let text = segments.map { InfoBox.join($0.map(\.text)) }.joined(separator: "\n")
            let flat = InfoBox.join(c.lines.map(\.text))
            let words = InfoBox.wordCount(flat)
            // Cümle gibi: noktalama ile biter (liste öğeleri bitmez), ≥4 sözcük, en az bir küçük harfle başlayan sözcük.
            guard words >= 4, let last = flat.last, ".!?".contains(last) || ".!?\"”’".contains(last) else { continue }
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
        /// Bu görünürlük döneminde yuvada gösterilmiş sürümler (titreyen kısa sondaki sözcük yeniden gönderilmesin).
        var history: Set<String> = []
        /// Telefon konuşması: gösterilen mesaj sayısı ve son mesaj (yeni mesaj, kısa da olsa yeniden gönderim ister).
        var shownCount = 0
        var shownLast = ""
    }
    private var tracks: [String: Track] = [:]
    private var mode: ScreenMode = .game
    private var pendingMode: ScreenMode?
    private var pendingCount = 0
    /// Mod değişimi için gereken ardışık geçiş sayısı (game↔menu titremesini önler).
    public static let modeConfirm = 2
    public init() {}

    /// Hata ayıklama/susturma için: onaylanmış (debounce edilmiş) mod.
    public var currentMode: ScreenMode { mode }

    public mutating func reset() { tracks = [:]; mode = .game; pendingMode = nil; pendingCount = 0 }

    private static func produces(_ mode: ScreenMode, _ kind: ScreenKind) -> Bool {
        switch mode {
        case .game: return kind == .objective || kind == .info || kind == .notify || kind == .vehicle || kind == .prompt
        case .map, .menu: return kind == .description || kind == .prompt || kind == .menu
        case .phone: return kind == .phone || kind == .prompt
        }
    }

    /// Bu geçişte görünen blokları ver; yapılacak eylemler döner.
    public mutating func feed(_ blocks: [ScreenBlock], mode newMode: ScreenMode, at now: TimeInterval) -> [Action] {
        var actions: [Action] = []
        if newMode == mode {
            pendingMode = nil; pendingCount = 0
        } else {
            if pendingMode == newMode { pendingCount += 1 } else { pendingMode = newMode; pendingCount = 1 }
            if pendingCount >= Self.modeConfirm {
                // Mod 2 ardışık geçişte görüldü: yeni modda üretilmeyen türleri hemen kaldır.
                for (slot, t) in tracks where !Self.produces(newMode, t.kind) {
                    if t.visible { actions.append(.clear(slot: slot, kind: t.kind)) }
                    tracks[slot] = nil
                }
                mode = newMode
                pendingMode = nil; pendingCount = 0
            }
        }
        var seen = Set<String>()
        for b in blocks {
            // Aynı yuvada birden fazla blok varsa ilk (en üstteki) alınır.
            guard seen.insert(b.slot).inserted else { continue }
            var t = tracks[b.slot] ?? Track(kind: b.kind)
            t.absentSince = nil
            if b.kind == .vehicle || b.kind == .prompt || b.kind == .menu {
                // Araç paneli / istemler / liste: kanonik hücre metni birebir aynı 2 ardışık geçişte kararlı; sayfa/içerik değişince yeniden gönder.
                let sig = b.vehicleSignature
                // Liste: seçili (beyaz zeminli) satırın OCR'ı geçişten geçişe titrer ("Stanier"/"Staniel"); birebir eşitlik
                // hiç kararlı olmaz ve satır hiç gönderilmez. Liste için satır sayısı aynı + ≥0.9 benzerlik yeterlidir.
                func same(_ a: String, _ c: String) -> Bool {
                    if a == c { return true }
                    guard b.kind == .menu, !a.isEmpty, !c.isEmpty, a.components(separatedBy: "#").last == c.components(separatedBy: "#").last,
                          a.components(separatedBy: "|").count == c.components(separatedBy: "|").count else { return false }
                    return TextMatch.isSimilar(a, c, threshold: 0.9)
                }
                if same(sig, t.candidate) { t.stable += 1 } else { t.stable = 1 }
                t.candidate = sig
                if t.stable >= 2, !(t.visible && t.shown.map { same(sig, $0) } == true) {
                    t.shown = sig; t.visible = true
                    actions.append(.translate(b))
                }
                tracks[b.slot] = t
                continue
            }
            let norm = TextMatch.normalize((b.title.map { $0 + " " } ?? "") + b.text)
            if !t.candidate.isEmpty, TextMatch.isSimilar(t.candidate, norm, threshold: Self.similarity) { t.stable += 1 } else { t.stable = 1 }
            t.candidate = norm   // her zaman en son okunanı tut (kayarak gelen bildirim tamamlanınca güncellensin)
            let lastNorm = TextMatch.normalize(b.messages.last?.text ?? "")
            // Yeni mesaj göründü ya da son mesaj değişti (kaydırma): metin ≥0.85 benzese de yeniden gönder.
            let grown = t.visible && !b.messages.isEmpty
                && (b.messages.count > t.shownCount || !TextMatch.isSimilar(t.shownLast, lastNorm, threshold: Self.similarity))
            if t.stable >= 2 {
                if t.visible, !grown, let s = t.shown, TextMatch.isSimilar(s, norm, threshold: Self.similarity),
                   norm.count < s.count + 3 {
                    // aynı metin ekranda (uzamadı): yeniden gönderme
                } else if t.visible, let s = t.shown, !norm.isEmpty, s.hasPrefix(norm + " ") {
                    // yalnızca sondaki sözcükler düştü (OCR titremesi): yeniden gönderme
                } else if t.visible, t.history.contains(norm) {
                    // bu görünürlük döneminde zaten gösterilmiş sürüm: yeniden gönderme
                } else {
                    t.shown = norm; t.visible = true; t.history.insert(norm)
                    t.shownCount = b.messages.count; t.shownLast = lastNorm
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
        if var t = tracks[slot] { t.shown = nil; t.visible = false; t.history = []; tracks[slot] = t }
    }
}

/// Altyazı bandı (kırpma) OCR'ından gelen satırlar için diyalog denetimi: kesik önizleme, telefon/bildirim içindeki satır,
/// ortalanmamış satır elenir; tam kare okuması ile çapraz doğrulama yapılır. Kutular tüm karede normalize, sol-üst başlangıç.
public enum DialogueGate {
    public enum Reason: String { case ellipsisPreview, insideExcluded, offCenter, objectiveBand }

    /// Satır kutusunun ≥%60'ı dışlanan bir alanın içinde mi (telefon alanı, bildirim kartı).
    static func isInside(_ r: CGRect, _ areas: [CGRect]) -> Bool {
        let a = max(Double(r.width * r.height), 1e-9)
        return areas.contains { area in
            let i = r.intersection(area)
            return !i.isNull && Double(i.width * i.height) / a >= 0.6
        }
    }

    private static let ellipsisRx = try! NSRegularExpression(pattern: "(\\.\\.\\.|…)\\s*$")
    private static let commaEllipsisRx = try! NSRegularExpression(pattern: ",\\s*(\\.\\.\\.|…)\\s*$")
    private static func m(_ r: NSRegularExpression, _ s: String) -> Bool { r.firstMatch(in: s, range: NSRange(s.startIndex..., in: s)) != nil }

    public static func reason(for line: ScreenLine, exclude: [CGRect]) -> Reason? {
        let r = line.rect
        let text = line.text.trimmingCharacters(in: .whitespacesAndNewlines)
        if isInside(r, exclude) { return .insideExcluded }
        if r.minY >= 0.94 { return .objectiveBand }
        let cx = Double(r.midX), w = Double(r.width)
        if abs(cx - 0.5) > 0.12 && w <= 0.35 { return .offCenter }
        // "Welcome to BuckMe, ..." gibi kesik önizleme. Gerçek diyalogda sona yaslanan "you know..." satırı korunur:
        // yalnızca ", ..." ile biten ya da ≥3 sözcüğü çok dar (<0.15) kutuya sığdırılmış satır kesik sayılır.
        if m(ellipsisRx, text) {
            if m(commaEllipsisRx, text) { return .ellipsisPreview }
            if w < 0.15, InfoBox.wordCount(text) >= 3 { return .ellipsisPreview }
        }
        return nil
    }

    /// Korunan satırların indeksleri ve elenenler.
    public static func evaluate(_ lines: [ScreenLine], exclude: [CGRect]) -> (kept: [Int], dropped: [(index: Int, reason: Reason)]) {
        var kept: [Int] = [], dropped: [(Int, Reason)] = []
        for (i, l) in lines.enumerated() {
            if let r = reason(for: l, exclude: exclude) { dropped.append((i, r)) } else { kept.append(i) }
        }
        return (kept, dropped.map { (index: $0.0, reason: $0.1) })
    }

    /// Son tam kare geçişinde (≤~1 sn önce) alt bantta ortalanmış satır kümesi kırpma metnine ≥0.6 benziyorsa o metin döner.
    public static func crossCheck(cropText: String, screen: [ScreenLine], exclude: [CGRect]) -> (text: String, rect: CGRect)? {
        let target = TextMatch.normalize(cropText)
        guard !target.isEmpty else { return nil }
        struct C { var text: String; var rect: CGRect }
        var cands: [C] = []
        for l in screen where l.confidence >= 0.5 && l.rect.minY >= 0.70 && l.rect.maxY <= 0.94 {
            if reason(for: l, exclude: exclude) != nil { continue }
            let text = OcrNoise.clean(GlyphNormalizer.normalize(l.text.trimmingCharacters(in: .whitespacesAndNewlines)))
            guard !text.isEmpty, SubtitleFilter.lineReason(text, options: SubtitleFilter.Options()) == nil else { continue }
            cands.append(C(text: text, rect: l.rect))
        }
        cands.sort { $0.rect.minY < $1.rect.minY }
        var groups: [[C]] = []
        for c in cands {
            if let p = groups.last?.last, c.rect.minY - p.rect.maxY < max(p.rect.height, c.rect.height) { groups[groups.count - 1].append(c) } else { groups.append([c]) }
        }
        var best: (text: String, rect: CGRect, score: Double)?
        for g in groups {
            let text = InfoBox.join(g.map(\.text))
            let n = TextMatch.normalize(text)
            guard TextMatch.isSimilar(n, target, threshold: 0.6) else { continue }
            let score = similarity(n, target)
            if best == nil || score > best!.score { best = (text, g.dropFirst().reduce(g[0].rect) { $0.union($1.rect) }, score) }
        }
        return best.map { ($0.text, $0.rect) }
    }

    private static func similarity(_ a: String, _ b: String) -> Double {
        var lo = 0.0, hi = 1.0
        for _ in 0..<8 { let mid = (lo + hi) / 2; if TextMatch.isSimilar(a, b, threshold: mid) { lo = mid } else { hi = mid } }
        return lo
    }
}
