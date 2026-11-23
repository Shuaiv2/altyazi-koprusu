# Altyazı süzgeci testleri

Repo kökünden:

    C=core/Sources/GameTranslatorCore
    swiftc -parse-as-library -o /tmp/subtitle-filter-tests mac/SubtitleFilter.swift \
      $C/SubtitleStabilizer.swift $C/OcrNoise.swift $C/TextMatch.swift mac/tests/SubtitleFilterTests.swift \
      && /tmp/subtitle-filter-tests

Cümle birleştirici testleri:

    C=core/Sources/GameTranslatorCore
    swiftc -parse-as-library -o /tmp/sentence-joiner-tests mac/SentenceJoiner.swift $C/TextMatch.swift mac/tests/SentenceJoinerTests.swift \
      && /tmp/sentence-joiner-tests

Hata ayıklama: uygulamayı `AK_DEBUG_FILTER=1` ile başlatınca elenen satırlar ve nedenleri stderr'e yazılır.

Bilgi kutusu, tekrar gönderim süzgeci ve GameEnglish testleri:

    C=core/Sources/GameTranslatorCore
    swiftc -parse-as-library -o /tmp/infobox-tests mac/InfoBox.swift mac/SubtitleFilter.swift \
      $C/OcrNoise.swift $C/TextMatch.swift $C/GameEnglish.swift mac/tests/InfoBoxTests.swift \
      && /tmp/infobox-tests

Ekran metni (görev hedefi / bilgi kutusu / bildirim / harita-menü açıklaması) sınıflandırması ve izleyici
(fikstürler araclar/izleme/ocr.tsv'deki gerçek GTA 6 karelerinden):

    C=core/Sources/GameTranslatorCore
    swiftc -parse-as-library -o /tmp/screentext-tests mac/ScreenText.swift mac/InfoBox.swift mac/SubtitleFilter.swift \
      $C/OcrNoise.swift $C/TextMatch.swift mac/tests/ScreenTextTests.swift \
      && /tmp/screentext-tests
