#!/usr/bin/env bash
# Removes CodePad for the current user. It keeps your experiments and settings unless you add --purge-data.
#
#   ./uninstall.sh [--purge-data]
#
# --purge-data also deletes ~/.local/share/codepad (the database) and ~/.config/codepad (the settings). That cannot be undone.
set -euo pipefail

fail() { echo "uninstall.sh: $1" >&2; exit 1; }

purge=0
for argument in "$@"; do
  case "$argument" in
    --purge-data) purge=1 ;;
    -h|--help) sed -n '2,7p' "$0"; exit 0 ;;
    *) fail "unknown option $argument" ;;
  esac
done

data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
config_home="${XDG_CONFIG_HOME:-$HOME/.config}"
root="${CODEPAD_INSTALL_DIR:-$data_home/CodePad}"
bin_dir="${CODEPAD_BIN_DIR:-$HOME/.local/bin}"
apps_dir="${CODEPAD_APPLICATIONS_DIR:-$data_home/applications}"

case "$root" in
  ""|"/"|"$HOME"|"$HOME/"|"$data_home"|"$data_home/") fail "CODEPAD_INSTALL_DIR must be a folder of its own, not $root" ;;
esac

rm -rf "$root/app" "$root/app.new" "$root/app.old"
rmdir "$root" 2>/dev/null || true
if [ -L "$bin_dir/codepad" ]; then rm -f "$bin_dir/codepad"; fi
rm -f "$apps_dir/codepad.desktop"

if [ "$purge" = "1" ]; then
  rm -rf "$data_home/codepad" "$config_home/codepad"
  echo "CodePad and your data are removed."
else
  echo "CodePad is removed. Your data is kept in $data_home/codepad and $config_home/codepad."
fi
