#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
set -euo pipefail
if [[ "${1:-}" != "--compile" ]]; then
  printf '%s\n' 'No compilation performed. Explicit invocation: bash Linux/scripts/build.sh --compile [linux-x64|linux-arm64]' >&2
  exit 2
fi
runtime="${2:-linux-x64}"
case "$runtime" in linux-x64|linux-arm64) ;; *) printf '%s\n' 'Unsupported runtime.' >&2; exit 2 ;; esac
project_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
command -v dotnet >/dev/null
command -v npm >/dev/null
cd "$project_root/Frontend"
npm ci
npm run typecheck
AMT_PAGE=linux npm exec -- vite build --outDir ../Linux/Host/wwwroot --emptyOutDir
cp ../Linux/Host/wwwroot/src/pages/linux/index.html ../Linux/Host/wwwroot/index.html
cd "$project_root"
dotnet test Linux/Tests/ArdysaModsTools.Linux.Tests.csproj --configuration Release
dotnet publish Linux/Host/ArdysaModsTools.Linux.csproj --configuration Release --runtime "$runtime" --self-contained true --output "artifacts/$runtime"
cp LICENSE NOTICE "artifacts/$runtime/"
printf '%s\n' "Built artifacts/$runtime/ardysa-mods-tools. No game files were changed."
