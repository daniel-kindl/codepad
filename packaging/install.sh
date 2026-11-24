#!/usr/bin/env bash
# Installs CodePad for the current user from a release archive. It needs no root rights and no .NET.
#
#   ./install.sh codepad-<version>-linux-x64.tar.gz [--sha256 HEX]
#
# The archive goes to ~/.local/share/CodePad/app. A link ~/.local/bin/codepad points to the command line, and a launcher entry
# is written to ~/.local/share/applications. The same command updates an existing installation: the new files are unpacked
# next to the old ones and replace them in one step. Your data is not in that folder and is never touched.
#
# Variables for tests and special layouts: CODEPAD_INSTALL_DIR, CODEPAD_BIN_DIR, CODEPAD_APPLICATIONS_DIR.
set -euo pipefail

fail() { echo "install.sh: $1" >&2; exit 1; }

archive=""
expected=""
while [ "$#" -gt 0 ]; do
  case "$1" in
    --sha256) [ "$#" -ge 2 ] || fail "--sha256 needs a value"; expected="$2"; shift 2 ;;
    -h|--help) sed -n '2,12p' "$0"; exit 0 ;;
    -*) fail "unknown option $1" ;;
    *) [ -z "$archive" ] || fail "give one archive"; archive="$1"; shift ;;
  esac
done
[ -n "$archive" ] || fail "give the archive: ./install.sh codepad-<version>-linux-x64.tar.gz"
[ -f "$archive" ] || fail "the archive does not exist"

data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
root="${CODEPAD_INSTALL_DIR:-$data_home/CodePad}"
bin_dir="${CODEPAD_BIN_DIR:-$HOME/.local/bin}"
apps_dir="${CODEPAD_APPLICATIONS_DIR:-$data_home/applications}"

# A folder that is the home folder or the root of the file system would be wiped by an update or an uninstall. Refuse it.
case "$root" in
  ""|"/"|"$HOME"|"$HOME/"|"$data_home"|"$data_home/") fail "CODEPAD_INSTALL_DIR must be a folder of its own, not $root" ;;
esac

# Check the digest when the user gives one, or when a SHA256SUMS file stands next to the archive. The name must match the whole name.
sums="$(dirname "$archive")/SHA256SUMS"
if [ -z "$expected" ] && [ -f "$sums" ]; then
  expected="$(awk -v name="$(basename "$archive")" '$2 == name { print $1 }' "$sums" | head -n 1)"
  [ -n "$expected" ] || fail "SHA256SUMS does not list $(basename "$archive"). Nothing was installed."
fi
if [ -n "$expected" ]; then
  actual="$(sha256sum "$archive" | cut -d' ' -f1)"
  [ "$actual" = "$expected" ] || fail "the SHA-256 digest of the archive does not match. Nothing was installed."
fi

mkdir -p "$root" "$bin_dir" "$apps_dir"
stage="$root/app.new"
rm -rf "$stage"
mkdir "$stage"
trap 'rm -rf "$stage"' EXIT

# tar removes a leading slash and refuses a path with "..". The files keep their modes, so the programs stay executable.
tar -xzf "$archive" -C "$stage" --no-same-owner
[ -x "$stage/Codepad.Desktop" ] || fail "the archive has no Codepad.Desktop program. Nothing was installed."
[ -x "$stage/codepad" ] || fail "the archive has no codepad command. Nothing was installed."

if [ -d "$root/app" ]; then
  rm -rf "$root/app.old"
  mv "$root/app" "$root/app.old"
fi
mv "$stage" "$root/app"
rm -rf "$root/app.old"
trap - EXIT

if [ -e "$bin_dir/codepad" ] && [ ! -L "$bin_dir/codepad" ]; then
  echo "install.sh: $bin_dir/codepad is not a link, so it was left as it is. Use $root/app/codepad." >&2
else
  ln -sfn "$root/app/codepad" "$bin_dir/codepad"
fi
cat > "$apps_dir/codepad.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=CodePad
Comment=Run C# snippets and check JSON
Exec="$root/app/Codepad.Desktop"
Terminal=false
Categories=Development;
DESKTOP

echo "CodePad is installed in $root/app"
echo "The command is $bin_dir/codepad. Make sure $bin_dir is in your PATH."
echo "Your data stays in ${XDG_DATA_HOME:-$HOME/.local/share}/codepad. Remove CodePad with uninstall.sh."
