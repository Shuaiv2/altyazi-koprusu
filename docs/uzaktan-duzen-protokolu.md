# Mac'ten uzaktan altyazı düzeni — protokol (Altyazı Köprüsü)

Ayarların sahibi TV'dir: kalıcı kayıt TV'de `localStorage['gametranslator.subStyle']` içinde tutulur (biçim: `tv/webos/subtitle-settings.js`, sürüm 1). Mac yalnızca önizleme gönderir ve kaydetme ister.

## Ayar nesnesi (`settings`)
`subtitle-settings.js` modelinin aynısıdır. Uzunluklar sayfa pikseli cinsindendir (genişlik 1920, yükseklik yaklaşık 1080):
`{x, y, width, fontSize, margin, textColor, bgColor, bgOpacity, outline, align, bold, positionSource, version: 1}`.

Her iki taraf da gelen nesneyi `sanitize` eder.

## Mac → TV: SSE olayı `layout`
```
event: layout
data: {"mode":"preview"|"save"|"cancel", "settings":{...}, "rev": <artan tamsayı>}
```
- **`preview`**
  - TV, `settings`'i geçici taslak olarak uygular ve örnek Türkçe satırı aynı yerleşim fonksiyonuyla gösterir.
  - Ekranın köşesinde küçük bir "Mac'ten düzenleniyor" etiketi görünür.
  - Kaydedilmiş ayara dokunulmaz.
  - 60 saniye yeni bir `layout` olayı gelmezse önizleme kendiliğinden iptal edilir.
  - TV'de kendi ayar ekranı açıksa önizleme yok sayılır; TV'deki düzenleme önceliklidir.
- **`save`**
  - `settings` sanitize edilir, `positionSource` `'tv'` yapılır ve kalıcı olarak kaydedilir.
  - Önizleme kapanır. Örnek satır 3 saniye gösterilir, sonra gizlenir.
  - TV, kaydettiği ayarı Mac'e geri bildirir (aşağıdaki POST).
- **`cancel`:** Önizleme kapanır, kayıtlı ayar geri gelir.
- **`rev`:** TV, gördüğü en büyük `rev` değerinden küçük olan olayları atar. Bu sayaç bağlantı her yeniden kurulduğunda sıfırlanır.

## TV → Mac: `POST /tv-layout`
- **Gövde:** `{"settings":{...}, "source":"tv", "app":"1.2.0"}`.
- **Content-Type:** `text/plain`. Böylece WebView'de CORS ön isteği (preflight) oluşmaz.
- **Ne zaman gönderilir:**
  - SSE bağlantısı açıldığında.
  - TV'de ayar kaydedildiğinde.
  - Mac'ten gelen bir `save` uygulandığında.
- **Yanıt:** `200 {"status":"ok"}`. Mac, CORS için `Access-Control-Allow-Origin: *` gönderir.

`GET /tv-layout` son bildirilen ayarı döndürür; hiç bildirim gelmediyse `{}` döner. Mac bu ayarı bellekte tutar ve `UserDefaults` içinde `gt_tv_layout` anahtarıyla saklar.

## Mac düzenleyici sayfası ↔ Swift köprüsü
- **Sayfa:** `Contents/Resources/tv-web/editor/editor.html`.
  - `WKWebView.loadFileURL` ile ve `tv-web` klasörüne okuma izniyle açılır.
  - Sayfanın gereksindiği dosyalar `tv/editor/FILES.txt` içinde listelenir (yollar `tv/` köküne göre).
  - Derleme betiği `tv/editor/*` ve bu listedeki dosyaları `tv-web/` altına, aynı göreli yollarla kopyalar.
- **JS → Swift:** `window.webkit.messageHandlers.altyazi.postMessage({type, settings})`.
  - `type` şunlardan biridir: `ready`, `preview`, `save`, `cancel`.
  - `preview` iletileri sayfa tarafında en fazla saniyede 10 kez gönderilir.
- **Swift → JS:**
  - `window.__editorLoad(settingsOrNull, tvConnected)`: TV'nin son bildirdiği ayarı yükler.
  - `window.__editorStatus(tvConnected, lastReportIso)`: bağlantı durumunu günceller.

## Ekran metinleri (screen) — Mac 1.6.0

Mac, altyazıdan bağımsız olarak oyun ekranındaki metinleri ~1 sn'de bir **tam kare** OCR ile bulur, türüne göre sınıflandırır ve TV'ye SSE ile gönderir. Her tür kendi kimliğiyle (`region`) yönetilir; TV yalnızca ilgili türün öğelerini değiştirir ya da temizler. Oyun modunda serbest metin (tabela, reklam panosu, GPS, para, saat, PS bildirimi) hiçbir zaman gönderilmez.

| `region` | Ne | Ekrandaki yer | `placement` |
| --- | --- | --- | --- |
| `objective` | görev hedefi (tek satır) | alt orta şerit (y > 0,93) | `over` |
| `info` | öğretici/bilgi kutusu | sol üst (x < 0,08, y 0,18–0,55) | `below` |
| `notify` | telefon mesajı ya da kısa bildirim | sağ alt (mesaj) / sağ üst (ör. "Map updated") | `left` |
| `description` | harita/menü açıklaması (yalnızca harita ve duraklatma menüsünde) | panel (örn. x 0,55–0,80) | `over` |

`placement` değerleri: `below` = çeviri İngilizce kutunun altına; `left` = kutunun soluna; `over` = TV Türkçeyi İngilizce kutunun **üzerine** (onu örterek) çizer.

**Mac → TV: SSE olayı `screen`**

```json
{"region":"notify","duration":15000,"items":[
  {"text":"Merhaba, bankadayım. Hesabı açalım.","source":"Hey I'm at the bank. Let's set up this account.",
   "speaker":"Jason",
   "box":{"left":0.80,"top":0.81,"right":0.965,"bottom":0.907},
   "line":0.026,"placement":"left"}
]}
```

- `region`: `"objective"`, `"info"`, `"notify"` ya da `"description"`. Alan yoksa `"info"` kabul edilir. (Eski `"phone"` artık gönderilmez; TV eski değeri `notify` gibi işleyebilir.)
- `items`: türün o anki tüm öğeleri; TV, o türün önceki öğelerini bu listeyle **değiştirir**, diğer türlere dokunmaz.
- `box`: tüm karede normalize (0..1), başlangıç sol-üst; İngilizce bloğun (description için başlık dahil) kutusu. `line`: normalize satır yüksekliği. `duration`: ms cinsinden en fazla görünme süresi.
- `speaker` (isteğe bağlı, yalnızca `notify` mesajı): gönderen adı; çevrilmez, `text` yalnızca mesajı içerir. TV isterse etiket olarak gösterir.
- `description` öğesinde `text`, `"BAŞLIK: gövde"` biçimindedir (başlık ve gövde ayrı çevrilir; başlık büyük harf). `source` da `"TITLE: body"`.
- Bir blok, 2 ardışık tam kare geçişinde (≥ 0,85 metin benzerliği) görülünce gönderilir; ekranda kaldığı sürece aynı metin yeniden gönderilmez.

**Mac → TV: SSE olayı `screen-clear`**

```json
{"region":"notify"}
```

Yalnızca belirtilen türün öğelerini siler. Gövde boş ya da `region` yoksa `"info"` kabul edilir (geriye dönük uyum). Mac şu durumlarda gönderir: tür kesintisiz 2 sn kaybolunca; mod değişince (ör. harita kapanınca `description` hemen temizlenir; haritaya girilince oyun türleri temizlenir); çeviri durdurulunca (4 türün hepsi).
