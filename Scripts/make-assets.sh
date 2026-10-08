#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
mkdir -p assets Packaging
swift Scripts/make-icon.swift assets/thermal-kun-icon.png
asset_work_dir="$(mktemp -d "${TMPDIR:-/tmp}/thermal-kun-assets.XXXXXX")"
trap 'rm -rf "$asset_work_dir"' EXIT
iconset_directory="$asset_work_dir/AppIcon.iconset"
mkdir -p "$iconset_directory"
for icon_size in 16 32 128 256 512; do
  sips -z "$icon_size" "$icon_size" assets/thermal-kun-icon.png --out "$iconset_directory/icon_${icon_size}x${icon_size}.png" >/dev/null
  retina_size=$((icon_size * 2))
  sips -z "$retina_size" "$retina_size" assets/thermal-kun-icon.png --out "$iconset_directory/icon_${icon_size}x${icon_size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset_directory" -o Packaging/AppIcon.icns
swift Scripts/make-thumbnail.swift "$PWD"
print "生成完了: アプリアイコン、GitHubサムネイル／ソーシャルプレビュー"
