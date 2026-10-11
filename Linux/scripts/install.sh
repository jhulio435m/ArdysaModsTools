#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
set -euo pipefail
if [[ $# != 1 ]]; then printf '%s\n' 'Usage: bash Linux/scripts/install.sh /absolute/path/to/published-artifacts' >&2; exit 2; fi
artifact_dir="$(realpath -- "$1")"
if [[ ! -x "$artifact_dir/ardysa-mods-tools" || ! -f "$artifact_dir/wwwroot/index.html" ]]; then
  printf '%s\n' 'Expected previously published Linux artifacts. This script never compiles.' >&2
  exit 2
fi
case "${XDG_DATA_HOME:-}" in /*) data_root="$XDG_DATA_HOME" ;; *) data_root="$HOME/.local/share" ;; esac
app_dir="$data_root/ardysa-mods-tools/app"
if [[ -e "$app_dir" || -L "$app_dir" ]]; then printf '%s\n' 'An installation already exists. Back it up or move it before installing.' >&2; exit 2; fi
launcher="$HOME/.local/bin/ardysa-mods-tools"
if [[ -e "$launcher" || -L "$launcher" ]]; then printf '%s\n' 'Launcher already exists; refusing to replace it.' >&2; exit 2; fi
desktop_path="$data_root/applications/ardysa-mods-tools.desktop"
if [[ -e "$desktop_path" || -L "$desktop_path" ]]; then printf '%s\n' 'Desktop entry already exists; refusing to replace it.' >&2; exit 2; fi
mkdir -p -- "$app_dir" "$HOME/.local/bin" "$data_root/applications"
cp -a -- "$artifact_dir/." "$app_dir/"
ln -s -- "$app_dir/ardysa-mods-tools" "$launcher"
desktop_exec="${launcher//\\/\\\\}"
desktop_exec="${desktop_exec//\"/\\\"}"
desktop_exec="${desktop_exec//\$/\\\$}"
desktop_exec="${desktop_exec//\`/\\\`}"
desktop_exec="${desktop_exec//%/%%}"
cat > "$desktop_path" <<EOF
[Desktop Entry]
Type=Application
Name=ArdysaModsTools Linux
Comment=Experimental native Dota 2 mod manager
Exec="$desktop_exec" serve
Icon=applications-games
Terminal=true
Categories=Game;
StartupNotify=false
EOF
printf '%s\n' 'User installation complete. No root permissions, game modifications, or compilation were performed.'
