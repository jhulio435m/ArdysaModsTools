#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
set -euo pipefail
case "${XDG_DATA_HOME:-}" in /*) data_root="$XDG_DATA_HOME" ;; *) data_root="$HOME/.local/share" ;; esac
case "${XDG_STATE_HOME:-}" in /*) state_root="$XDG_STATE_HOME" ;; *) state_root="$HOME/.local/state" ;; esac
app="$data_root/ardysa-mods-tools/app/ardysa-mods-tools"
log_dir="$state_root/ardysa-mods-tools"
mkdir -p -- "$log_dir"
umask 077
if command -v systemd-run >/dev/null && systemctl --user show-environment >/dev/null 2>&1; then
  desktop_env=()
  for name in DISPLAY WAYLAND_DISPLAY XDG_CURRENT_DESKTOP XDG_RUNTIME_DIR DBUS_SESSION_BUS_ADDRESS PATH XDG_DATA_HOME XDG_CONFIG_HOME XDG_STATE_HOME XDG_CACHE_HOME; do
    if [[ -v "$name" ]]; then desktop_env+=("--setenv=$name=${!name}"); fi
  done
  exec systemd-run --user --collect --quiet "${desktop_env[@]}" "$app" serve
fi
nohup "$app" serve </dev/null >>"$log_dir/desktop.log" 2>&1 &
