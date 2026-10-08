#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
if [[ ! -d 'build/thermal kun.app' ]]; then
  ./Scripts/build.sh
fi
open 'build/thermal kun.app'
