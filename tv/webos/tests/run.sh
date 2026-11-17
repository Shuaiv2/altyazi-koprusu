#!/bin/zsh
# TV web arayüzü testleri: tüm dosyaların sözdizimi + ayar modeli birim testleri. macOS'un yerleşik jsc'sini kullanır.
set -e
J=/System/Library/Frameworks/JavaScriptCore.framework/Versions/A/Helpers/jsc
cd "${0:A:h}/.."
for f in app.js i18n.js config.js subtitle-settings.js subtitle-layout.js ../android/platform.js ../editor/editor.js; do
  out=$("$J" -e "checkSyntax('$f')" 2>&1) || { echo "SÖZDİZİMİ HATASI: $f"; echo "$out"; exit 1; }
done
if grep -nE "=>|\blet |\bconst |\`" app.js i18n.js subtitle-settings.js subtitle-layout.js; then echo "ES5 dışı sözdizimi bulundu"; exit 1; fi
"$J" subtitle-settings.js tests/subtitle-settings.test.js
echo "Sözdizimi ve birim testleri geçti."
