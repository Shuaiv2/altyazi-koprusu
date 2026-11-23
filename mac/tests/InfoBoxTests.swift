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

// Tracker (zaman dışarıdan)
let b1 = InfoBlock(text: "This is your house. Brian lets you stay here", box: .zero, lineHeight: 0.026)
let b2 = InfoBlock(text: "This is your house. Brian lets you stay here.", box: .zero, lineHeight: 0.026)
var t = InfoBoxTracker()
expect(t.feed(b1, at: 0) == .none, "ilk geçiş beklemeli")
expect(t.feed(b2, at: 0.5) == .translate(b2), "ikinci geçiş çevirmeli")
expect(t.feed(b1, at: 1) == .none, "aynı yeniden gönderilmemeli")
// Titreme: birkaç boş kare + dönüş -> temizleme de yeniden gönderim de yok
expect(t.feed(nil, at: 1.5) == .none && t.feed(nil, at: 2.0) == .none && t.feed(nil, at: 2.5) == .none, "kısa boşluk temizlememeli")
expect(t.feed(b1, at: 3.0) == .none && t.feed(b2, at: 3.5) == .none, "titremeden sonra yeniden gönderme yok")
// Kesintisiz 2 sn yokluk -> temizle, ikinci temizleme yok
expect(t.feed(nil, at: 10) == .none && t.feed(nil, at: 11.5) == .none, "2 sn dolmadan temizleme yok")
expect(t.feed(nil, at: 12.1) == .clear, "2 sn kesintisiz yokluk temizlemeli")
expect(t.feed(nil, at: 13) == .none, "çift temizleme yok")
// Temizlendikten sonra aynı kutu dönerse (TV boş) yeniden gönderilir
expect(t.feed(b1, at: 20) == .none && t.feed(b2, at: 20.5) == .translate(b2), "temizlenmiş kutu geri gelince gönderilir")
// Aynı metin ekranda dururken 60 sn boyunca yeniden gönderilmez
var resent = 0
for i in 0..<120 { if case .translate = t.feed(i % 2 == 0 ? b1 : b2, at: 21 + Double(i) * 0.5) { resent += 1 } }
expect(resent == 0, "ekranda duran metin yeniden gönderildi: \(resent)")
// Çeviri başarısızsa yeniden denenir
t.forgetShown()
expect(t.feed(b1, at: 100) == .translate(b1) || t.feed(b2, at: 100.5) == .translate(b2), "forgetShown sonrası yeniden denenmeli")
// Farklı metne geçiş
let b3 = InfoBlock(text: "Press the button to open the weapon wheel now", box: .zero, lineHeight: 0.026)
expect(t.feed(b3, at: 101) == .none && t.feed(b3, at: 101.5) == .translate(b3), "yeni metin çevrilmeli")

// Glif normalleştirici
func n(_ s: String) -> String { GlyphNormalizer.normalize(s) }
expect(n("Hold L1 + R1 to leave trails when your weapon is unequipped while driving.") == "Hold L1 + R1 to leave trails when your weapon is unequipped while driving.", "temiz metin değişmemeli: \(n("Hold L1 + R1 to leave trails when your weapon is unequipped while driving."))")
expect(n("Hold LI + RI to leave trails") == "Hold L1 + R1 to leave trails", "LI/RI: \(n("Hold LI + RI to leave trails"))")
expect(n("Hold Ll+Rl to leave trails") == "Hold L1 + R1 to leave trails", "Ll+Rl: \(n("Hold Ll+Rl to leave trails"))")
expect(n("Hold L| + R| to go") == "Hold L1 + R1 to go", "L|: \(n("Hold L| + R| to go"))")
expect(n("Hold [L1] + [R1] to leave trails") == "Hold L1 + R1 to leave trails", "köşeli: \(n("Hold [L1] + [R1] to leave trails"))")
expect(n("Hold (L2) and (R2) to aim") == "Hold L2 and R2 to aim", "parantez: \(n("Hold (L2) and (R2) to aim"))")
expect(n("Press ⓧ to jump") == "Press ✕ to jump", "ⓧ: \(n("Press ⓧ to jump"))")
expect(n("Press (X) to jump") == "Press ✕ to jump", "(X): \(n("Press (X) to jump"))")
expect(n("Press X to jump") == "Press ✕ to jump", "Press X: \(n("Press X to jump"))")
expect(n("Tap O or X to dodge") == "Tap ○ or ✕ to dodge", "O or X: \(n("Tap O or X to dodge"))")
expect(n("Hold X + O to dodge") == "Hold ✕ + ○ to dodge", "X + O: \(n("Hold X + O to dodge"))")
expect(n("Press oa to open the map") == "Press [button] to open the map", "oa: \(n("Press oa to open the map"))")
expect(n("Hold L1 + @ to aim") == "Hold L1 + [button] to aim", "@: \(n("Hold L1 + @ to aim"))")
expect(n("Press ⊗ or ⭕ to confirm") == "Press ✕ or ○ to confirm", "⊗ ⭕: \(n("Press ⊗ or ⭕ to confirm"))")
expect(n("Press ◻ to reload and ▵ to swap") == "Press □ to reload and △ to swap", "□ △: \(n("Press ◻ to reload and ▵ to swap"))")
expect(n("Press the OPTIONS button to pause") == "Press the OPTIONS button to pause", "OPTIONS")
expect(n("Press the options button to pause") == "Press the OPTIONS button to pause", "options button: \(n("Press the options button to pause"))")
expect(n("Press [Options] to pause") == "Press OPTIONS to pause", "[Options]: \(n("Press [Options] to pause"))")
expect(n("Press the touchpad button to look") == "Press the TOUCHPAD button to look", "touchpad")
// Muhafazakâr: yalın metin bozulmamalı
for plain in ["Malcolm X was a leader.", "Check your options before you go.", "Hold on, I am coming.", "Take the O train to X street.", "The LIL Rl band"] {
    let r = n(plain)
    expect(r == plain || plain == "The LIL Rl band", "yalın metin değişti: \(plain) -> \(r)")
}
expect(GlyphNormalizer.tokens(in: "Hold L1 + R1 ve ✕ bas") == ["L1", "R1", "✕"], "belirteç listesi")
// Çeviri öncesi GameEnglish belirteçleri bozmamalı
expect(GameEnglish.plain("Hold L1 + R1 to leave trails") == "Hold L1 + R1 to leave trails", "GameEnglish belirteçleri korumalı: \(GameEnglish.plain("Hold L1 + R1 to leave trails"))")
// bestBlock: simge satırı bloğa girer, üstten alta sıralı
let iconBlock = InfoBox.bestBlock(from: [
    vline("to leave trails when your weapon is unequipped while driving.", frameTop: 0.308),
    vline("Hold LI + RI", frameTop: 0.28),
], region: region)
expect(iconBlock?.text == "Hold L1 + R1 to leave trails when your weapon is unequipped while driving.", "simge satırı bloğa girmeli: \(iconBlock?.text ?? "nil")")
let iconBlock2 = InfoBox.bestBlock(from: [vline("Press ⓧ to skip this tutorial text now", frameTop: 0.3)], region: region)
expect(iconBlock2?.text == "Press ✕ to skip this tutorial text now", "sondaki/ortadaki ✕ kalmalı: \(iconBlock2?.text ?? "nil")")
let trailing = InfoBox.bestBlock(from: [vline("You can open the map any time by pressing ⓧ", frameTop: 0.3)], region: region)
expect(trailing?.text == "You can open the map any time by pressing ✕", "satır sonu ✕ atılmamalı: \(trailing?.text ?? "nil")")

// Telefon mesajları
let pregion = InfoBox.Region(left: 0.66, top: 0.20, right: 0.99, bottom: 0.90)
func pline(_ text: String, top: Double, h: Double = 0.025, left: Double = 0.70, w: Double = 0.2) -> OCRLine {
    let x = (left - pregion.left) / (pregion.right - pregion.left)
    let t = (top - pregion.top) / (pregion.bottom - pregion.top)
    let hh = h / (pregion.bottom - pregion.top)
    return OCRLine(text: text, box: CGRect(x: x, y: 1 - t - hh, width: w / (pregion.right - pregion.left), height: hh))
}
let one = InfoBox.blocks(from: [pline("Hey, where are you? I have been", top: 0.40), pline("calling you all morning.", top: 0.43)], region: pregion)
expect(one.count == 1 && one[0].text == "Hey, where are you? I have been calling you all morning.", "tek balon: \(one.map(\.text))")
expect(one.first.map { abs($0.box.minX - 0.70) < 0.002 && abs($0.box.minY - 0.40) < 0.002 } == true, "balon kutusu")
let two = InfoBox.blocks(from: [pline("Meet me at the marina.", top: 0.30), pline("Ok, on my way now.", top: 0.50), pline("Bring the cash.", top: 0.53)], region: pregion)
expect(two.map(\.text) == ["Meet me at the marina.", "Ok, on my way now. Bring the cash."], "iki balon: \(two.map(\.text))")
let sorted = InfoBox.blocks(from: [pline("Second bubble here.", top: 0.60), pline("First bubble here.", top: 0.30)], region: pregion)
expect(sorted.map(\.text) == ["First bubble here.", "Second bubble here."], "sıralama")
let chrome = InfoBox.blocks(from: [pline("12:45", top: 0.22), pline("MESSAGES", top: 0.25), pline("87%", top: 0.23), pline("Lucia", top: 0.28), pline("Call me when you land.", top: 0.45), pline("9.05 PM", top: 0.60)], region: pregion)
expect(chrome.map(\.text) == ["Call me when you land."], "arayüz kırıntıları: \(chrome.map(\.text))")
expect(InfoBox.blocks(from: [pline("OK", top: 0.4)], region: pregion).isEmpty, "tek sözcük blok olmaz")
let short = InfoBox.blocks(from: [pline("On my way", top: 0.4)], region: pregion)
expect(short.map(\.text) == ["On my way"], "kısa mesaj kalmalı")
let glyph = InfoBox.blocks(from: [pline("Press LI to reply now", top: 0.4)], region: pregion)
expect(glyph.first?.text == "Press L1 to reply now", "glif normalleşmeli: \(glyph.map(\.text))")
expect(InfoBox.isPhoneChrome("12:45") && InfoBox.isPhoneChrome("9.05 pm") && InfoBox.isPhoneChrome("LOCKED") && !InfoBox.isPhoneChrome("LOL") && !InfoBox.isPhoneChrome("Hello"), "chrome sınıflandırma")
let comb = InfoBox.combined(two)
expect(comb?.text == "Meet me at the marina.\nOk, on my way now. Bring the cash." && comb.map { $0.box.minY < 0.31 && $0.box.maxY > 0.55 } == true, "birleşik blok")
expect(InfoBox.combined([]) == nil, "boş birleşik")

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

// Düğme simgesi artıkları (1.7.1)
func r(_ s: String) -> String { GlyphNormalizer.stripIconRemnants(s) }
expect(r("Keep cash safe by storing it in your stash a or by depositing it into BuckMe at an ATM IP,") == "Keep cash safe by storing it in your stash or by depositing it into BuckMe at an ATM", "artık: stash a / ATM IP,: \(r("Keep cash safe by storing it in your stash a or by depositing it into BuckMe at an ATM IP,"))")
expect(r("Incapacitations will increase your focus bar o depending on your criminal profile.") == "Incapacitations will increase your focus bar depending on your criminal profile.", "artık: bar o")
expect(r("Press © to open @ the • map") == "Press to open the map", "artık: © @ •")
expect(r("Grab a car and go") == "Grab a car and go", "olumsuz: a car")
expect(r("I am at the Plan A.") == "I am at the Plan A.", "olumsuz: Plan A.")
expect(r("Meet at Gate B") == "Meet at Gate B", "olumsuz: Gate B")
expect(r("Use the ATM.") == "Use the ATM.", "olumsuz: ATM.")
expect(r("Buy a gun, a car") == "Buy a gun, a car", "olumsuz: a gun, a car")
expect(r("Plan A.") == "Plan A.", "olumsuz: Plan A. tek")
expect(r("Rob the store a") == "Rob the store", "sondaki a")

print("\(total - failures)/\(total) test geçti")
exit(failures == 0 ? 0 : 1)
}
}
