#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."

app_directory="$PWD/build/thermal kun.app"
if [[ ! -d "$app_directory" ]]; then
  ./Scripts/build.sh
fi
codesign --verify --strict "$app_directory"
release_version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$app_directory/Contents/Info.plist")"
source_version="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' Packaging/Info.plist)"
if [[ "$release_version" != "$source_version" ]]; then
  print -u2 'バージョンが一致しません。アプリを終了し、./Scripts/build.sh で更新してください。'
  exit 1
fi
release_arch="$(lipo -archs "$app_directory/Contents/MacOS/ThermalKun")"
release_arch="${release_arch// /-}"
release_name="thermal-kun-macOS-${release_arch}-v${release_version}"
staging_directory="$(mktemp -d "${TMPDIR:-/tmp}/thermal-kun-release.XXXXXX")"
trap 'rm -rf "$staging_directory"' EXIT
release_directory="$staging_directory/$release_name"
mkdir -p "$release_directory" dist
ditto "$app_directory" "$release_directory/thermal kun.app"
cp README.md README.ja.md LICENSE THIRD_PARTY_NOTICES.md CHANGELOG.md "$release_directory/"
ditto assets "$release_directory/assets"
ditto docs "$release_directory/docs"
ditto -c -k --norsrc --noextattr --noqtn --keepParent "$release_directory" "$PWD/dist/$release_name.zip"
(
  cd dist
  shasum -a 256 "$release_name.zip" > "$release_name.zip.sha256"
)
print "配布ZIP: $PWD/dist/$release_name.zip"
print "SHA-256: $PWD/dist/$release_name.zip.sha256"
