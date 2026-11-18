# Kurulum

Sıra: Mac (veya Windows) → PS5 → Android TV.

## Mac

1. Sürümler sayfasından `AltyaziKoprusu-macOS-arm64.zip` dosyasını indirip açın, `AltyaziKoprusu.app` uygulamasını Uygulamalar klasörüne taşıyın.
2. Uygulama ad-hoc imzalıdır. İlk açılışta çift tıklamak yerine uygulamaya **sağ tıklayıp → Aç** deyin ve onaylayın.
3. **Sistem Ayarları → Gizlilik ve Güvenlik → Ekran ve Sistem Ses Kaydı** bölümünde Altyazı Köprüsü'ne izin verin. Uygulamayı her güncellediğinizde bu izni kapatıp açmanız ya da yeniden vermeniz gerekebilir.
4. [chiaki-ng](https://github.com/chiaki-ng/chiaki-ng/releases) uygulamasını yalnızca resmi GitHub sürümler sayfasından indirip kurun.
5. PS5 eşleştirmesi için [nodejs.org](https://nodejs.org) adresinden Node.js LTS kurun.
6. **Sistem Ayarları → Genel → Dil ve Bölge → Çeviri Dilleri** bölümünden **hem İngilizce hem Türkçe** dilini indirin. Biri eksikse çeviri çalışmaz.

## PS5

1. **Ayarlar → Kullanıcılar ve Hesaplar** bölümünde ikinci PSN hesabını **kalıcı kullanıcı** olarak ekleyin. Tek seferlik veya misafir kullanıcı olmaz.
2. O kullanıcı için **Ayarlar → Sistem → Remote Play** özelliğini açın (Remote Play etkin olmalı).
3. Eşleştirme sırasında ikinci kullanıcı PS5'te etkin olmalıdır. **Cihazı Bağla (Link Device)** kodu o kullanıcı etkinken gösterilir; kodu Mac uygulamasındaki eşleştirme ekranına girin.
4. **Ayarlar → Sistem → HDMI → "HDMI Aygıt Bağlantısını Etkinleştir"** seçeneğini kapatın. Açık kalırsa TV kendi HDMI görünümüne atlayabilir.

## Android TV

1. `AltyaziKoprusu-TV.apk` dosyasını USB belleğe koyun, TV'ye takın ve dosya yöneticisiyle kurun (bilinmeyen kaynaklardan kuruluma izin verin). Alternatif: `adb install AltyaziKoprusu-TV.apk`.
2. Uygulamayı açın. Kumandada **1–4 tuşlarıyla** PS5'in bağlı olduğu HDMI girişini seçin.
3. Altyazı konumu ve boyutu için kumandadaki menüden uzaktan düzen (remote layout) ekranını açın.

## Windows

Bu bölüm geçicidir; Windows sürümü henüz denenmedi.

1. Sürümler sayfasından `AltyaziKoprusu-Windows-x64.zip` dosyasını indirip açın.
2. SmartScreen uyarısı çıkarsa **Daha fazla bilgi → Yine de çalıştır** deyin. Uygulama imzalı değildir.
3. Eşleştirme ve PS5 adımları Mac ile aynıdır.

## Sorun giderme

- **Hata 0x80108b12**: O kullanıcı veya konsol için Remote Play kapalı. PS5'te ikinci kullanıcının Remote Play ayarını açın ve konsolun uyku/açık durumunu kontrol edin.
- **Siyah ekran**: Kumandada PS5'in bağlı olduğu HDMI numarasına (1–4) basın.
- **TV Mac'i bulamıyor**: TV uygulamasında menüden **"Mac adresini elle gir"** seçeneğiyle Mac'in IP adresini yazın.
- **Adres sürekli değişiyor**: Yönlendiricinizde Mac için DHCP rezervasyonu (sabit IP) tanımlayın.
- **Çeviri çıkmıyor**: İngilizce ve Türkçe çeviri dillerinin ikisinin de indirildiğini ve Ekran Kaydı izninin verildiğini kontrol edin.
