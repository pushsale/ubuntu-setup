# Quick Shot — Chi tiết kỹ thuật

## Tổng quan

Công cụ chụp vùng màn hình + vẽ chú thích (Mũi tên / Khung / Chữ) cho Ubuntu 26.04, GNOME Shell 50.1, Wayland. Một file GJS chạy trực tiếp (`gjs -m`), không biên dịch.

- Tech stack: GJS 1.88 (ESM), GTK 4, libadwaita 1.9, Cairo (`import Cairo from 'cairo'`), Pango / PangoCairo, xdg-desktop-portal (D-Bus), `wl-copy`.
- Mã nguồn: `quick-shot/quick-shot.js`, `quick-shot/install.sh`, `quick-shot/README.md`.
- Cài vào: `~/.local/share/quick-shot/quick-shot.js`, `~/.local/share/applications/com.ubuntu_assistant.QuickShot.desktop`.

Cách chạy:

```bash
cd quick-shot && ./install.sh                 # cài / cập nhật, gán phím Print
./install.sh uninstall                        # gỡ, trả Print cho GNOME
gjs -m quick-shot.js                          # chụp tương tác (như bấm Print)
gjs -m quick-shot.js ảnh.png                  # mở ảnh PNG có sẵn để vẽ
QUICK_SHOT_SELFTEST=out.png gjs -m quick-shot.js in.png   # kiểm thử vẽ, không cần màn hình
```

## Kiến trúc + pipeline

```
Print ─► gnome-shell (custom keybinding quick-shot) ─► gjs -m ~/.local/share/quick-shot/quick-shot.js
   │
   ▼  Adw.Application com.ubuntu_assistant.QuickShot (HANDLES_OPEN, một instance)
   │   lần chạy thứ 2 trở đi: gửi activate sang tiến trình đang chạy rồi thoát
   ▼
 'activate' ─► app.hold() ─► portalScreenshot(true)
   │             org.freedesktop.portal.Screenshot.Screenshot('', {handle_token, interactive: true})
   │             ─► GNOME Shell mở giao diện chụp ─► người dùng chọn vùng ─► Request.Response(0, {uri})
   ▼
 EditorWindow(path)  Cairo.ImageSurface.createFromPNG
   │  DrawingArea: vẽ ảnh thu nhỏ vừa khung (không phóng to) + annotations + draft
   │  GestureDrag: arrow / rect; bấm không kéo với tool text: ô nhập Gtk.Entry trong Gtk.Overlay
   ▼
 Copy: renderSurface ─► PNG tạm trong $XDG_RUNTIME_DIR ─► wl-copy --type image/png (stdin) ─► đóng cửa sổ
 Lưu : Gtk.FileDialog (mặc định ~/Pictures/Screenshots/Screenshot-<thời gian>.png) ─► writeToPNG ─► đóng
```

## Luồng chi tiết (code references, `quick-shot/quick-shot.js`)

| Bước | Dòng | Ghi chú |
|---|---|---|
| Hằng: `APP_ID`, `SIZES` (Mảnh/Vừa/Đậm: nét 2/4/7 px, chữ 16/24/36 px), `TOOLS` (phần tử đầu = công cụ chọn sẵn) | 17–30 | Đơn vị là px của ảnh gốc, không phải px màn hình |
| `portalScreenshot(interactive)` | 36 | Đăng ký nghe `Response` trên đường dẫn request **trước** khi gọi; code 0 = có ảnh, 1 = huỷ (Esc), 2 = lỗi |
| `drawArrow`, `drawRect`, `drawText`, `drawAnnotation` | 83–145 | Mũi tên: thân dừng ở 60% đầu mũi tên để đầu nhọn không bị nét tròn làm cùn. Chữ: viền tương phản (đen nếu chữ sáng, trắng nếu chữ tối) rồi tô màu |
| `renderSurface(image, annotations)` | 147 | Ảnh gốc + chú thích ở độ phân giải gốc; dùng cho Copy, Lưu, selftest |
| `EditorWindow._build` | 179 | HeaderBar: nhóm ToggleButton công cụ, ColorDialogButton, DropDown độ dày, Hoàn tác; bên phải Lưu…, Copy. Vùng vẽ: DrawingArea trong Gtk.Overlay trong Adw.ToastOverlay |
| `_addShortcuts` | 261 | ShortcutController trên cửa sổ (bubble): Ctrl+C/S/Z, Esc, A/R/T. Đang gõ trong Entry thì Entry ăn phím trước |
| `_toImage`, `_draw` | 292, 300 | `view = {s, ox, oy}` tính lại mỗi lần vẽ; s = min(1, w/W, h/H), ảnh đặt giữa |
| `_dragBegin/_dragUpdate/_dragEnd` | 326–358 | Hình kéo < 4 px màn hình bị bỏ (bấm nhầm). Tool text: kéo < 6 px thì mở ô nhập |
| `_openTextEntry`, `_finishText(keep)` | 360, 387 | Ô nhập đặt dưới điểm bấm bằng margin; chữ hiện trực tiếp khi gõ; Enter / bấm chỗ khác / Copy / Lưu = giữ; Esc = bỏ |
| `copy()` | 411 | Xem Architectural Decisions về wl-copy |
| `save()` | 438 | Thêm `.png` nếu thiếu đuôi |
| `app.connect('activate')` | 486 | `hold()` trong lúc giao diện GNOME mở (chưa có cửa sổ nào), `release()` sau khi xong |
| `app.connect('open')`, selftest | 497, 503 | Mở ảnh có sẵn; `QUICK_SHOT_SELFTEST` vẽ chú thích mẫu và ghi PNG rồi thoát, không chạy app |

## Data model

Không có cơ sở dữ liệu, không lưu cấu hình. Chú thích giữ trong bộ nhớ của từng cửa sổ:

| type | Trường |
|---|---|
| `arrow` | `x1, y1, x2, y2, color [r,g,b,a], width` |
| `rect` | `x1, y1, x2, y2, color, width` |
| `text` | `x, y` (góc trên trái), `text, color, size` |

Toạ độ theo px của ảnh gốc.

## API / protocol

- **xdg-desktop-portal Screenshot**: `org.freedesktop.portal.Desktop` `/org/freedesktop/portal/desktop`, method `org.freedesktop.portal.Screenshot.Screenshot(s parent_window, a{sv} options)`; kết quả qua signal `org.freedesktop.portal.Request.Response(u code, a{sv} results)` trên `/org/freedesktop/portal/desktop/request/<sender>/<token>`.
  - `interactive: true` không kiểm tra PermissionStore (người dùng đang thao tác).
  - `interactive: false` cần quyền trong PermissionStore bảng `screenshot`, id `screenshot`, khoá là app id. Thử nghiệm ngày 2026-11-17: chụp không tương tác ghi file `~/Pictures/Screenshot.png`.
- **D-Bus của app**: tên `com.ubuntu_assistant.QuickShot` (GApplication, một instance).
- **Clipboard**: `wl-copy --type image/png`, dữ liệu qua stdin.

## Settings

Không có file cấu hình. Thay đổi trong code:

| Muốn đổi | Sửa | Tác động |
|---|---|---|
| Độ dày / cỡ chữ | `SIZES` | Áp dụng cho hình vẽ mới |
| Màu mặc định | `DEFAULT_COLOR` | Màu khi mở cửa sổ |
| Kích thước cửa sổ | `default_width/height` trong `_build` | Giới hạn 640×400 đến 1600×1000 |
| Phím gọi | `install.sh` (`binding 'Print'`) | Chạy lại `./install.sh` |

Phím tắt do `install.sh` đặt:
- `org.gnome.shell.keybindings show-screenshot-ui` = `['<Ctrl>Print']` (gốc: `['Print', '<Ctrl>Print']`).
- `custom-keybindings/quick-shot/`: `name 'Quick Shot'`, `command 'gjs -m ~/.local/share/quick-shot/quick-shot.js'`, `binding 'Print'`.

## UI

Thanh trên: `[Khung|Mũi tên|Chữ] [màu] [Mảnh/Vừa/Đậm ▾] [↶]  Quick Shot W×H  [Lưu…] [Copy]`. Vùng vẽ nền xám tối, ảnh ở giữa, con trỏ chữ thập. Lỗi Copy / Lưu hiện bằng toast.

## Lịch sử tính năng

| Ngày | Task | Thay đổi |
|---|---|---|
| 2026-11-17 | #35 | Bản đầu. Cùng ngày gỡ Flameshot (phím tắt, quyền portal; gói apt do người dùng gỡ) |
| 2026-11-17 | #36 | Công cụ chọn sẵn khi mở là Khung (trước là Mũi tên); thứ tự nút: Khung, Mũi tên, Chữ |

## Cấu trúc thư mục

```
quick-shot/
├─ quick-shot.js   # toàn bộ chương trình
├─ install.sh      # cài / gỡ, phím Print
└─ README.md       # hướng dẫn cho người dùng (repo public)
```

## Quy ước phát triển

- Giữ một file, không phụ thuộc thư viện ngoài gói mặc định của Ubuntu GNOME (trừ wl-clipboard).
- Chuỗi giao diện tiếng Việt có dấu.
- Kiểm thử phần vẽ bằng selftest; kiểm thử giao diện không làm phiền màn hình thật bằng Broadway: `gtk4-broadwayd :7` rồi `GDK_BACKEND=broadway BROADWAY_DISPLAY=:7 gjs -m quick-shot.js in.png`, mở `http://localhost:8087`.
- Sửa cách cài thì cập nhật `quick-shot/README.md` cùng lúc.

## Workflow xử lý issue

1. Xem log: `journalctl --user -b | grep -iE "quick-shot|portal"`.
2. Bấm Print không có gì: kiểm tra `gsettings list-recursively org.gnome.settings-daemon.plugins.media-keys.custom-keybinding:/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/quick-shot/` và chạy tay `gjs -m ~/.local/share/quick-shot/quick-shot.js` trong terminal để thấy lỗi.
3. Lỗi vẽ: tái hiện bằng selftest.
4. Sửa ở repo, chạy `./install.sh` để chép bản mới.

## Onboarding checklist cho session mới

1. Memory của project → `../index.md` → file này.
2. Cheatsheet:

| Cần làm | File / chỗ |
|---|---|
| Thêm công cụ vẽ | `TOOLS`, hàm `draw*`, `drawAnnotation`, nhánh trong `_dragBegin/_dragEnd` |
| Đổi cách chụp | `portalScreenshot`, `app.connect('activate')` |
| Đổi Copy / Lưu | `copy()`, `save()` |
| Đổi phím | `install.sh` |

3. Trạng thái runtime (2026-11-17): đã cài trên máy phát triển, Print đã gán. Phần vẽ đã kiểm thử (selftest + Broadway gọi hàm trực tiếp), đường portal đã kiểm thử ở chế độ không tương tác. Người dùng xác nhận trên máy thật: bấm Print, vẽ, Copy rồi dán chạy đúng.

## Architectural Decisions & Rationale

1. **Chọn vùng bằng giao diện của GNOME (portal interactive), không tự vẽ lớp phủ toàn màn hình như Lightshot.** Trên Wayland một cửa sổ toàn màn hình chỉ nằm trên một màn (lỗi Flameshot gặp phải); tự làm phải mở mỗi màn một cửa sổ và tự xử lý kéo vắt qua hai màn. Chụp không tương tác lại cần quyền PermissionStore theo app id, mà app id suy ra từ systemd scope nên khó đoán (xem Pitfall 1). Đổi lại: công cụ vẽ nằm ở cửa sổ riêng.
2. **Copy bằng `wl-copy`, không dùng `Gdk.Clipboard`.** Trên Wayland, clipboard do ứng dụng nguồn phục vụ; đóng cửa sổ ngay sau Copy thì dữ liệu mất. `wl-copy` tự chạy nền giữ dữ liệu.
3. **Ô nhập chữ là `Gtk.Entry` trong `Gtk.Overlay`, không dùng `Gtk.Popover`.** Popover là bề mặt riêng; khi thử trên Broadway, phím không vào được ô nhập trong popover. Ô nhập cùng bề mặt với vùng vẽ thì chắc chắn nhận phím.
4. **Lưu chú thích theo toạ độ ảnh gốc**, cửa sổ chỉ thu nhỏ khi hiển thị, nên ảnh xuất ra đúng độ phân giải.
5. **Print cho Quick Shot, Ctrl+Print cho giao diện gốc**: giữ đường dùng quay màn hình của GNOME.

## Common Pitfalls

1. **Portal từ chối chụp không tương tác.** App id của ứng dụng không sandbox được suy ra từ tên systemd scope (`app-gnome-<id>-<pid>.scope`). Chưa có quyền thì portal hiện hộp thoại hỏi, nhưng GNOME Shell chỉ cho ứng dụng đang được focus hiện hộp thoại → tự từ chối. Flameshot gặp đúng lỗi này ngày 2026-11-17 (`Unable to capture screen`). Quick Shot tránh bằng chế độ tương tác.
2. **`pkill -f quick-shot.js` trong shell của Claude giết luôn shell đang chạy lệnh** (dòng lệnh của shell chứa chuỗi đó). Dùng `pgrep -f "^gjs -m quick-shot.js"`.
3. **Broadway không nhận đúng thao tác chuột / phím giả lập** (toạ độ âm, kéo không có chuyển động). Đừng kết luận lỗi ứng dụng từ đó; kiểm thử bằng cách gọi hàm trực tiếp.

## Memory files & external resources

- Memory: `/home/luannt/.claude/projects/-home-luannt-works-claude-project-Ubuntu-Assistant/memory/`
- Bản cài: `/home/luannt/.local/share/quick-shot/`, `/home/luannt/.local/share/applications/com.ubuntu_assistant.QuickShot.desktop`
- Spec portal Screenshot: https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Screenshot.html
