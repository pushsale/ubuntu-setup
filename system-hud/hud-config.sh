#!/usr/bin/env bash
# Đổi cấu hình HUD đang cài. Từ phiên đăng nhập có extension bản mới, thay đổi áp dụng ngay.
# Dùng:
#   ./hud-config.sh                    xem cấu hình hiện tại
#   ./hud-config.sh monitor secondary  chuyển sang màn hình phụ (primary | secondary | 0 | 1 ...)
#   ./hud-config.sh side right         đặt sát mép phải (left | right)
#   ./hud-config.sh theme orbitalLight đổi giao diện (orbital | orbitalLight | card | tactical | terminal)
#   ./hud-config.sh widthFraction 0.3  rộng 30% màn hình
#   ./hud-config.sh intervalMs 5000    lấy số liệu 5 giây một lần (animMs: thời gian chuyển, fps: khung hình/giây)
#   ./hud-config.sh ui                 mở cửa sổ cấu hình (giống bấm biểu tượng HUD trên thanh trên cùng)
set -euo pipefail
DIR="$HOME/.local/share/gnome-shell/extensions/system-hud@ubuntu-assistant"
CFG="$DIR/config.json"
[ -f "$CFG" ] || { echo "Chưa cài HUD ($CFG không tồn tại)"; exit 1; }
if [ "${1:-}" = "ui" ]; then
    exec gjs -m "$DIR/settings-app.js"
fi
if [ $# -ge 2 ]; then
    python3 - "$CFG" "$1" "$2" <<'PY'
import json, sys
path, key, val = sys.argv[1:4]
cfg = json.load(open(path))
try:
    val = json.loads(val)          # số, true/false
except ValueError:
    pass                           # chuỗi
cfg[key] = val
json.dump(cfg, open(path, "w"), indent=2)
PY
fi
cat "$CFG"; echo
