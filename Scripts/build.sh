#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
if pgrep -x ThermalKun >/dev/null; then
  print -u2 "ビルドを更新する前に、thermal kunのメニューからQuit thermal kunを選んで終了してください。"
  exit 1
fi
swift build -c release --jobs 4
binary_directory="$(swift build -c release --show-bin-path)"
app_directory="$PWD/build/thermal kun.app"
mkdir -p "$app_directory/Contents/MacOS" "$app_directory/Contents/Resources"
cp "$binary_directory/ThermalKun" "$app_directory/Contents/MacOS/ThermalKun"
cp Packaging/Info.plist "$app_directory/Contents/Info.plist"
if [[ -f Packaging/AppIcon.icns ]]; then
  cp Packaging/AppIcon.icns "$app_directory/Contents/Resources/AppIcon.icns"
fi
if [[ -f THIRD_PARTY_NOTICES.md ]]; then
  cp THIRD_PARTY_NOTICES.md "$app_directory/Contents/Resources/THIRD_PARTY_NOTICES.md"
fi
if [[ -f LICENSE ]]; then
  cp LICENSE "$app_directory/Contents/Resources/LICENSE"
fi
codesign --force --sign - "$app_directory"
codesign --verify --strict "$app_directory"
print "生成完了: $app_directory"
