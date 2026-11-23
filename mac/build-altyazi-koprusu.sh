#!/bin/bash
# Ludora macOS derleme betiği (GameTranslator tabanlı, AGPL-3.0).
# Kullanım: mac/build-altyazi-koprusu.sh [çıktı_klasörü]   (varsayılan: <repo>/../teslim)
# Altyazı süzgeci testleri: bkz. mac/tests/README.md
# Yalnızca çıktı klasörüne yazar. /Applications'a kurmaz, karantina özniteliğine dokunmaz, zip üretmez.
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$DIR/.." && pwd)"
OUT="${1:-$ROOT/../teslim}"
mkdir -p "$OUT"
OUT="$(cd "$OUT" && pwd)"
APP="$OUT/Ludora.app"
MACOS="$APP/Contents/MacOS"
RES="$APP/Contents/Resources"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/altyazi-build.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

echo "==> Swift derleniyor"
CORE="$ROOT/core/Sources/GameTranslatorCore"
swiftc -O -parse-as-library -target arm64-apple-macos14.0 "$DIR/NativeApp.swift" "$DIR/SubtitleFilter.swift" "$DIR/SentenceJoiner.swift" "$DIR/InfoBox.swift" "$DIR/ScreenText.swift" "$DIR/BubbleColor.swift" \
  "$CORE/SubtitleStabilizer.swift" "$CORE/OcrNoise.swift" "$CORE/TextMatch.swift" "$CORE/GameEnglish.swift" -o "$WORK/Ludora"

echo "==> Paket hazırlanıyor: $APP"
rm -rf "$APP"
mkdir -p "$MACOS" "$RES"
cp "$WORK/Ludora" "$MACOS/Ludora"
chmod +x "$MACOS/Ludora"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key>
	<string>Ludora</string>
	<key>CFBundleIconFile</key>
	<string>AppIcon</string>
	<key>CFBundleIdentifier</key>
	<string>com.altyazikoprusu.mac</string>
	<key>CFBundleName</key>
	<string>Ludora</string>
	<key>CFBundleDisplayName</key>
	<string>Ludora</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleShortVersionString</key>
	<string>1.7.5</string>
	<key>CFBundleVersion</key>
	<string>1</string>
	<key>LSMinimumSystemVersion</key>
	<string>14.0</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSScreenCaptureUsageDescription</key>
	<string>Ludora, PS5 oyun görüntüsünü analiz ederek altyazıları Türkçeye çevirir.</string>
	<key>NSLocalNetworkUsageDescription</key>
	<string>Ludora, çevrilen altyazıları göndermek için yerel ağdaki PS5 ve televizyonu bulur.</string>
</dict>
</plist>
PLIST
plutil -lint "$APP/Contents/Info.plist"

echo "==> Simge"
ICON_SRC="$ROOT/design/altyazi-koprusu/icon-1024.png"
if [ -f "$ICON_SRC" ]; then
  SET="$WORK/AppIcon.iconset"
  mkdir -p "$SET"
  for s in 16 32 128 256 512; do
    sips -z $s $s "$ICON_SRC" --out "$SET/icon_${s}x${s}.png" >/dev/null
    sips -z $((s*2)) $((s*2)) "$ICON_SRC" --out "$SET/icon_${s}x${s}@2x.png" >/dev/null
  done
  iconutil -c icns "$SET" -o "$RES/AppIcon.icns"
else
  echo "UYARI: $ICON_SRC yok, simge eklenmedi."
fi

echo "==> Kaynaklar"
[ -f "$DIR/GameTranslator/Resources/game-frame.png" ] && cp "$DIR/GameTranslator/Resources/game-frame.png" "$RES/"

mkdir -p "$RES/tv-app"
APK="$ROOT/tv/android/app/build/outputs/apk/release/app-release.apk"
if [ -f "$APK" ]; then
  cp "$APK" "$RES/tv-app/altyazi-koprusu-tv.apk"
else
  echo "UYARI: TV APK bulunamadı ($APK); pakete eklenmedi."
fi

# Yalnızca metin/betik biçimindeki yardımcılar (hazır ikili dosya KOPYALANMAZ).
mkdir -p "$RES/scripts"
cp "$ROOT/scripts/android-tv-install.py" "$RES/scripts/android-tv-install.py"
chmod +x "$RES/scripts/android-tv-install.py"

mkdir -p "$RES/ps5-register"
cp "$ROOT/scripts/gametranslator-ps5-register" "$RES/ps5-register/"
cp "$ROOT/scripts/ps5-register.js" "$RES/ps5-register/"
chmod +x "$RES/ps5-register/gametranslator-ps5-register"
if [ -d "$ROOT/scripts/ps5-register/node_modules" ]; then
  cp -R "$ROOT/scripts/ps5-register/node_modules" "$RES/ps5-register/node_modules"
else
  echo "UYARI: ps5-register/node_modules yok; PS5 eşleştirme çalışmaz."
fi

# TV altyazı düzenleyicisi (tv/editor/* + FILES.txt'te listelenen tv/ göreli dosyalar)
EDITOR_SRC="$ROOT/tv/editor"
if [ -d "$EDITOR_SRC" ]; then
  mkdir -p "$RES/tv-web/editor"
  cp -R "$EDITOR_SRC/." "$RES/tv-web/editor/"
  if [ -f "$EDITOR_SRC/FILES.txt" ]; then
    while IFS= read -r rel || [ -n "$rel" ]; do
      rel="${rel%$'\r'}"
      case "$rel" in ""|\#*) continue ;; esac
      if [ ! -f "$ROOT/tv/$rel" ]; then
        echo "HATA: tv/editor/FILES.txt içinde listelenen dosya yok: tv/$rel" >&2
        exit 1
      fi
      mkdir -p "$RES/tv-web/$(dirname "$rel")"
      cp "$ROOT/tv/$rel" "$RES/tv-web/$rel"
    done < "$EDITOR_SRC/FILES.txt"
  else
    echo "UYARI: tv/editor/FILES.txt yok; yalnızca tv/editor/* kopyalandı."
  fi
else
  echo "UYARI: tv/editor yok; TV altyazı düzenleyicisi pakete eklenmedi."
fi

# Lisans bildirimleri
for f in LICENSE NOTICE THIRD-PARTY-NOTICES.md; do
  [ -f "$ROOT/$f" ] && cp "$ROOT/$f" "$RES/"
done

echo "==> Ad-hoc imza"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"
echo "Hazır: $APP"
