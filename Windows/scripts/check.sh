#!/usr/bin/env bash
set -euo pipefail
windows_root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_bin="${DOTNET_BIN:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_PROCESSOR_COUNT=2
cd "$windows_root"
nice -n 10 "$dotnet_bin" run --project tests/ThermalKun.Checks/ThermalKun.Checks.csproj -c Release -p:UseSharedCompilation=false
