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

## Ekran bölgeleri (screen)

Mac, altyazıdan bağımsız olarak ekranın belirli bölgelerini (bilgi/öğretici kutusu, telefon mesajları) ayrı ayrı çevirir ve TV'ye SSE ile gönderir. Her bölge kendi kimliğiyle (`region`) yönetilir; TV yalnızca ilgili bölgenin öğelerini değiştirir ya da temizler.

| `region` | Ekrandaki yer | `placement` |
| --- | --- | --- |
| `info` | sol üst bilgi/öğretici kutusu | `below` (çeviri kutunun altına) |
| `phone` | sağ taraftaki telefon mesajları | `left` (çeviri balonun soluna) |

**Mac → TV: SSE olayı `screen`**

```json
{"region":"phone","duration":15000,"items":[
  {"text":"Türkçe çeviri","source":"English text",
   "box":{"left":0.70,"top":0.30,"right":0.90,"bottom":0.36},
   "line":0.025,"placement":"left"}
]}
```

- `region`: `"info"` ya da `"phone"`. Alan yoksa (eski Mac sürümleri) `"info"` kabul edilir.
- `items`: bölgenin o anki tüm öğeleri; TV, o bölgenin önceki öğelerini bu listeyle **değiştirir**, diğer bölgelere dokunmaz. Telefonda ayrı balonlar (dikey boşluk > ~1,2 satır yüksekliği) ayrı öğe olur; her öğenin kendi `box` değeri vardır.
- `box`: tüm karede normalize (0..1), başlangıç sol-üst. `line`: normalize satır yüksekliği. `duration`: ms cinsinden en fazla görünme süresi.

**Mac → TV: SSE olayı `screen-clear`**

```json
{"region":"phone"}
```

Yalnızca belirtilen bölgenin öğelerini siler. Gövde boş ya da `region` yoksa `"info"` kabul edilir (geriye dönük uyum). Mac, bölge metni 2 sn kesintisiz kaybolunca ve çeviri durdurulunca ilgili bölge için gönderir.
