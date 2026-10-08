#!/bin/zsh
set -euo pipefail

# メインアプリを起動せず、監視モデルの正確性と保持量を検証する。
project_dir="$(cd "$(dirname "$0")/.." && pwd)"
check_dir="$(mktemp -d "${TMPDIR:-/tmp}/thermal-kun-check.XXXXXX")"
trap 'rm -rf "$check_dir"' EXIT

swiftc "$project_dir/Sources/ThermalKun/Models.swift" \
    "$project_dir/Sources/ThermalKun/WindowGeometry.swift" \
    "$project_dir/Tests/ModelChecks.swift" \
    -o "$check_dir/ModelChecks"
"$check_dir/ModelChecks"
