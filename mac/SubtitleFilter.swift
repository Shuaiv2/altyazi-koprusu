// SPDX-License-Identifier: AGPL-3.0-only

import CoreGraphics
import Foundation

/// Yazı tanımanın bulduğu tek satır (Vision normalize kutusu: 0...1, başlangıç sol-alt).
public struct OCRLine: Equatable {
    public var text: String
    public var box: CGRect
    public var confidence: Float

    public init(text: String, box: CGRect, confidence: Float = 1) {
        self.text = text
        self.box = box
        self.confidence = confidence
    }
}

/// Oyun altyazısı olmayan satırları (tabela, plaka, HUD sayıları, tuş ipuçları) birleştirmeden önce eler.
/// Saf mantık: arayüz ya da Vision bağımlılığı yok.
public enum SubtitleFilter {
    public struct Options {
        public var dropFewLetters = true        // < 2 harf ("1/3", "00:12", "$500")
        public var dropDigitHeavy = true        // rakam ağırlıklı ya da plaka biçimli
        public var dropShortCaps = true         // ≤2 sözcüklük BÜYÜK HARF ("EXIT", "GAS STATION")
        public var dropButtonPrompts = true     // "Press ⓧ", "X Atla", "Hold R2", "Skip"
        public var dropLowConfidence = true     // diğer satırlara göre çok düşük güven
        public var useGeometry = true           // merkez ve satır yüksekliği kuralları
        public var centerTolerance = 0.30       // baskın satırın merkezinden en çok bu kadar (kırpma genişliği oranı)
        public var heightTolerance = 0.45       // referans satır yüksekliğinden en çok bu oranda sapma
        public init() {}
    }

    public enum Reason: String {
        case fewLetters, digitHeavy, shortCaps, buttonPrompt, lowConfidence, offCenter, heightMismatch
    }

    public struct Result {
        public var kept: [OCRLine]
        public var dropped: [(line: OCRLine, reason: Reason)]
    }

    public static func filter(_ lines: [OCRLine], options: Options = Options()) -> [OCRLine] {
        evaluate(lines, options: options).kept
    }

    /// Hata ayıklama günlüğü için: elenen satırlar ve nedenleri.
    public static func evaluate(_ lines: [OCRLine], options: Options = Options()) -> Result {
        var kept: [OCRLine] = []
        var dropped: [(OCRLine, Reason)] = []

        // 1) Satır düzeyi kurallar
        for line in lines {
            if let reason = lineReason(line.text, options: options) { dropped.append((line, reason)) } else { kept.append(line) }
        }

        // 2) Güven: kalan satırlar arasında belirgin düşük olanlar
        if options.dropLowConfidence, kept.count >= 2, let best = kept.map(\.confidence).max() {
            var next: [OCRLine] = []
            for line in kept {
                if line.confidence < best * 0.5 && line.confidence < 0.6 { dropped.append((line, .lowConfidence)) } else { next.append(line) }
            }
            kept = next
        }

        // 3) Geometri
        if options.useGeometry, kept.count >= 2 {
            let dominant = kept.max { letterCount($0.text) < letterCount($1.text) }!
            let heights = kept.map { Double($0.box.height) }.sorted()
            let reference = kept.count >= 3 ? heights[heights.count / 2] : Double(dominant.box.height)
            let dMid = Double(dominant.box.midX)
            let dMin = Double(dominant.box.minX), dMax = Double(dominant.box.maxX)
            var next: [OCRLine] = []
            for line in kept {
                if line == dominant { next.append(line); continue }
                let farCenter = abs(Double(line.box.midX) - dMid) > options.centerTolerance
                // Sola yaslı çok satırlı altyazıda kısa satır baskın satırın kapladığı aralığın içindedir.
                let inside = Double(line.box.minX) >= dMin - 0.02 && Double(line.box.maxX) <= dMax + 0.02
                if farCenter && !inside {
                    dropped.append((line, .offCenter))
                } else if reference > 0, abs(Double(line.box.height) - reference) / reference > options.heightTolerance {
                    dropped.append((line, .heightMismatch))
                } else {
                    next.append(line)
                }
            }
            kept = next
        }
        return Result(kept: kept, dropped: dropped.map { (line: $0.0, reason: $0.1) })
    }

    // MARK: - Satır kuralları

    static func lineReason(_ raw: String, options: Options) -> Reason? {
        let text = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let letters = letterCount(text)
        let digits = text.filter(\.isNumber).count
        let hasLower = text.contains { $0.isLowercase }
        let tokens = text.split(whereSeparator: \.isWhitespace).map(String.init)
        let wordTokens = tokens.filter { $0.contains(where: \.isLetter) }

        if options.dropFewLetters, letters < 2 { return .fewLetters }

        if options.dropButtonPrompts, isButtonPrompt(text, tokens: tokens) { return .buttonPrompt }

        if options.dropDigitHeavy {
            if digits >= letters { return .digitHeavy }
            // Küçük harfsiz, rakamlı, kısa: plaka / kod ("34 ABC 123", "AB-1234-XY")
            if !hasLower, digits > 0, wordTokens.count <= 4, tokens.count <= 4, !isSpeakerLabeled(text) { return .digitHeavy }
        }

        if options.dropShortCaps, !hasLower, wordTokens.count <= 2, !isSpeakerLabeled(text) {
            if !endsWithTerminalPunctuation(text) { return .shortCaps }
        }
        return nil
    }

    /// Cümle sonu noktalaması (. ! ? …), ardından isteğe bağlı kapanan tırnak/parantez. Tabela ve menüler böyle bitmez.
    static func endsWithTerminalPunctuation(_ text: String) -> Bool {
        let closers: Set<Character> = ["\"", "'", "”", "’", "»", ")", "]"]
        var t = Substring(text.trimmingCharacters(in: .whitespacesAndNewlines))
        while let l = t.last, closers.contains(l) { t.removeLast() }
        guard let l = t.last else { return false }
        return ".!?…".contains(l)
    }

    static func letterCount(_ s: String) -> Int { s.filter(\.isLetter).count }

    /// "NAME: metin": iki noktadan sonra metin var.
    static func isSpeakerLabeled(_ text: String) -> Bool {
        text.range(of: "^[\\p{L}][\\p{L}\\p{N} .'’\\-]{0,24}:\\s*\\S", options: .regularExpression) != nil
    }

    private static let strongGlyphs: Set<String> = [
        "○", "△", "□", "✕", "✖", "⨯", "ⓧ", "Ⓧ", "◯", "⊗",
        "R1", "R2", "R3", "L1", "L2", "L3", "OPTIONS", "TOUCHPAD",
    ]
    private static let glyphSymbols = CharacterSet(charactersIn: "○△□✕✖⨯ⓧⓍ◯⊗")
    private static let ambiguousGlyphs: Set<String> = ["X", "O"]
    private static let promptKeywords: Set<String> = [
        "skip", "atla", "continue", "devam", "press", "hold", "tap", "bas", "back", "geri", "select", "seç", "sec",
        "confirm", "onayla", "cancel", "iptal", "close", "kapat", "retry", "resume", "accept", "kabul", "cutscene",
        "to", "için", "icin", "button", "düğme", "tuş", "tus", "tut",
    ]
    private static let menuWords: Set<String> = [
        "skip", "atla", "continue", "devam", "back", "geri", "select", "seç", "sec", "confirm", "onayla",
        "cancel", "iptal", "cutscene", "ara", "sahneyi", "sahne", "resume", "retry", "close", "kapat", "et",
    ]

    private static func stripEdges(_ token: String) -> String {
        token.trimmingCharacters(in: CharacterSet(charactersIn: "()[]<>{}.,:;!?\"'“”‘’«»|"))
    }

    static func isButtonPrompt(_ text: String, tokens: [String]) -> Bool {
        let stripped = tokens.map(stripEdges).filter { !$0.isEmpty }
        let lowered = stripped.map { $0.lowercased() }
        let hasSymbolGlyph = text.unicodeScalars.contains { glyphSymbols.contains($0) }
        let strongTokens = stripped.filter { strongGlyphs.contains($0) }
        let hasAmbiguous = stripped.contains { ambiguousGlyphs.contains($0) }
        let hasKeyword = lowered.contains { promptKeywords.contains($0) }
        let otherWords = stripped.filter {
            !strongGlyphs.contains($0) && !ambiguousGlyphs.contains($0) && $0.contains(where: \.isLetter)
        }

        // Kesin düğme simgesi/adı + en çok 4 başka sözcük
        if (hasSymbolGlyph || !strongTokens.isEmpty), otherWords.count <= 4 { return true }
        // Belirsiz "X"/"O": yalnız yanında ipucu sözcüğü varsa ("Malcolm X" altyazıdır)
        if hasAmbiguous, hasKeyword, otherWords.count <= 4 { return true }

        // Hold/Press/Tap/Basılı tut/Bas ile başlayan ipuçları ("Hold on!" altyazıdır)
        if let first = lowered.first {
            let startsPrompt = ["hold", "press", "tap"].contains(first) || first == "bas"
                || (first == "basılı" && lowered.count > 1 && lowered[1] == "tut")
            if startsPrompt {
                let second = lowered.count > 1 ? lowered[1] : ""
                let hasButtonWord = lowered.contains { ["button", "buttons", "key", "düğme", "düğmesi", "tuş", "tuşu", "tuşuna"].contains($0) }
                if hasButtonWord || second == "to" || lowered.contains("to") && lowered.count <= 5 || lowered.count == 1 {
                    return true
                }
            }
        }
        // "... to continue/skip", "devam etmek için" kalıbı kısa satırda
        if stripped.count <= 6 {
            let joined = lowered.joined(separator: " ")
            if joined.range(of: "\\b(to|için|icin) (continue|skip|start|retry|resume|devam|atla|başla|basla)\\b", options: .regularExpression) != nil,
               hasButtonish(lowered) { return true }
        }

        // Kısa menü sözcüğü satırı: "Skip", "Atla", "Back", "Geri"
        if !otherWords.isEmpty, otherWords.count <= 2, otherWords.allSatisfy({ menuWords.contains($0.lowercased()) }),
           !endsWithTerminalPunctuation(text) {
            return true
        }
        return false
    }

    private static func hasButtonish(_ lowered: [String]) -> Bool {
        lowered.contains { ["press", "hold", "tap", "bas", "basın", "dokun", "any", "herhangi"].contains($0) }
    }
}
