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
expect(tr3.feed([], mode: .game, at: 2).isEmpty, "mod 1 geçişte değişmez (titreme)")
expect(tr3.feed([], mode: .game, at: 3) == [.clear(slot: "description", kind: .description)], "mod 2 ardışık geçişte değişince açıklama temizlenir")
// game<->menu titremesi: tek geçişlik menü araya girerse görev hedefi silinmez
var tr6 = ScreenTracker()
let ob6 = blk(.objective, "Get in your car")
_ = tr6.feed([ob6], mode: .game, at: 0); _ = tr6.feed([ob6], mode: .game, at: 1)
expect(tr6.feed([ob6], mode: .menu, at: 2).isEmpty && tr6.feed([ob6], mode: .game, at: 3).isEmpty, "tek geçişlik menü araya girince hedef kalır")
expect(tr6.feed([], mode: .menu, at: 4).isEmpty && tr6.feed([], mode: .menu, at: 5) == [.clear(slot: "objective", kind: .objective)], "menü 2 geçiş sürerse hedef temizlenir")
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

// ===== 1.7.0 =====
// --- Telefon mesaj listesi (kare 0237 ve eğik tutulan 0243): mod phone, hiçbir şey çevrilmez
let f237 = lines([psToast, (0.436, 0.201, 0.493, 0.223, 1.0, "Suggested"), (0.451, 0.244, 0.482, 0.267, 1.0, "Lucia"), (0.450, 0.268, 0.573, 0.303, 1.0, "Hey! Get this, Kat and..."),
    (0.445, 0.319, 0.483, 0.343, 1.0, "Donnie"), (0.444, 0.342, 0.566, 0.381, 1.0, "SBW off Sunbelt High..."), (0.437, 0.394, 0.479, 0.423, 1.0, "Kenton"),
    (0.437, 0.417, 0.558, 0.459, 1.0, "Still can't believe u fo..."), (0.393, 0.455, 0.447, 0.488, 1.0, "Messages"), (0.429, 0.509, 0.458, 0.530, 1.0, "Brian"),
    (0.426, 0.531, 0.549, 0.578, 1.0, "Remember, you get y..."), (0.422, 0.587, 0.442, 0.607, 1.0, "Cal"), (0.419, 0.606, 0.541, 0.655, 1.0, "And 2 here. Have fun..."),
    (0.920, 0.902, 0.959, 0.925, 1.0, "SELECT"), (0.930, 0.951, 0.961, 0.972, 1.0, "BACK")])
let r237 = ScreenText.analyze(f237)
expect(r237.mode == .phone && r237.blocks.isEmpty && r237.phone?.isList == true, "0237 telefon listesi: \(r237.mode) \(r237.blocks.map(\.text))")
expect(r237.phone.map { $0.box.minX < 0.42 && $0.box.maxX > 0.55 && $0.box.minY < 0.21 && $0.box.maxY > 0.6 } == true, "0237 telefon kutusu: \(String(describing: r237.phone?.box))")
let f243 = lines([psToast, (0.522, 0.578, 0.568, 0.597, 1.0, "€ Suggested"), (0.541, 0.607, 0.565, 0.620, 1.0, "Donnie"), (0.539, 0.617, 0.611, 0.642, 0.5, "SBW off Sunbelt High."),
    (0.535, 0.649, 0.561, 0.661, 0.5, "Kenton"), (0.533, 0.660, 0.604, 0.684, 1.0, "Still can't believe u fo"), (0.505, 0.678, 0.540, 0.701, 1.0, "Messages"),
    (0.526, 0.711, 0.545, 0.724, 1.0, "Lucia"), (0.524, 0.722, 0.601, 0.750, 1.0, "You wanna go we can ."), (0.519, 0.752, 0.539, 0.767, 1.0, "Brian"),
    (0.517, 0.764, 0.595, 0.797, 1.0, "Remember, you get y..."), (0.513, 0.798, 0.526, 0.811, 1.0, "Cal"), (0.509, 0.811, 0.585, 0.840, 1.0, "And 2 here. Have fun."),
    (0.505, 0.839, 0.572, 0.873, 1.0, "Chuck's Gun Shop"), (0.504, 0.857, 0.559, 0.884, 1.0, "Thanks Andrew."), (0.498, 0.887, 0.530, 0.911, 0.5, "BuckMe"),
    (0.496, 0.902, 0.575, 0.940, 1.0, "Welcome to BuckMe,"), (0.920, 0.901, 0.961, 0.926, 1.0, "SELECT"), (0.930, 0.948, 0.978, 0.974, 1.0, "BACK O")])
let r243 = ScreenText.analyze(f243)
expect(r243.mode == .phone && r243.blocks.isEmpty && r243.phone?.isList == true, "0243 telefon listesi: \(r243.mode) \(r243.blocks.map(\.text))")
// Aynı SELECT/BACK ile gerçek menü (0183) hâlâ menü
expect(r183.mode == .menu && r183.phone == nil, "0183 hâlâ menü")

// --- Açık konuşma: önce sentetik düzen, ardından gerçek kare 0238
// (balonlar .phone bloğu)
let convo = lines([(0.60, 0.20, 0.72, 0.23, 1.0, "Hey, you around"), (0.60, 0.23, 0.70, 0.26, 1.0, "later today?"),
    (0.66, 0.34, 0.78, 0.37, 1.0, "Yeah, I can be there"), (0.66, 0.37, 0.76, 0.40, 1.0, "in ten minutes."),
    (0.60, 0.48, 0.72, 0.51, 1.0, "Great, bring the car."), (0.66, 0.60, 0.78, 0.63, 1.0, "On my way now"), (0.930, 0.951, 0.961, 0.972, 1.0, "BACK")])
let rc = ScreenText.analyze(convo)
expect(rc.mode == .phone && rc.phone?.isList == false, "konuşma modu: \(rc.mode) \(String(describing: rc.phone))")
expect(rc.blocks.count == 1 && rc.blocks[0].kind == .phone && rc.blocks[0].slot == "phone", "konuşma tek blok: \(rc.blocks.map(\.slot))")
expect(rc.blocks.first?.messages.map(\.text) == ["Hey, you around later today?", "Yeah, I can be there in ten minutes.", "Great, bring the car.", "On my way now"], "konuşma balonları: \(rc.blocks.first?.messages.map(\.text) ?? [])")

let f238 = lines([psToast, (0.487, 0.199, 0.517, 0.220, 1.0, "Lucia"), (0.424, 0.271, 0.491, 0.295, 1.0, "You got this!"), (0.451, 0.378, 0.527, 0.411, 1.0, "New message"),
    (0.407, 0.430, 0.526, 0.472, 1.0, "Hey! Get this, Kat and"), (0.406, 0.458, 0.552, 0.505, 1.0, "Mattia are throwing a party"), (0.403, 0.483, 0.545, 0.529, 1.0, "on their YACHT!?! Said we"),
    (0.402, 0.510, 0.546, 0.562, 1.0, "could meet up at the Rusty"), (0.399, 0.542, 0.527, 0.587, 1.0, "and go from there. That"), (0.397, 0.574, 0.426, 0.592, 1.0, "cool?"),
    (0.434, 0.634, 0.539, 0.677, 1.0, "Yeah, sure. I wanna"), (0.429, 0.665, 0.516, 0.707, 1.0, "see this dinghy."), (0.420, 0.719, 0.515, 0.763, 1.0, "You wanna go we"), (0.417, 0.752, 0.458, 0.778, 1.0, "can go."),
    (0.919, 0.853, 0.959, 0.876, 1.0, "SCROLL"), (0.930, 0.901, 0.959, 0.926, 1.0, "SEND"), (0.930, 0.951, 0.961, 0.972, 1.0, "BACK")])
let r238 = ScreenText.analyze(f238)
let m238 = r238.blocks.first?.messages ?? []
expect(r238.mode == .phone && r238.phone?.isList == false && r238.blocks.count == 1 && r238.blocks[0].kind == .phone && m238.count == 3, "0238 konuşma: \(m238.map(\.text))")
expect(m238.first?.text == "You got this!" && r238.blocks.first?.speaker == "Lucia" && m238[1].text.hasPrefix("Hey! Get this, Kat and Mattia are throwing a party") && m238[1].text.hasSuffix("That cool?"), "0238 metinler/gönderen: \(m238.map(\.text))")

// --- 1.7.3 sohbet paneli: canlı GTA 6 geometrisi (kutu ≈ 0.450,0.122 0.247x0.613); sentetik satırlar
// Linda'nın iki mesajı solda, oyuncunun yanıtları sağa yaslı.
let liveRows: [Row] = [(0.490, 0.140, 0.540, 0.163, 1.0, "Linda"),
    (0.466, 0.200, 0.560, 0.224, 1.0, "As long as you pay"), (0.466, 0.224, 0.540, 0.248, 1.0, "your rent. L."),
    (0.466, 0.300, 0.570, 0.324, 1.0, "See me ASAP to"), (0.466, 0.324, 0.560, 0.348, 1.0, "discuss. L."),
    (0.545, 0.410, 0.676, 0.434, 1.0, "Okay. Thanks."),
    (0.466, 0.490, 0.570, 0.514, 1.0, "Rent. Remember it."), (0.466, 0.514, 0.570, 0.538, 1.0, "Have a nice day. L."),
    (0.540, 0.610, 0.676, 0.634, 1.0, "Yeah I haven't"), (0.540, 0.634, 0.620, 0.658, 1.0, "forgotten."), (0.930, 0.951, 0.961, 0.972, 1.0, "BACK")]
let rl = ScreenText.analyze(lines(liveRows))
expect(rl.mode == .phone && rl.phone?.isList == false && rl.blocks.count == 1 && rl.blocks[0].slot == "phone", "canlı: tek blok \(rl.blocks.map(\.slot)) \(rl.mode)")
let ml = rl.blocks.first?.messages ?? []
expect(ml.map(\.text) == ["As long as you pay your rent. L.", "See me ASAP to discuss. L.", "Okay. Thanks.", "Rent. Remember it. Have a nice day. L.", "Yeah I haven't forgotten."], "canlı metinler: \(ml.map(\.text))")
expect(ml.map(\.fromMe) == [false, false, true, false, true], "canlı taraflar: \(ml.map(\.fromMe))")
expect(rl.blocks.first?.speaker == "Linda", "canlı kişi adı: \(String(describing: rl.blocks.first?.speaker))")
expect(rl.blocks.first?.text == ml.map(\.text).joined(separator: "\n"), "canlı blok metni")
// --- 1.7.4: balon rengi (mavi = oyuncu). Kare 0238'de son seçenek balonunun merkezi telefon merkezinin SOLUNDA: konum kuralı yanılır.
let r238c = ScreenText.analyze(f238, bubbleBlue: { $0.minY >= 0.62 })
let m238c = r238c.blocks.first?.messages ?? []
expect(m238c.map(\.fromMe) == [false, false, true, true] && m238c.count == 4, "0238 renkli: \(m238c.map { "\($0.fromMe):\($0.text)" })")
expect(m238c.map(\.text) == ["You got this!", "Hey! Get this, Kat and Mattia are throwing a party on their YACHT!?! Said we could meet up at the Rusty and go from there. That cool?", "Yeah, sure. I wanna see this dinghy.", "You wanna go we can go."], "0238 renkli metinler: \(m238c.map(\.text))")
expect(r238c.blocks.first?.speaker == "Lucia" && !m238c.contains { $0.text.lowercased().contains("new message") || $0.text == "Lucia" }, "başlık adı ve 'New message' mesaj olmaz")
// İpucu yok / closure nil döndürür -> eski konum kuralı (üç mesaj, son birleşik balon konumla karşı tarafa düşer)
let m238n = ScreenText.analyze(f238, bubbleBlue: { _ in nil }).blocks.first?.messages ?? []
expect(m238n == m238, "ipucu yoksa eski davranış: \(m238n.map(\.text))")
expect(ScreenText.analyze(lines(liveRows), bubbleBlue: { _ in nil }).blocks.first?.messages.map(\.fromMe) == [false, false, true, false, true], "ipucu nil: konum kuralı")
// ScreenLine.blueBubble alanı varsayılan nil
expect(ScreenLine(text: "x", rect: .zero).blueBubble == nil, "blueBubble varsayılan nil")

// Taraf kuralı birimi
let pbox = CGRect(x: 0.450, y: 0.122, width: 0.247, height: 0.613)
expect(!ScreenText.isMine(CGRect(x: 0.47, y: 0.3, width: 0.10, height: 0.05), in: pbox), "sol balon them")
expect(ScreenText.isMine(CGRect(x: 0.58, y: 0.3, width: 0.09, height: 0.05), in: pbox), "sağa yaslı balon me")
expect(!ScreenText.isMine(CGRect(x: 0.50, y: 0.3, width: 0.10, height: 0.05), in: pbox), "ortada/soldan balon them")

// --- Tek yuva izleyici: yeni mesaj gelince yeniden gönderilir, OCR titremesinde gönderilmez
func phoneBlock(_ msgs: [(Bool, String)]) -> ScreenBlock {
    let m = msgs.map { PhoneMessage(fromMe: $0.0, text: $0.1) }
    return ScreenBlock(kind: .phone, text: m.map(\.text).joined(separator: "\n"), title: nil, speaker: "Linda", box: pbox, lineHeight: 0.024, slot: "phone", messages: m)
}
var trp = ScreenTracker()
let base: [(Bool, String)] = [(false, "As long as you pay your rent. L."), (false, "See me ASAP to discuss. L."), (true, "Okay. Thanks.")]
let pb1 = phoneBlock(base)
expect(trp.feed([pb1], mode: .phone, at: 0) == [], "telefon: ilk geçiş beklenir")
let pa = trp.feed([pb1], mode: .phone, at: 1)
expect(pa == [.translate(pb1)], "telefon: ilk gönderim \(pa)")
expect(trp.feed([pb1], mode: .phone, at: 3) == [], "telefon: aynı sohbet tekrar gönderilmez")
var jitter = base; jitter[1].1 = "See me ASAP to discuss. L"
expect(trp.feed([phoneBlock(jitter)], mode: .phone, at: 4) == [] && trp.feed([phoneBlock(jitter)], mode: .phone, at: 5) == [], "telefon: OCR titremesi gönderilmez")
let pb2 = phoneBlock(base + [(false, "Rent. Remember it. Have a nice day. L.")])
let pa2 = trp.feed([pb2], mode: .phone, at: 6) + trp.feed([pb2], mode: .phone, at: 7)
expect(pa2 == [.translate(pb2)], "telefon: yeni mesaj bir kez gönderilir \(pa2.count)")
let pb3 = phoneBlock(base + [(false, "Rent. Remember it. Have a nice day. L."), (true, "Ok.")])
let pa3 = trp.feed([pb3], mode: .phone, at: 8) + trp.feed([pb3], mode: .phone, at: 9)
expect(pa3 == [.translate(pb3)], "telefon: çok kısa yeni mesaj da gönderilir \(pa3.count)")
expect(trp.feed([], mode: .phone, at: 20) == [] && trp.feed([], mode: .phone, at: 22.5) == [.clear(slot: "phone", kind: .phone)], "telefon: kapanınca temizlenir")

// --- Salt ad/etiket çevrilmez (kare 0092); hedefler geçer
expect(ScreenText.analyze(lines([psToast, (0.033, 0.183, 0.164, 0.215, 1.0, "Key Lento Coffee Morning")])).blocks.isEmpty, "0092 ad/etiket çevrilmez")
expect(ScreenText.analyze(lines([(0.400, 0.946, 0.600, 0.977, 1.0, "Get in your car")])).blocks.first?.kind == .objective, "hedef: Get in your car")
expect(ScreenText.analyze(lines([(0.350, 0.946, 0.650, 0.977, 1.0, "Take Lucia to the community center")])).blocks.first?.kind == .objective, "hedef: Take Lucia ...")
expect(InfoBox.isNameLabel("Key Lento Coffee Morning") && !InfoBox.isNameLabel("Get in your car") && !InfoBox.isNameLabel("Map updated") && !InfoBox.isNameLabel("Call Joey from your Contacts or check it."), "isNameLabel")
expect(InfoBox.stripLeadingIcon("$ No goods to sell") == "No goods to sell" && InfoBox.stripLeadingIcon("i Hello there") == "Hello there" && InfoBox.stripLeadingIcon("I think so") == "I think so" && InfoBox.stripLeadingIcon("Hello") == "Hello", "stripLeadingIcon")

// --- Harita açıklaması: simge atılır, başlık ve gövde ayrı parça (kare 0117, 0122)
let mapPrompts: [Row] = [(0.905, 0.757, 0.961, 0.781, 1.0, "RECENTER"), (0.891, 0.852, 0.961, 0.879, 1.0, "ADD MARKER"), (0.894, 0.951, 0.959, 0.974, 1.0, "CLOSE MENU")]
let r117 = ScreenText.analyze(lines([psToast, (0.576, 0.341, 0.693, 0.370, 1.0, "DONNIE'S TRAILER"), (0.551, 0.395, 0.676, 0.424, 1.0, "$ No goods to sell"),
    (0.547, 0.439, 0.762, 0.465, 1.0, "Donnie will pay for any narcotics,"), (0.544, 0.475, 0.792, 0.504, 1.0, "jewelry and marked bills dropped off"), (0.546, 0.511, 0.629, 0.538, 1.0, "at his trailer.")] + mapPrompts))
expect(r117.blocks.first?.text == "No goods to sell\nDonnie will pay for any narcotics, jewelry and marked bills dropped off at his trailer.", "0117 parçalar: \(r117.blocks.map(\.text))")
let r122 = ScreenText.analyze(lines([psToast, (0.576, 0.341, 0.718, 0.372, 1.0, "SAVE FOR THE FUTURE"), (0.567, 0.398, 0.769, 0.426, 1.0, "Family Savings will earn interest"),
    (0.547, 0.439, 0.770, 0.468, 1.0, "You are Buckin' Broke. Your family"), (0.546, 0.471, 0.766, 0.507, 1.0, "needs savings for a better future."), (0.547, 0.512, 0.767, 0.540, 1.0, "Contribute to your BuckMe Family"),
    (0.546, 0.541, 0.783, 0.578, 1.0, "Savings account and reach the first"), (0.546, 0.581, 0.640, 0.607, 1.0, "milestone.")] + mapPrompts))
expect(r122.blocks.first?.text.hasPrefix("Family Savings will earn interest\nYou are Buckin' Broke.") == true, "0122 parçalar: \(r122.blocks.map(\.text))")
for (sym, w) in [("$", "Hello"), ("€", "Hello"), ("©", "Hello"), ("®", "Hello"), ("i", "Hello")] { expect(InfoBox.stripLeadingIcon("\(sym) \(w) world") == "\(w) world", "simge \(sym)") }

// --- Altyazı denetimi (DialogueGate), kare 0084 (iki satır) ve sızıntılar
let f84: [ScreenLine] = lines([(0.020, 0.829, 0.061, 0.853, 1.0, "0.15 mi"), (0.243, 0.828, 0.757, 0.877, 1.0, "Don't wanna ask and then have to say no 'cause of some case"),
    (0.436, 0.884, 0.565, 0.915, 1.0, "officer bullshit."), (0.923, 0.894, 0.985, 0.931, 1.0, "THROW O"), (0.907, 0.948, 0.980, 0.977, 1.0, "PUT AWAY")])
let g84 = DialogueGate.evaluate(Array(f84[1...2]), exclude: [])
expect(g84.kept == [0, 1], "0084 iki satır korunur")
let leak = lines([(0.404, 0.763, 0.531, 0.820, 1.0, "Welcome to BuckMe, ..."), (0.790, 0.800, 0.980, 0.830, 1.0, "Hey! Ge are thro"), (0.412, 0.884, 0.592, 0.917, 1.0, "Yeah, well, you know..."),
    (0.470, 0.850, 0.530, 0.880, 1.0, "Hey! Get this, Kat and..."), (0.470, 0.850, 0.640, 0.880, 1.0, "Look at this mess")])
expect(DialogueGate.reason(for: leak[0], exclude: []) == .ellipsisPreview, "kesik önizleme (virgül + ...)")
expect(DialogueGate.reason(for: leak[1], exclude: [CGRect(x: 0.79, y: 0.80, width: 0.19, height: 0.17)]) == .insideExcluded, "bildirim kartı içi")
expect(DialogueGate.reason(for: leak[1], exclude: []) == .offCenter, "ortalanmamış")
expect(DialogueGate.reason(for: leak[2], exclude: []) == nil, "gerçek '...' diyalogu korunur")
expect(DialogueGate.reason(for: leak[3], exclude: []) == .ellipsisPreview, "dar '...' önizleme")
expect(DialogueGate.reason(for: leak[4], exclude: [CGRect(x: 0.40, y: 0.20, width: 0.2, height: 0.7)]) == .insideExcluded, "telefon alanı içi")
expect(DialogueGate.reason(for: ScreenLine(text: "A very long line that spans wide of the screen", rect: CGRect(x: 0.05, y: 0.85, width: 0.5, height: 0.03)), exclude: []) == nil, "geniş satır merkez dışı olsa da geçer")

// --- Çapraz doğrulama: kırpma çöp/eksik okuduğunda tam kare metni
let full84 = f84 + [ScreenLine(text: "A Uzaktan Oynama baglandi.", rect: CGRect(x: 0.411, y: 0.047, width: 0.178, height: 0.028), confidence: 0.5)]
let cc = DialogueGate.crossCheck(cropText: "Sh, Don't wanna ask and then have to say no cause of some case officer bullshit", screen: full84, exclude: [])
expect(cc?.text == "Don't wanna ask and then have to say no 'cause of some case officer bullshit.", "çapraz doğrulama: \(cc?.text ?? "nil")")
expect(cc.map { $0.rect.minY < 0.83 && $0.rect.maxY > 0.91 } == true, "çapraz doğrulama kutusu iki satırı kapsar")
expect(DialogueGate.crossCheck(cropText: "Completely unrelated words appear in the crop text now", screen: full84, exclude: []) == nil, "benzemeyen metin kullanılmaz")
expect(DialogueGate.crossCheck(cropText: "Don't wanna ask and then have to say no", screen: full84, exclude: [CGRect(x: 0, y: 0.7, width: 1, height: 0.25)]) == nil, "dışlanan alandaki tam kare satırı kullanılmaz")

// --- İzleyici: sondaki sözcük titremesi (1.7.1)
do {
    func nb(_ t: String) -> ScreenBlock { ScreenBlock(kind: .notify, text: t, title: nil, speaker: nil, box: .zero, lineHeight: 0.02, slot: "notify-bottom") }
    var tr = ScreenTracker()
    let full = nb("SBW off Sunbelt Highway cmon"), short = nb("SBW off Sunbelt Highway")
    var sends = 0
    func f(_ b: ScreenBlock, _ t: Double) { for a in tr.feed([b], mode: .game, at: t) { if case .translate = a { sends += 1 } } }
    f(short, 0); f(short, 1)
    expect(sends == 1, "ilk gönderim: \(sends)")
    f(full, 2); f(full, 3)      // 5 karakter uzadı: yeniden gönderilir
    expect(sends == 2, "uzayınca yeniden: \(sends)")
    for i in 0..<6 { f(i % 2 == 0 ? short : full, 4 + Double(i)) }
    expect(sends == 2, "titreme yeniden göndermemeli: \(sends)")
    var tr2 = ScreenTracker()
    var s2 = 0
    func g(_ b: ScreenBlock, _ t: Double) { for a in tr2.feed([b], mode: .game, at: t) { if case .translate = a { s2 += 1 } } }
    g(full, 0); g(full, 1); g(short, 2); g(short, 3)
    expect(s2 == 1, "sondaki sözcük düşünce yeniden gönderme yok: \(s2)")
    expect(tr2.currentMode == .game, "currentMode game")
    var tr3 = ScreenTracker()
    _ = tr3.feed([], mode: .menu, at: 0); _ = tr3.feed([], mode: .menu, at: 1)
    expect(tr3.currentMode == .menu, "currentMode menu onaylandı")
}

print("\(total - failures)/\(total) test geçti")
exit(failures == 0 ? 0 : 1)
}
}
