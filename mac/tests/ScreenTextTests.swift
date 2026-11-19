// SPDX-License-Identifier: AGPL-3.0-only
//
// Çalıştırma: bkz. mac/tests/README.md
// Fikstürler araclar/izleme/ocr.tsv'den (GTA 6 tam kare OCR) elle seçilmiş oyun metni satırlarıdır
// (kare numarası yorumda). PS sistem bindirmesi ya da kullanıcı adı İÇERMEZ; PS bildirimi satırı sentetiktir.
import CoreGraphics
import Foundation

var failures = 0
var total = 0
func expect(_ ok: Bool, _ note: String, line ln: UInt = #line) {
    total += 1
    if !ok { failures += 1; print("FAIL (\(ln)): \(note)") }
}

/// (x0, y0, x1, y1, güven, metin) — ocr.tsv sütun sırası, sol-üst başlangıç.
typealias Row = (Double, Double, Double, Double, Float, String)
func lines(_ rows: [Row]) -> [ScreenLine] {
    rows.map { ScreenLine(text: $0.5, rect: CGRect(x: $0.0, y: $0.1, width: $0.2 - $0.0, height: $0.3 - $0.1), confidence: $0.4) }
}
// Her karede bulunan sentetik PS bildirimi (gerçek satır kopyalanmadı)
let psToast: Row = (0.411, 0.047, 0.589, 0.075, 1.0, "A Uzaktan Oynama baglandi.")
let hud: [Row] = [(0.924, 0.026, 0.962, 0.054, 1.0, "$500"), (0.911, 0.059, 0.962, 0.090, 1.0, "$2,441")]

@main struct Tests {
static func main() {
// --- Kare 0054: harita, açıklama + liste + istemler
let f54 = lines([psToast,
    (0.016, 0.038, 0.073, 0.076, 1.0, "STORY"), (0.081, 0.121, 0.156, 0.145, 1.0, "Earn Money"),
    (0.080, 0.181, 0.224, 0.209, 1.0, "Find Donnie's Package"), (0.080, 0.240, 0.218, 0.269, 1.0, "Set Up Bank Account"),
    (0.478, 0.500, 0.523, 0.531, 1.0, "0.40 mi"),
    (0.576, 0.434, 0.722, 0.465, 1.0, "SET UP BANK ACCOUNT"),
    (0.547, 0.483, 0.797, 0.509, 1.0, "Lucia needs a bank account to get her"),
    (0.547, 0.519, 0.801, 0.545, 1.0, "community service payments. Meet her"),
    (0.547, 0.556, 0.683, 0.581, 1.0, "at the Fleeca nearby."),
    (0.905, 0.757, 0.961, 0.781, 1.0, "RECENTER"), (0.917, 0.806, 0.959, 0.830, 1.0, "2D VIEW"),
    (0.891, 0.852, 0.961, 0.879, 1.0, "ADD MARKER"), (0.871, 0.902, 0.959, 0.925, 1.0, "SET DESTINATION"),
    (0.894, 0.951, 0.959, 0.974, 1.0, "CLOSE MENU")] + hud.map { $0 })
let r54 = ScreenText.analyze(f54)
expect(r54.mode == .map, "0054 mod map: \(r54.mode)")
expect(r54.blocks.count == 1, "0054 tek blok: \(r54.blocks.map(\.text))")
expect(r54.blocks.first?.kind == .description, "0054 tür")
expect(r54.blocks.first?.text == "Lucia needs a bank account to get her community service payments. Meet her at the Fleeca nearby.", "0054 metin: \(r54.blocks.first?.text ?? "nil")")
expect(r54.blocks.first?.title == "SET UP BANK ACCOUNT", "0054 başlık: \(r54.blocks.first?.title ?? "nil")")

// --- Kare 0058: harita, tek cümlelik açıklama (2 satır)
let f58 = lines([psToast,
    (0.064, 0.119, 0.151, 0.145, 1.0, "- GAS STATIONS"), (0.080, 0.181, 0.209, 0.209, 1.0, "247 Gas - Key Lento"),
    (0.081, 0.240, 0.206, 0.261, 1.0, "Arrow Gas - Hamlet"), (0.081, 0.300, 0.228, 0.323, 1.0, "Globe Oil Gas - Hamlet"),
    (0.080, 0.419, 0.198, 0.442, 1.0, "Xero Gas - Hamlet"),
    (0.545, 0.449, 0.682, 0.481, 1.0, "247 GAS - KEY LENTO"),
    (0.547, 0.496, 0.781, 0.525, 1.0, "Fill up on fuel or quickly service your"), (0.546, 0.532, 0.597, 0.559, 1.0, "vehicle."),
    (0.905, 0.757, 0.961, 0.781, 1.0, "RECENTER"), (0.891, 0.852, 0.961, 0.879, 1.0, "ADD MARKER"), (0.894, 0.951, 0.959, 0.974, 1.0, "CLOSE MENU")])
let r58 = ScreenText.analyze(f58)
expect(r58.blocks.map(\.text) == ["Fill up on fuel or quickly service your vehicle."] && r58.blocks.first?.title == "247 GAS - KEY LENTO", "0058: \(r58.blocks.map(\.text)) \(r58.blocks.first?.title ?? "")")

// --- Kare 0086: sol üst bilgi kutusu (2. satırı simge sağa itmiş)
let f86 = lines([psToast, (0.689, 0.098, 0.837, 0.199, 0.5, "FLEECA"),
    (0.025, 0.282, 0.218, 0.308, 1.0, "Call Joey from your Contacts"), (0.048, 0.312, 0.225, 0.339, 1.0, "or check the engine to tow"),
    (0.025, 0.344, 0.176, 0.370, 1.0, "and repair your vehicle."), (0.685, 0.470, 0.705, 0.488, 0.5, "58 ft")])
let r86 = ScreenText.analyze(f86)
expect(r86.mode == .game, "0086 mod game")
expect(r86.blocks.count == 1 && r86.blocks[0].kind == .info && r86.blocks[0].text == "Call Joey from your Contacts or check the engine to tow and repair your vehicle.", "0086: \(r86.blocks.map(\.text))")

// --- Kare 0114: mesaj bildirimi (gönderen Jason), diyalog altyazısı ve tabelalar dışarıda
let f114 = lines([psToast, (0.802, 0.098, 0.815, 0.121, 0.5, "FL"), (0.330, 0.336, 0.356, 0.354, 0.5, "FLEECA"),
    (0.249, 0.176, 0.326, 0.202, 0.5, "BANK"), (0.438, 0.881, 0.563, 0.915, 1.0, "Mind if I drive?"),
    (0.831, 0.811, 0.871, 0.830, 1.0, "Jason"), (0.805, 0.850, 0.965, 0.876, 1.0, "Hey I'm at the bank. Let's"),
    (0.804, 0.881, 0.930, 0.907, 1.0, "set up this account."), (0.898, 0.940, 0.951, 0.964, 1.0, "→ Lucia")])
let r114 = ScreenText.analyze(f114)
expect(r114.blocks.count == 1 && r114.blocks[0].kind == .notify, "0114 tek notify: \(r114.blocks.map(\.text))")
expect(r114.blocks.first?.text == "Hey I'm at the bank. Let's set up this account.", "0114 metin: \(r114.blocks.first?.text ?? "nil")")
expect(r114.blocks.first?.speaker == "Jason", "0114 gönderen")

// --- Kare 0065 ve 0115: diğer mesajlar
let r65 = ScreenText.analyze(lines([(0.823, 0.814, 0.869, 0.835, 0.5, "Kenton"), (0.798, 0.863, 0.955, 0.884, 1.0, "Still can't believe u found"),
    (0.797, 0.897, 0.969, 0.922, 1.0, "them doubloons. U got that"), (0.795, 0.928, 0.972, 0.953, 1.0, "luck boy. Need u out there...")]))
expect(r65.blocks.first?.text == "Still can't believe u found them doubloons. U got that luck boy. Need u out there..." && r65.blocks.first?.speaker == "Kenton", "0065: \(r65.blocks.map(\.text))")
let r115 = ScreenText.analyze(lines([(0.833, 0.868, 0.869, 0.889, 1.0, "Lucia"), (0.808, 0.904, 0.969, 0.933, 1.0, "OMW get started without"), (0.810, 0.941, 0.834, 0.959, 1.0, "me.")]))
expect(r115.blocks.first?.text == "OMW get started without me." && r115.blocks.first?.speaker == "Lucia", "0115: \(r115.blocks.map(\.text))")

// --- Kare 0022: üst sağ bildirim; arama bitti arayüzü yok
let f22 = lines([psToast] + hud + [(0.869, 0.258, 0.956, 0.284, 1.0, "Map updated"), (0.680, 0.745, 0.744, 0.831, 0.5, "Gianaás"),
    (0.823, 0.907, 0.859, 0.930, 1.0, "Lucia"), (0.823, 0.940, 0.891, 0.964, 1.0, "Call Ended")])
let r22 = ScreenText.analyze(f22)
expect(r22.blocks.count == 1 && r22.blocks[0].text == "Map updated" && r22.blocks[0].kind == .notify && r22.blocks[0].slot == "notify-top", "0022: \(r22.blocks.map(\.text))")

// --- 0039 ve 0107 üst sağ bildirimler, 0023 gönderensiz alt sağ bildirim
expect(ScreenText.analyze(lines([(0.907, 0.384, 0.962, 0.412, 1.0, "Fuel low")])).blocks.map(\.text) == ["Fuel low"], "0039")
expect(ScreenText.analyze(lines([(0.791, 0.385, 0.984, 0.413, 1.0, "'70 Ganado has been repaired")])).blocks.map(\.text) == ["'70 Ganado has been repaired"], "0107")
let r23 = ScreenText.analyze(lines([(0.826, 0.904, 0.958, 0.931, 1.0, "Workout Incomplete"), (0.825, 0.936, 0.891, 0.963, 1.0, "Gains lost")]))
expect(r23.blocks.first?.text == "Workout Incomplete Gains lost" && r23.blocks.first?.speaker == nil, "0023: \(r23.blocks.map(\.text))")

// --- Kare 0116 / 0124: görev hedefi (tabelalar ve BÜYÜK HARF dışarıda)
let r116 = ScreenText.analyze(lines([psToast, (0.077, 0.005, 0.244, 0.101, 1.0, "FLEECA"), (0.419, 0.106, 0.446, 0.119, 1.0, "\"NOTICE"),
    (0.375, 0.946, 0.625, 0.977, 1.0, "Ask about setting up a bank account")]))
expect(r116.blocks.count == 1 && r116.blocks[0].kind == .objective && r116.blocks[0].text == "Ask about setting up a bank account", "0116: \(r116.blocks.map(\.text))")
let r124 = ScreenText.analyze(lines([(0.424, 0.945, 0.576, 0.982, 1.0, "Speak to the manager"), (0.872, 0.035, 1.001, 0.107, 1.0, "NOTICE"),
    (0.858, 0.223, 0.999, 0.268, 1.0, "ALL ACTIVITIES"), (0.870, 0.262, 0.984, 0.295, 1.0, "MONITORED")]))
expect(r124.blocks.map(\.text) == ["Speak to the manager"], "0124: \(r124.blocks.map(\.text))")

// --- Yalnız tabela / reklam panosu kareleri: hiçbir şey çevrilmez
let f119 = lines([psToast, (0.109, 0.111, 0.156, 0.127, 1.0, "Sometimes"), (0.109, 0.126, 0.166, 0.142, 1.0, "landlords just"),
    (0.112, 0.142, 0.174, 0.158, 1.0, "can't get ahead."), (0.521, 0.208, 0.577, 0.259, 1.0, "'NOTICE"), (0.521, 0.322, 0.573, 0.373, 1.0, "ALL ACTIVITIES"),
    (0.523, 0.344, 0.567, 0.390, 1.0, "MONITORED"), (0.527, 0.366, 0.564, 0.410, 1.0, "BY VIDEO"), (0.520, 0.390, 0.558, 0.424, 1.0, "• CAMERA")])
expect(ScreenText.analyze(f119).blocks.isEmpty, "0119 boş: \(ScreenText.analyze(f119).blocks.map(\.text))")
let f120 = lines([psToast, (0.467, 0.075, 0.645, 0.173, 1.0, "FLEEA"), (0.496, 0.184, 0.606, 0.229, 1.0, "BANK"), (0.711, 0.342, 0.790, 0.374, 1.0, "ALL ACTIVITIES")])
expect(ScreenText.analyze(f120).blocks.isEmpty, "0120 boş")
// 0111: sol üstte pano (x0 0.097 > 0.08), sağda küçük pano (ince, 3 satır), kenara değen yazı
let f111 = lines([psToast, (0.097, 0.190, 0.186, 0.221, 1.0, "Why wait years"), (0.099, 0.219, 0.147, 0.248, 1.0, "for your"), (0.102, 0.245, 0.174, 0.272, 1.0, "inheritance?"),
    (0.808, 0.457, 0.846, 0.473, 1.0, "Sometimes"), (0.808, 0.470, 0.852, 0.483, 1.0, "landlords just"), (0.807, 0.483, 0.856, 0.496, 1.0, "can't get ahead."),
    (0.946, 0.625, 0.999, 0.643, 1.0, "Speak to an ach"), (0.214, 0.568, 0.250, 0.592, 0.5, "FLE CA")])
expect(ScreenText.analyze(f111).blocks.isEmpty, "0111 boş: \(ScreenText.analyze(f111).blocks.map(\.text))")

// Küçük harfle başlayan parça (kesik pano, kare 0215)
expect(ScreenText.analyze(lines([(0.030, 0.300, 0.150, 0.326, 1.0, "can't get ahead.")])).blocks.isEmpty, "küçük harfle başlayan parça elenir")

// --- Yalnız PS bildirimi, GPS, para, saat, arama arayüzü: asla
let junk = lines([psToast, (0.40, 0.047, 0.60, 0.075, 1.0, "Remote Play connected"), (0.03, 0.35, 0.12, 0.38, 1.0, "Turn Left"), (0.03, 0.39, 0.08, 0.41, 1.0, "9 ft"),
    (0.03, 0.42, 0.10, 0.44, 1.0, "0.56 mi"), (0.417, 0.189, 0.467, 0.217, 1.0, "1:19 pm"),
    (0.85, 0.86, 0.95, 0.88, 1.0, "Lucia"), (0.85, 0.89, 0.97, 0.91, 1.0, "Incoming Call..."), (0.88, 0.92, 0.94, 0.94, 1.0, "00:03")] + hud.map { $0 })
expect(ScreenText.analyze(junk).blocks.isEmpty, "gürültü boş: \(ScreenText.analyze(junk).blocks.map(\.text))")
// Oyun modunda haritadaki türden serbest cümle üretilmez
expect(ScreenText.analyze(lines([(0.547, 0.483, 0.797, 0.509, 1.0, "Lucia needs a bank account to get her"), (0.547, 0.519, 0.801, 0.545, 1.0, "community service payments.")])).blocks.isEmpty, "game modunda serbest metin yok")
// Düşük güven
expect(ScreenText.analyze(lines([(0.375, 0.946, 0.625, 0.977, 0.3, "Ask about setting up a bank account")])).blocks.isEmpty, "düşük güven elenir")
// Kenara değen bildirim
expect(ScreenText.analyze(lines([(0.791, 0.385, 0.999, 0.413, 1.0, "'70 Ganado has been repaired")])).blocks.isEmpty, "kenara değen elenir")

// --- Duraklatma menüsü ayar açıklaması (SENTETİK: gerçek karede ayar menüsü yoktu; yerleşim gerçek haritadaki açıklama panelinden)
let menuF = lines([psToast, (0.060, 0.180, 0.140, 0.208, 1.0, "AIMING"), (0.060, 0.250, 0.180, 0.278, 1.0, "Camera Sensitivity"),
    (0.520, 0.300, 0.700, 0.330, 1.0, "CAMERA SENSITIVITY"),
    (0.520, 0.345, 0.830, 0.372, 1.0, "Adjusts how fast the camera turns while"), (0.520, 0.380, 0.780, 0.407, 1.0, "aiming a weapon on foot."),
    (0.863, 0.951, 0.903, 0.974, 1.0, "SELECT"), (0.930, 0.951, 0.959, 0.974, 1.0, "BACK")])
let rm = ScreenText.analyze(menuF)
expect(rm.mode == .menu, "menü modu: \(rm.mode)")
expect(rm.blocks.count == 1 && rm.blocks[0].text == "Adjusts how fast the camera turns while aiming a weapon on foot." && rm.blocks[0].title == "CAMERA SENSITIVITY", "menü açıklaması: \(rm.blocks.map(\.text))")
// Gerçek menü kareleri (0183 sol menü listesi): çevrilecek şey yok
let r183 = ScreenText.analyze(lines([(0.910, 0.126, 0.981, 0.147, 1.0, "CHAPTER 1"), (0.026, 0.287, 0.086, 0.310, 1.0, "Im) Game"), (0.032, 0.352, 0.082, 0.375, 1.0, "• Store"),
    (0.045, 0.421, 0.076, 0.447, 1.0, "Help"), (0.025, 0.486, 0.129, 0.509, 1.0, "[ Photo Studio"), (0.023, 0.550, 0.100, 0.581, 1.0, "ii Settings"),
    (0.760, 0.951, 0.836, 0.974, 1.0, "PHOTO STUDIO"), (0.863, 0.951, 0.903, 0.974, 1.0, "SELECT"), (0.930, 0.951, 0.959, 0.974, 1.0, "BACK")]))
expect(r183.mode == .menu && r183.blocks.isEmpty, "0183: \(r183.mode) \(r183.blocks.map(\.text))")

// --- Düğme simgesi normalleşmesi
let r_btn = ScreenText.analyze(lines([(0.375, 0.946, 0.625, 0.977, 1.0, "Press LI to reply now")]))
expect(r_btn.blocks.first?.text == "Press L1 to reply now", "glif: \(r_btn.blocks.map(\.text))")

// --- İzleyici
func blk(_ kind: ScreenKind, _ text: String, slot: String? = nil) -> ScreenBlock {
    ScreenBlock(kind: kind, text: text, title: nil, speaker: nil, box: .zero, lineHeight: 0.02, slot: slot ?? kind.rawValue)
}
var tr = ScreenTracker()
let ob = blk(.objective, "Ask about setting up a bank account")
expect(tr.feed([ob], mode: .game, at: 0).isEmpty, "ilk geçiş çevrilmez")
expect(tr.feed([ob], mode: .game, at: 1) == [.translate(ob)], "ikinci geçişte çevrilir")
expect(tr.feed([ob], mode: .game, at: 2).isEmpty, "aynı metin yeniden gönderilmez")
expect(tr.feed([blk(.objective, "Ask about setting up a bank acount")], mode: .game, at: 3).isEmpty, "benzer OCR değişimi yeniden göndermez")
expect(tr.feed([], mode: .game, at: 4).isEmpty, "kısa kayboluş temizlemez")
expect(tr.feed([ob], mode: .game, at: 5).isEmpty, "geri gelince (temizlenmedi) tekrar göndermez")
expect(tr.feed([], mode: .game, at: 6).isEmpty, "kayboluş başladı")
expect(tr.feed([], mode: .game, at: 8.1) == [.clear(slot: "objective", kind: .objective)], "2 sn sonra temizler")
expect(tr.feed([ob], mode: .game, at: 9).isEmpty && tr.feed([ob], mode: .game, at: 10) == [.translate(ob)], "temizlenen geri gelince yeniden çevrilir")
// farklı metin: yeniden çevir
let ob2 = blk(.objective, "Speak to the manager")
expect(tr.feed([ob2], mode: .game, at: 11).isEmpty && tr.feed([ob2], mode: .game, at: 12) == [.translate(ob2)], "değişen metin 2 geçişte çevrilir")
// tek geçişlik titreme çevrilmez
var tr2 = ScreenTracker()
expect(tr2.feed([blk(.info, "Some flicker text here")], mode: .game, at: 0).isEmpty && tr2.feed([], mode: .game, at: 1).isEmpty && tr2.feed([blk(.info, "Other thing entirely different")], mode: .game, at: 2).isEmpty, "titreme çevrilmez")
// mod değişimi: description hemen temizlenir
var tr3 = ScreenTracker()
let ds = blk(.description, "Fill up on fuel or quickly service your vehicle.")
_ = tr3.feed([ds], mode: .map, at: 0)
expect(tr3.feed([ds], mode: .map, at: 1) == [.translate(ds)], "açıklama çevrilir")
expect(tr3.feed([], mode: .game, at: 2) == [.clear(slot: "description", kind: .description)], "mod değişince açıklama hemen temizlenir")
// aynı türde iki yuva bağımsız
var tr4 = ScreenTracker()
let nb = blk(.notify, "Map updated", slot: "notify-top"), nm = blk(.notify, "Hey I'm at the bank.", slot: "notify-bottom")
_ = tr4.feed([nb, nm], mode: .game, at: 0)
expect(tr4.feed([nb, nm], mode: .game, at: 1).count == 2, "iki yuva birlikte çevrilir")
// kayarak gelen bildirim: önce yarım metin çevrilir, tamamlanınca güncellenir; sonra tekrar gönderilmez
var tr5 = ScreenTracker()
let p1 = blk(.notify, "Hey I'm at the bank. Let", slot: "notify-bottom")
let p2 = blk(.notify, "Hey I'm at the bank. Let's set up this account.", slot: "notify-bottom")
_ = tr5.feed([p1], mode: .game, at: 0)
expect(tr5.feed([p1], mode: .game, at: 1) == [.translate(p1)], "yarım bildirim çevrilir")
// tam metin yarımdan yeterince farklıysa önce 2 geçiş kararlı olmalı; en geç ikinci geçişte gönderilir
let a2 = tr5.feed([p2], mode: .game, at: 2), a3 = tr5.feed([p2], mode: .game, at: 3)
expect((a2 + a3) == [.translate(p2)], "tamamlanan bildirim bir kez güncellenir: \(a2) \(a3)")
expect(tr5.feed([p2], mode: .game, at: 4) == [], "tam metin tekrar gönderilmez")

print("\(total - failures)/\(total) test geçti")
exit(failures == 0 ? 0 : 1)
}
}
