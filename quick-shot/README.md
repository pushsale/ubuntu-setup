# Quick Shot

Chụp một vùng màn hình bằng phím **Print**, vẽ chữ, mũi tên, khung chữ nhật lên ảnh rồi **Copy** vào clipboard hoặc **Lưu** thành file PNG. Gọn hơn Lightshot, chạy trên Ubuntu GNOME (Wayland).

## Cài

```bash
./install.sh
```

Cần `gjs`, GTK 4, libadwaita (có sẵn trên Ubuntu GNOME) và `wl-copy` (`sudo apt install wl-clipboard`).

Sau khi cài:

| Phím | Tác dụng |
|---|---|
| `Print` | Quick Shot: chọn vùng, rồi mở cửa sổ vẽ |
| `Ctrl+Print` | Giao diện chụp gốc của GNOME (có quay màn hình) |

## Dùng

1. Bấm **Print**, kéo chọn vùng (chọn được trên mọi màn hình), bấm nút chụp hoặc Enter.
2. Cửa sổ vẽ mở ra:

| Thao tác | Cách làm |
|---|---|
| Khung / Mũi tên | Chọn công cụ (phím `R` / `A`), kéo chuột trên ảnh. Mở ra là chọn sẵn Khung |
| Chữ | Chọn **Chữ** (phím `T`), bấm vào chỗ cần ghi, gõ, Enter. Esc để bỏ |
| Màu, độ dày | Ô màu và danh sách Mảnh / Vừa / Đậm trên thanh công cụ |
| Hoàn tác | `Ctrl+Z` |
| Copy | Nút **Copy** hoặc `Ctrl+C`: copy ảnh rồi đóng cửa sổ |
| Lưu | Nút **Lưu…** hoặc `Ctrl+S`: chọn nơi lưu, mặc định `~/Pictures/Screenshots` |
| Đóng không lưu | `Esc` |

Mở một ảnh PNG có sẵn để vẽ:

```bash
gjs -m ~/.local/share/quick-shot/quick-shot.js ảnh.png
```

## Gỡ

```bash
./install.sh uninstall
```

Phím Print trở lại giao diện chụp của GNOME.
