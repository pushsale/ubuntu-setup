# Quick Shot — Tổng quan

Bấm phím Print để chọn một vùng màn hình, vẽ chữ, mũi tên, khung chữ nhật lên ảnh, rồi copy vào clipboard hoặc lưu file PNG. Gọn hơn Lightshot, chạy trên Ubuntu 26.04 GNOME 50 Wayland.

## Bài toán giải quyết

| Bài toán | Cách xử lý |
|---|---|
| Phím Print của GNOME chọn được vùng nhưng không có công cụ vẽ, đóng khung | Sau khi chọn vùng, mở cửa sổ có công cụ Mũi tên / Khung / Chữ, màu, độ dày, hoàn tác |
| Flameshot trên Wayland chỉ phủ được một màn hình và hay bị từ chối quyền chụp | Dùng chính giao diện chọn vùng của GNOME (chọn được trên mọi màn hình); chụp kiểu tương tác nên không cần quyền lưu sẵn |
| Copy ảnh rồi đóng cửa sổ thì trên Wayland dán không ra | Copy qua `wl-copy`: tiến trình nền giữ dữ liệu clipboard sau khi cửa sổ đóng |

## Kiến trúc

```
 Phím Print (phím tắt tuỳ chỉnh của GNOME)
        │  gjs -m quick-shot.js
        ▼
 Quick Shot (một tiến trình duy nhất, D-Bus: com.ubuntu_assistant.QuickShot)
        │  xdg-desktop-portal Screenshot (interactive)
        ▼
 Giao diện chọn vùng của GNOME Shell ──► file PNG vùng đã chọn
        │
        ▼
 Cửa sổ vẽ (GTK 4 + libadwaita, Cairo)
        │                     │
   Copy (wl-copy)        Lưu (hộp thoại lưu file, PNG)
```

## Vai trò từng lớp

| Lớp | Vai trò | Công nghệ |
|---|---|---|
| Phím tắt | Print gọi Quick Shot; giao diện chụp gốc chuyển sang Ctrl+Print | gsettings, phím tắt tuỳ chỉnh GNOME |
| Chụp | Mở giao diện chọn vùng của GNOME, nhận lại file ảnh | xdg-desktop-portal qua D-Bus |
| Cửa sổ vẽ | Hiện ảnh, công cụ vẽ, Copy / Lưu | GJS, GTK 4, libadwaita, Cairo, Pango |
| Clipboard | Giữ ảnh sau khi cửa sổ đóng | `wl-copy` (wl-clipboard) |

## Cung cấp cho ai

- **Người dùng máy:** chụp, chú thích, gửi ảnh nhanh (dán vào chat, email, tài liệu).
- **Người cài máy mới:** một lệnh `./install.sh` là xong, gỡ bằng `./install.sh uninstall`.

## Tính năng theo mốc thời gian

| Ngày | Tính năng |
|---|---|
| 2026-11-17 | Bản đầu (#35): chọn vùng bằng GNOME, Mũi tên / Khung / Chữ, màu, 3 độ dày, hoàn tác, Copy, Lưu; phím Print |

## Tech stack

GJS 1.88 (JavaScript ESM), GTK 4, libadwaita 1.9, Cairo, Pango, xdg-desktop-portal, wl-clipboard. Không cần biên dịch.

## Quy mô hiện tại

Một file chương trình khoảng 520 dòng, một script cài. Chạy trên một máy (2 màn hình 1920×1080).

## Đặc điểm nổi bật

- Chọn vùng trên mọi màn hình, kể cả kéo vắt qua hai màn, vì dùng giao diện của GNOME.
- Chữ tiếng Việt có dấu, có viền mảnh để đọc được trên mọi nền.
- Ảnh xuất ra đúng độ phân giải gốc dù cửa sổ vẽ thu nhỏ ảnh cho vừa.

## Giới hạn hiện tại

- Công cụ vẽ nằm ở cửa sổ riêng mở ra sau khi chọn vùng, không nằm ngay trên màn hình đang chụp như Lightshot.
- Giao diện chụp của GNOME vẫn tự lưu ảnh gốc (chưa vẽ) vào `~/Pictures/Screenshots` và có thể copy ảnh gốc vào clipboard; Copy của Quick Shot sẽ ghi đè clipboard bằng ảnh đã vẽ.
- Chỉ hoàn tác, không sửa / di chuyển hình đã vẽ.
- Chỉ mở được ảnh PNG.

## Roadmap (tạm dự kiến)

- Thêm công cụ nếu cần: bút tự do, làm mờ vùng, đánh số.
- Cân nhắc đưa vào danh sách cài của `initial-setup`.

## Tài liệu liên quan

- [system.md](system.md): chi tiết kỹ thuật
- [../index.md](../index.md): mục lục tài liệu
- Hướng dẫn cài / dùng: `quick-shot/README.md` trong mã nguồn
- Liên hệ: chủ dự án; repo `github.com/pushsale/ubuntu-setup`
