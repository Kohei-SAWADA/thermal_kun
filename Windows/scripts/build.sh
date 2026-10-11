#!/usr/bin/env bash
set -euo pipefail
windows_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_bin="${DOTNET_BIN:-dotnet}"
runtime="${1:-win-x64}"
case "$runtime" in win-x64|win-arm64) ;; *) echo "Usage: build.sh [win-x64|win-arm64]" >&2; exit 2;; esac
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_PROCESSOR_COUNT=2
cd "$windows_root"
nice -n 10 "$dotnet_bin" publish src/ThermalKun.App/ThermalKun.App.csproj -c Release -r "$runtime" --self-contained true \
  -o "artifacts/$runtime" -m:1 -p:UseSharedCompilation=false -p:PublishReadyToRun=false -p:PublishTrimmed=false
