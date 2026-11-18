// SPDX-License-Identifier: AGPL-3.0-only
//
// Çalıştırma (repo kökünden):
//   swiftc -o /tmp/subtitle-filter-tests mac/SubtitleFilter.swift \
//     core/Sources/GameTranslatorCore/SubtitleStabilizer.swift core/Sources/GameTranslatorCore/OcrNoise.swift \
//     core/Sources/GameTranslatorCore/TextMatch.swift mac/tests/SubtitleFilterTests.swift && /tmp/subtitle-filter-tests
import CoreGraphics
import Foundation

var failures = 0
var total = 0

func line(_ text: String, x: Double = 0.2, w: Double = 0.6, y: Double = 0.5, h: Double = 0.2, conf: Float = 0.9) -> OCRLine {
    OCRLine(text: text, box: CGRect(x: x, y: y, width: w, height: h), confidence: conf)
}

/// Tek satırlı kare: tutulmalı mı?
func check(_ text: String, keep: Bool, _ note: String = "", file: StaticString = #file, line ln: UInt = #line) {
    total += 1
    let kept = !SubtitleFilter.filter([line(text)]).isEmpty
    if kept != keep {
        failures += 1
        print("FAIL (\(ln)): \"\(text)\" beklenen \(keep ? "tut" : "at"), sonuç \(kept ? "tut" : "at") \(note)")
    }
}

func checkFrame(_ lines: [OCRLine], expect: [String], _ note: String, line ln: UInt = #line) {
    total += 1
    let got = SubtitleFilter.filter(lines).map(\.text)
    if got != expect {
        failures += 1
        print("FAIL (\(ln)) \(note): beklenen \(expect), sonuç \(got)")
    }
}

// MARK: - Kararlılık hattı (OcrNoise → filtre → birleştir → SubtitleStabilizer)
func pipeline(_ frames: [[OCRLine]]) -> [StabilizerAction] {
    var s = SubtitleStabilizer()
    return frames.map { lines in
        let cleaned = lines.map { OCRLine(text: OcrNoise.clean($0.text), box: $0.box, confidence: $0.confidence) }
        let text = SubtitleFilter.filter(cleaned).map(\.text).joined(separator: " ")
        return s.feed(text)
    }
}
@main struct Tests {
static func main() {
// Plaka, HUD, sayılar
check("34 ABC 123", keep: false)
check("AB-1234-XY", keep: false)
check("00:12", keep: false)
check("1/3", keep: false)
check("$500", keep: false)
check("100", keep: false)
check("ABC 1234", keep: false)
// Tabela
check("EXIT", keep: false)
check("POLICE", keep: false)
check("GAS STATION", keep: false)
check("ROUTE 66", keep: false)
// Tuş ipuçları EN/TR
check("X Atla", keep: false)
check("Press ⓧ to continue", keep: false)
check("Hold R2", keep: false)
check("Press X to continue", keep: false)
check("Hold R2 to aim", keep: false)
check("Basılı tut R2", keep: false)
check("Bas ⓧ", keep: false)
check("Press any button to continue", keep: false)
check("Tap to continue", keep: false)
check("OPTIONS Atla", keep: false)
check("Skip", keep: false)
check("Atla", keep: false)
check("Geri", keep: false)
check("Select", keep: false)
check("Devam", keep: false)
check("△ Back", keep: false)
// Altyazılar
check("We need to get out of here before they find us.", keep: true)
check("MARCUS: We need to move.", keep: true)
check("Marcus: Where is everybody?", keep: true)
check("Run!", keep: true)
check("What?", keep: true)
check("STOP!", keep: true)
check("WHAT?!", keep: true)
check("Hold on! I'm coming.", keep: true)
check("Hold on!", keep: true)
check("Press the attack, now!", keep: true)
check("Malcolm X", keep: true)
check("Bunu yapmak zorunda değildin, Çağrı.", keep: true)
check("Işığı kapat, hemen şimdi.", keep: true)
check("ÖĞRETMEN: Şimdi gidelim.", keep: true)
check("I'm at 3rd and Main, send backup.", keep: true)
check("THE ENEMY IS NEAR US", keep: true)
check("Back off!", keep: true)
check("WAIT.", keep: true)
check("NO.", keep: true)
check("Back.", keep: true)
check("\"WAIT.\"", keep: true)
check("Geri", keep: false)
check("Back", keep: false)
check("Back here in ten minutes.", keep: true)
check("It's over, Joel.", keep: true)

// Kare düzeyi
checkFrame([line("We need to get out of here", y: 0.6), line("Press ⓧ to continue", y: 0.2)],
           expect: ["We need to get out of here"], "altyazı + ipucu")
checkFrame([line("I told you not to come back here,", y: 0.6), line("and you did it anyway.", y: 0.4)],
           expect: ["I told you not to come back here,", "and you did it anyway."], "iki satırlı altyazı")
checkFrame([line("Where were you last night?", x: 0.2, w: 0.6, h: 0.2), line("Joe's diner", x: 0.02, w: 0.15, h: 0.08)],
           expect: ["Where were you last night?"], "merkez dışı küçük tabela")
checkFrame([line("Where were you last night?", x: 0.2, w: 0.6, h: 0.2), line("Joe's diner open", x: 0.4, w: 0.2, h: 0.07)],
           expect: ["Where were you last night?"], "ortada ama küçük puntolu tabela")
checkFrame([line("Where were you last night? I called twice", x: 0.05, w: 0.8), line("and nobody answered.", x: 0.05, w: 0.35)],
           expect: ["Where were you last night? I called twice", "and nobody answered."], "sola yaslı altyazı korunur")
checkFrame([line("Nereye gidiyorsun?", conf: 0.9), line("Nereye gdyrsn", y: 0.3, conf: 0.36)],
           expect: ["Nereye gidiyorsun?"], "düşük güvenli çöp")
checkFrame([line("34 ABC 123"), line("EXIT")], expect: [], "yalnız dünya yazıları")
// Seçenekler kapatılınca
total += 1
var off = SubtitleFilter.Options()
off.dropShortCaps = false; off.dropDigitHeavy = false; off.dropFewLetters = false; off.dropButtonPrompts = false
if SubtitleFilter.filter([line("EXIT")], options: off).count != 1 { failures += 1; print("FAIL seçenekler kapalı") }
// Neden bildirimi
total += 1
let r = SubtitleFilter.evaluate([line("EXIT")])
if r.dropped.first?.reason != .shortCaps { failures += 1; print("FAIL neden") }

let prompt = line("Press ⓧ to continue", y: 0.2)
let sub = line("We should head to the old harbor", y: 0.6)
let seq: [[OCRLine]] = [
    [prompt],                                   // yalnız ipucu: hiçbir şey
    [line("The ship")],          // daktilo: değişiyor
    [line("The shipment arrives at")],
    [line("The shipment arrives at midnight and the guards")],
    [sub, prompt], [sub, prompt], [sub], [sub], // kararlı: bir kez gönder
    [], [],                                     // kayboldu: temizle
]
let actions = pipeline(seq)
let translates = actions.filter { if case .translate = $0 { return true } else { return false } }
total += 1
if translates.count != 1 || translates.first != .translate("We should head to the old harbor") { failures += 1; print("FAIL kararlılık gönderim: \(actions)") }
total += 1
if actions.filter({ $0 == .clear }).count != 1 || actions.last != .clear { failures += 1; print("FAIL kararlılık temizleme: \(actions)") }
total += 1
if actions[0] != .none || actions[1] != .none || actions[2] != .none || actions[3] != .none { failures += 1; print("FAIL daktilo gönderildi: \(actions)") }

print("\(total - failures)/\(total) test geçti")
exit(failures == 0 ? 0 : 1)
}
}
