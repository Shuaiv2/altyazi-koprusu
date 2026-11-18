# Altyazı Köprüsü

PlayStation 5 oyunlarına Android TV üzerinde Türkçe altyazı ekleyen, açık kaynaklı bir araç.

PS5'in HDMI çıkışı televizyona doğrudan bağlı kalır; görüntü ve gecikme etkilenmez. Altyazıyı televizyondaki Android TV uygulaması HDMI görüntüsünün üstüne çizer. Metni ise Mac (veya Windows) bilgisayar üretir: bilgisayar, PS5'e **ikinci bir PSN hesabıyla** Remote Play üzerinden bağlanır, oyun ekranını okur (OCR), İngilizce metni Türkçeye çevirir ve yerel ağ üzerinden televizyona gönderir. Mac'te çeviri Apple'ın cihaz üstü çeviri özelliğiyle yapılır.

```
PS5 --HDMI--> Android TV (Altyazı Köprüsü TV uygulaması)
PS5 --Remote Play (2. PSN hesabı)--> Mac/Windows --OCR + çeviri--> yerel ağ --> TV
```

## Gereksinimler

- PS5 ve ikinci bir PSN hesabı (oyunu oynadığınız hesaptan ayrı)
- Android TV (HDMI girişini uygulamalara aktaran bir model gerekir, aşağıya bakın)
- Apple Silicon Mac, macOS 14 veya üstü (Windows sürümü için bkz. sınırlar)
- Mac, PS5 ve TV aynı yerel ağda
- [chiaki-ng](https://github.com/chiaki-ng/chiaki-ng/releases) ve PS5 eşleştirmesi için Node.js LTS

## Hızlı başlangıç

1. Sürümler sayfasından Mac zip'ini ve `AltyaziKoprusu-TV.apk` dosyasını indirin.
2. APK'yı televizyona kurun, Mac uygulamasını açın.
3. PS5'te ikinci hesabı ekleyip Remote Play'i açın, Mac uygulamasından eşleştirin.

Adım adım anlatım: [docs/KURULUM.md](docs/KURULUM.md)

## Sınırlar ve bilinen sorunlar

- Televizyonun HDMI görüntüsünü uygulamalara aktarması gerekir. Yalnızca Philips TPM171E (Android 8) üzerinde denendi; başka modellerde çalışmayabilir.
- Mac uygulaması ad-hoc imzalıdır (Apple Developer hesabıyla imzalı/noter onaylı değil). İlk açılışta sağ tık → Aç gerekir; güncellemeden sonra Ekran Kaydı iznini yeniden vermeniz gerekebilir.
- TV APK'sı, kendi anahtarınızı tanımlamadıysanız debug anahtarıyla imzalanır.
- Windows sürümü derlenir ancak henüz kimse çalıştırıp denemedi. Sorun çıkabilir.
- OCR yalnızca ekrandaki yazıyı okur; sesi çevirmez. Hatalı okuma ve çeviri olabilir.

## Gizlilik

- Apple cihaz üstü çeviri seçiliyken hiçbir metin internete gönderilmez; her şey yerel ağınızda kalır.
- Google, Gemini veya DeepL gibi çevrimiçi bir çeviri hizmetini siz seçerseniz, okunan altyazı metni o hizmete gönderilir. Ekran görüntüsü gönderilmez.
- Uygulama analitik veya kullanım verisi toplamaz. Ayrıntı: [PRIVACY.md](PRIVACY.md)

## Köken ve lisans

Altyazı Köprüsü, sefflex'in [GameTranslator](https://gitlab.com/sefflex/GameTranslator) projesinin değiştirilmiş bir çatalıdır. GameTranslator ise Sinan Yorulmaz'ın [CouchSubs](https://github.com/yorulmazsinan/couchsubs) projesine dayanır. Yapılan değişikliklerin özeti [NOTICE](NOTICE) dosyasındadır; ayrıntılı geçmiş git kayıtlarındadır.

Bu proje **GNU Affero Genel Kamu Lisansı v3 (AGPL-3.0)** ile dağıtılır: [LICENSE](LICENSE). Kaynak kodunu değiştirip bir ağ hizmeti olarak sunarsanız kaynağı da paylaşmanız gerekir. Üçüncü taraf bileşenler: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Sony, PlayStation, Philips, Apple ve diğer adlar ilgili sahiplerine aittir; bu proje onlarla bağlantılı değildir.
