# Altyazı süzgeci testleri

Repo kökünden:

    C=core/Sources/GameTranslatorCore
    swiftc -parse-as-library -o /tmp/subtitle-filter-tests mac/SubtitleFilter.swift \
      $C/SubtitleStabilizer.swift $C/OcrNoise.swift $C/TextMatch.swift mac/tests/SubtitleFilterTests.swift \
      && /tmp/subtitle-filter-tests

Hata ayıklama: uygulamayı `AK_DEBUG_FILTER=1` ile başlatınca elenen satırlar ve nedenleri stderr'e yazılır.
