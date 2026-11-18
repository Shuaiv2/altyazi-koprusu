// SPDX-License-Identifier: AGPL-3.0-only

import Foundation

/// Oyunların birkaç ekrana böldüğü cümleleri ("...very" / "fun.") çeviri öncesi birleştirir.
/// Saf mantık: zamanı dışarıdan alır, test edilebilir.
struct SentenceJoiner {
    struct Result: Equatable {
        /// Çevrilecek metin (önceki açık parçalar + bu satır, birleştirilmiş).
        var source: String
        /// Önceki bir parçanın devamı mı?
        var isContinuation: Bool
        /// Tek başına dolgu sözcüğü ("well...", "uh..."): sonraki parçayla çevrilmek üzere bekletilmeli.
        var hold: Bool
    }

    static let maxGap: TimeInterval = 6
    static let maxFragments = 3
    static let maxChars = 220

    private var fragments: [String] = []
    private var lastTime: TimeInterval = -.infinity

    mutating func reset() { fragments = []; lastTime = -.infinity }

    mutating func feed(_ raw: String, at time: TimeInterval) -> Result {
        let line = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        let continues = !fragments.isEmpty
            && time - lastTime <= Self.maxGap
            && Self.isOpen(fragments.last!)
            && Self.continues(line, after: fragments.last!)
        if !continues { fragments = [] }
        fragments.append(line)
        lastTime = time
        while fragments.count > Self.maxFragments
            || (fragments.count > 1 && Self.join(fragments).count > Self.maxChars) {
            fragments.removeFirst()
        }
        let isCont = fragments.count > 1
        let source = isCont ? Self.join(fragments) : Self.stripDash(line)
        return Result(source: source, isContinuation: isCont, hold: !isCont && Self.isFiller(line))
    }

    // MARK: - Kurallar

    static func isOpen(_ s: String) -> Bool {
        let t = s.trimmingCharacters(in: .whitespaces)
        if t.hasSuffix("...") { return true }
        guard let last = t.last else { return false }
        if ",;:—-…".contains(last) { return true }
        return !".!?\"”’)".contains(last)
    }

    private static func endsEllipsisOrComma(_ s: String) -> Bool {
        let t = s.trimmingCharacters(in: .whitespaces)
        return t.hasSuffix("...") || t.hasSuffix("…") || t.hasSuffix(",")
    }

    private static func continues(_ line: String, after prev: String) -> Bool {
        guard let first = line.first else { return false }
        if line.hasPrefix("- ") || line.hasPrefix("-") && line.dropFirst().first?.isLetter == true { return false }
        if line.range(of: #"^[A-ZÇĞİÖŞÜ][A-Za-zÇĞİÖŞÜçğıöşü0-9 ]{0,20}:"#, options: .regularExpression) != nil { return false }
        let s = line.drop(while: { $0 == "." || $0 == "…" || $0 == " " }).first ?? first
        return s.isLowercase || endsEllipsisOrComma(prev)
    }

    static func isFiller(_ s: String) -> Bool {
        let w = stripDash(s).lowercased().trimmingCharacters(in: CharacterSet(charactersIn: ".…,!? "))
        return ["well", "uh", "um", "uhm", "er", "erm", "so", "hmm", "ah", "oh", "okay", "ok", "like"].contains(w)
    }

    static func stripDash(_ s: String) -> String {
        s.hasPrefix("- ") ? String(s.dropFirst(2)) : s
    }

    static func join(_ parts: [String]) -> String {
        var out = stripDash(parts[0]).trimmingCharacters(in: .whitespaces)
        for p in parts.dropFirst() {
            var next = p.trimmingCharacters(in: .whitespaces)
            while next.hasPrefix(".") || next.hasPrefix("…") { next.removeFirst() }
            next = next.trimmingCharacters(in: .whitespaces)
            var sep = " "
            if out.hasSuffix("...") || out.hasSuffix("…") {
                out = String(out.dropLast(out.hasSuffix("…") ? 1 : 3)).trimmingCharacters(in: .whitespaces)
                if next.first?.isUppercase == true { sep = ", " }
            }
            out += sep + next
        }
        // Cümle ortasındaki "uh/um" dolgularını at.
        let cleaned = out.replacingOccurrences(of: #"\s+\b(uh|um|uhm|erm)\b[.,…]*"#, with: "",
                                               options: [.regularExpression, .caseInsensitive])
        return cleaned.isEmpty ? out : cleaned
    }
}
