# Ludora

Konsol oyunlarına canlı Türkçe altyazı. Ludora oyun ekranındaki İngilizce yazıları okur (OCR), Türkçeye çevirir ve çeviriyi oyunun üstüne yazar.

Çevrilenler: diyalog altyazıları, görev hedefleri, bilgi/öğretici kutuları, bildirim kartları, harita ve menü açıklamaları, oyun içi telefondaki mesajlaşmalar (telefonun yanında sohbet paneli olarak).

## Hangi kurulum bana uygun?

| Kurulum | Gerekenler | Çeviri nerede görünür |
|---|---|---|
| **Xbox + PC + Monitör** | Xbox Series S/X, Windows 10/11 64-bit PC | Oyun PC'de (xbox.com/play) tam ekran oynanır, çeviri üstüne yazılır |
| **Xbox + PC + Smart TV** | Yukarıdakiler + PC'den TV'ye HDMI kablo | PC görüntüsü TV'ye verilir, çeviri oyunun üstünde |
| **PS5 + Mac + Android TV** | PS5, ikinci PSN hesabı, Apple Silicon Mac (macOS 14+), HDMI görüntüsünü uygulamalara aktaran Android TV | PS5 doğrudan TV'ye bağlı kalır; TV uygulaması çeviriyi HDMI görüntüsünün üstüne çizer |

```
Xbox ──(ağ)──► PC: xbox.com/play + Ludora ──► Monitör ya da (HDMI) Smart TV
PS5 ──HDMI──► Android TV (Ludora TV uygulaması)
PS5 ──Remote Play (2. PSN hesabı)──► Mac: OCR + çeviri ──(yerel ağ)──► TV
```

## İndirme

[Sürümler](https://github.com/Shuaiv2/ludora/releases) sayfasından:

- **Windows (Xbox):** `Ludora-Windows-<sürüm>.zip` + `Ludora-Kurulum-Rehberi.pdf`
- **Mac (PS5):** `Ludora-Mac-<sürüm>.zip` (TV uygulaması Mac uygulamasının içinden kurulur)

## Hızlı başlangıç

### Windows + Xbox (monitör veya Smart TV)

1. Xbox'ta **Ayarlar › Cihazlar ve bağlantılar › Uzak özellikler › Uzak özellikleri etkinleştir**; güç modu **Uyku**.
2. Oyunun ayarlarından **altyazıları aç**.
3. PC'de Edge/Chrome ile **xbox.com/play** › aynı Microsoft hesabı › Konsollarım › **Uzaktan oynat**, ardından <kbd>F11</kbd>.
4. Zip'i ayıkla, **Ludora.exe**'yi çalıştır (imzasız uygulama uyarısında: Ek bilgi › Yine de çalıştır).
5. Sihirbazda **Xbox** › **Bu bilgisayarda (tam ekran oyunun üstünde)** › Xbox penceresini seç › **Çeviriyi Başlat**.
6. Smart TV için: PC'yi HDMI ile TV'ye bağla, <kbd>Win</kbd>+<kbd>P</kbd> › Yalnızca ikinci ekran, ses çıkışını TV yap.

Kısayollar: <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>O</kbd> çeviriyi göster/gizle, <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>L</kbd> yer ve boyut.

Ayrıntılı anlatım: `Ludora-Kurulum-Rehberi.pdf` (sürüm sayfasında).

### Mac + PS5 + Android TV

1. Mac uygulamasını açın, Ekran Kaydı izni verin.
2. PS5'te ikinci hesabı ekleyip Uzaktan Oynatma'yı açın, Mac uygulamasından eşleştirin.
3. TV uygulamasını Mac uygulamasının içinden kurun ve açın; TV, Mac'i ağda kendisi bulur.
4. **Çeviriyi Başlat**.

Adım adım: [docs/KURULUM.md](docs/KURULUM.md)

## Sınırlar ve bilinen sorunlar

- **Windows sürümü gerçek bir Windows bilgisayarda henüz denenmedi.** Tarayıcı Xbox görüntüsünün okunmasına izin vermezse (siyah görüntü) Edge'de grafik hızlandırmayı kapatmak ya da Windows Xbox uygulamasını denemek gerekebilir.
- Windows paketi şimdilik yalnızca Xbox içindir; PS5 bağlantısı Windows paketinde yok.
- Uygulamalar imzasızdır: Windows'ta SmartScreen uyarısı çıkar; Mac'te ilk açılışta sağ tık › Aç gerekir ve güncellemeden sonra Ekran Kaydı izni yeniden verilir.
- Android TV uygulaması yalnızca HDMI görüntüsünü uygulamalara aktaran modellerde çalışır (Philips Android TV'de denendi). Android olmayan akıllı TV'lerde Xbox + PC + Smart TV kurulumu kullanılır.
- OCR yalnızca ekrandaki yazıyı okur; ses çevrilmez. Makine çevirisidir, hatalar olabilir.

## Gizlilik

- Mac'te Apple'ın cihaz üstü çevirisi, Windows'ta bilgisayardaki çeviri modeli kullanıldığında hiçbir metin internete gönderilmez.
- DeepL gibi çevrimiçi bir hizmeti siz seçerseniz yalnızca okunan altyazı metni o hizmete gönderilir; ekran görüntüsü gönderilmez.
- Analitik veya kullanım verisi toplanmaz. Ayrıntı: [PRIVACY.md](PRIVACY.md)

## Lisans

Açık kaynak; **AGPL-3.0** ile dağıtılır. Lisans ve telif bildirimleri: [LICENSE](LICENSE), [NOTICE](NOTICE), [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Xbox, PlayStation, Philips, Apple ve diğer adlar ilgili sahiplerinin markalarıdır; Ludora onlarla bağlantılı değildir.
