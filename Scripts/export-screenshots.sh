#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
if pgrep -x ThermalKun >/dev/null; then
  print -u2 'スクリーンショットを更新する前に、thermal kunを終了してください。'
  exit 1
fi
if [[ ! -d 'build/thermal kun.app' ]]; then
  ./Scripts/build.sh
fi
export THERMAL_KUN_ASSET_DIRECTORY="$PWD/assets/screenshots"
export_marker="$(mktemp "${TMPDIR:-/tmp}/thermal-kun-export.XXXXXX")"
trap 'rm -f "$export_marker"' EXIT
'build/thermal kun.app/Contents/MacOS/ThermalKun' --export-assets
for screenshot_name in detail-dark compact-dark detail-light settings; do
  if [[ ! -s "$THERMAL_KUN_ASSET_DIRECTORY/$screenshot_name.png" || ! "$THERMAL_KUN_ASSET_DIRECTORY/$screenshot_name.png" -nt "$export_marker" ]]; then
    print -u2 "画像を生成できませんでした: $screenshot_name.png"
    exit 1
  fi
done
print '実測値を表示したアプリ画像を生成しました。'
