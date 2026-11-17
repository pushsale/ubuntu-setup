#!/usr/bin/env bash
# Cài Quick Shot và gán phím Print cho nó.
#   ./install.sh             cài / cập nhật
#   ./install.sh uninstall   gỡ, trả phím Print về giao diện chụp của GNOME
# Sau khi cài: Print = Quick Shot; Ctrl+Print = giao diện chụp gốc của GNOME (có quay màn hình).
set -euo pipefail

SRC="$(cd "$(dirname "$0")" && pwd)"
DEST="$HOME/.local/share/quick-shot"
APP_ID="com.ubuntu_assistant.QuickShot"
DESKTOP="$HOME/.local/share/applications/$APP_ID.desktop"

KB_LIST_SCHEMA="org.gnome.settings-daemon.plugins.media-keys"
KB_PATH="/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/quick-shot/"
KB_SCHEMA="org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:$KB_PATH"

# Danh sách custom-keybindings dạng GVariant: thêm / bớt một đường dẫn
kb_list_edit() {  # $1 = add | remove
    python3 - "$1" "$KB_PATH" "$(gsettings get $KB_LIST_SCHEMA custom-keybindings)" <<'PY'
import ast, sys
op, path, cur = sys.argv[1], sys.argv[2], sys.argv[3]
cur = cur.removeprefix('@as ')
items = ast.literal_eval(cur)
if op == 'add' and path not in items:
    items.append(path)
if op == 'remove':
    items = [i for i in items if i != path]
print(str(items))
PY
}

if [ "${1:-}" = "uninstall" ]; then
    gsettings set $KB_LIST_SCHEMA custom-keybindings "$(kb_list_edit remove)"
    gsettings reset-recursively "$KB_SCHEMA" 2>/dev/null || true
    gsettings set org.gnome.shell.keybindings show-screenshot-ui "['Print', '<Ctrl>Print']"
    rm -rf "$DEST" "$DESKTOP"
    echo "Đã gỡ Quick Shot. Phím Print trở lại giao diện chụp của GNOME."
    exit 0
fi

command -v wl-copy >/dev/null || { echo "Thiếu wl-copy (dùng cho nút Copy): sudo apt install wl-clipboard" >&2; exit 1; }

mkdir -p "$DEST" "$(dirname "$DESKTOP")"
cp "$SRC/quick-shot.js" "$DEST/"

cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=Quick Shot
Comment=Chụp vùng màn hình, vẽ chữ, mũi tên, khung
Exec=gjs -m $DEST/quick-shot.js
Icon=applets-screenshooter
Terminal=false
Categories=Utility;Graphics;
StartupNotify=false
EOF

# Print: Quick Shot. Giao diện chụp gốc của GNOME chuyển sang Ctrl+Print.
gsettings set org.gnome.shell.keybindings show-screenshot-ui "['<Ctrl>Print']"
gsettings set $KB_LIST_SCHEMA custom-keybindings "$(kb_list_edit add)"
gsettings set "$KB_SCHEMA" name 'Quick Shot'
gsettings set "$KB_SCHEMA" command "gjs -m $DEST/quick-shot.js"
gsettings set "$KB_SCHEMA" binding 'Print'

echo "Đã cài Quick Shot vào $DEST"
echo "Print: chụp và vẽ bằng Quick Shot. Ctrl+Print: giao diện chụp gốc của GNOME."
