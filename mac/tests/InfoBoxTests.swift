// SPDX-License-Identifier: AGPL-3.0-only
//
// Çalıştırma (repo kökünden):
//   C=core/Sources/GameTranslatorCore; swiftc -parse-as-library -o /tmp/infobox-tests mac/InfoBox.swift mac/SubtitleFilter.swift \
//     $C/OcrNoise.swift $C/TextMatch.swift $C/GameEnglish.swift mac/tests/InfoBoxTests.swift && /tmp/infobox-tests
import CoreGraphics
import Foundation

var failures = 0
var total = 0
func expect(_ ok: Bool, _ note: String, line ln: UInt = #line) {
    total += 1
    if !ok { failures += 1; print("FAIL (\(ln)): \(note)") }
}

let region = InfoBox.Region(left: 0.01, top: 0.24, right: 0.27, bottom: 0.44)
// Kırpma içinde (y yukarı) satır: frame y (üst) -> Vision y
func vline(_ text: String, frameTop: Double, h: Double = 0.026, left: Double = 0.02, w: Double = 0.18) -> OCRLine {
    let x = (left - region.left) / (region.right - region.left)
    let top = (frameTop - region.top) / (region.bottom - region.top)
    let hh = h / (region.bottom - region.top)
    return OCRLine(text: text, box: CGRect(x: x, y: 1 - top - hh, width: w / (region.right - region.left), height: hh))
}

@main struct Tests {
static func main() {
// Glif sonu
expect(InfoBox.stripTrailingGlyph("In the bedroom, you can open the wardrobe &") == "In the bedroom, you can open the wardrobe", "& atılmalı")
expect(InfoBox.stripTrailingGlyph("You can sleep to advance time =") == "You can sleep to advance time", "= atılmalı")
expect(InfoBox.stripTrailingGlyph("Collect cash from your mailbox.") == "Collect cash from your mailbox.", "nokta kalmalı")
expect(InfoBox.join(["Any main-", "tenance is your problem."]) == "Any maintenance is your problem.", "tire birleşmeli")

// Blok
let lines = [
    vline("This is your house. Brian lets you stay", frameTop: 0.28),
    vline("here but any maintenance is your problem. &", frameTop: 0.308),
    vline("Menu", frameTop: 0.40, left: 0.10, w: 0.05),
]
let block = InfoBox.bestBlock(from: lines, region: region)
expect(block?.text == "This is your house. Brian lets you stay here but any maintenance is your problem.", "blok metni: \(block?.text ?? "nil")")
expect(block.map { abs($0.box.minX - 0.02) < 0.002 && abs($0.box.minY - 0.28) < 0.002 } == true, "kutu: \(String(describing: block?.box))")
expect(InfoBox.bestBlock(from: [vline("Save game now", frameTop: 0.3)], region: region) == nil, "<4 sözcük")
expect(InfoBox.bestBlock(from: [vline("WEAPON WHEEL OPTIONS MENU", frameTop: 0.3)], region: region) == nil, "cümle değil")
let far = [vline("This is your house and it is nice.", frameTop: 0.28), vline("Another unrelated sentence here too.", frameTop: 0.40)]
expect(InfoBox.bestBlock(from: far, region: region)?.text == "This is your house and it is nice.", "dikey boşluk bloğu ayırmalı")

// Tracker
let b1 = InfoBlock(text: "This is your house. Brian lets you stay here", box: .zero, lineHeight: 0.026)
let b2 = InfoBlock(text: "This is your house. Brian lets you stay here.", box: .zero, lineHeight: 0.026)
var t = InfoBoxTracker()
expect(t.feed(b1) == .none, "ilk geçiş beklemeli")
expect(t.feed(b2) == .translate(b2), "ikinci geçiş çevirmeli")
expect(t.feed(b1) == .none, "aynı yeniden gönderilmemeli")
expect(t.feed(nil) == .none && t.feed(nil) == .none, "2 boş geçiş temizlememeli")
expect(t.feed(nil) == .clear, "3. boş geçiş temizlemeli")
expect(t.feed(nil) == .none, "çift temizleme yok")

// ResendGuard
var g = ResendGuard()
expect(g.shouldSend(["Bu ses nedir?", "What is that sound?"], at: 0), "ilk gönderim")
expect(!g.shouldSend(["Bu ses nedir?", "x"], at: 3), "aynı Türkçe 8 sn içinde")
expect(!g.shouldSend(["Başka", "What is that sound"], at: 4), "aynı İngilizce")
expect(!g.shouldSend(["Bu ses nedir ?!", ""], at: 5), "benzer")
expect(g.shouldSend(["Tamamen farklı bir cümle", "Totally different"], at: 6), "farklı metin")
expect(g.shouldSend(["Bu ses nedir?", "What is that sound?"], at: 9.5), "8 sn sonra serbest")

// GameEnglish
for (a, b) in [("He got canned yesterday.", "He got fired yesterday."), ("Don't get canned.", "Don't get fired."),
               ("About fuckin' time!", "Finally!"), ("see u b4 noon", "see u before noon"), ("2gether", "together"),
               ("I'm gonna go", "I'm going to go"), ("I wanna go", "I want to go"), ("It ain't right", "It is not right"),
               ("My bad.", "My mistake."), ("The sand canned goods", "The sand canned goods")] {
    expect(GameEnglish.plain(a) == b, "\(a) -> \(GameEnglish.plain(a))")
}

print("\(total - failures)/\(total) test geçti")
exit(failures == 0 ? 0 : 1)
}
}
