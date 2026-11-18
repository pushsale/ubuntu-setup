# System HUD (Windows) — Tổng quan

Một bảng thông số hệ thống trong suốt, nằm cố định sát mép phải màn hình. HUD hiện trên hình nền desktop, luôn nằm dưới mọi cửa sổ, không nhận chuột và không hiện trên taskbar.

## Bài toán giải quyết

| Bài toán | Cách xử lý |
|---|---|
| Muốn nhìn nhanh tải máy (CPU, RAM, GPU, ổ đĩa, mạng) mà không mở Task Manager | Bảng vẽ thẳng lên desktop, cập nhật mỗi 3 giây |
| Bản gốc chỉ chạy trên Ubuntu (GNOME Shell extension) | Viết lại cho Windows, giữ theme Orbital (màu, font Noto, thang màu nhiệt); mỗi khối dùng vòng tròn cùng cỡ |
| Widget không được che công việc | Cửa sổ luôn nằm đáy, click xuyên qua, không chiếm focus |
| Số liệu nhảy giật cục | Nội suy mượt giữa hai lần lấy mẫu (3 giây, 10 khung/giây) |
| Cài đặt rườm rà | Một file `.exe` chạy ngay trên Windows 10/11, không cần cài runtime |

## Kiến trúc

```
 Windows (Win32 API, PDH counters, WMI, registry)   nvidia-smi   LibreHardwareMonitor (tuỳ chọn)
                 \                                      |               /
                  +------------- Sensors (luồng nền, mỗi 3 giây) -------+
                                        |
                                 HudState (ảnh chụp số liệu)
                                        |
                          Animator (nội suy, 10 khung/giây)
                                        |
                       Hud.Draw  ->  theme Orbital (GDI+)
                                        |
                DesktopWindow (cửa sổ layered, gắn vào desktop)
                                        |
                              Màn hình người dùng

          HudConfig (config.json, sửa là áp dụng ngay)   Khay hệ thống (menu chuột phải)
```

## Vai trò từng lớp

| Lớp | Vai trò | Công nghệ |
|---|---|---|
| Sensors | Đọc số liệu phần cứng và hệ điều hành | Win32 API, PDH, WMI, nvidia-smi |
| Animator | Làm mượt chuyển động giữa hai mẫu | C# thuần |
| Hud / Orbital | Vẽ bảng theo thiết kế Orbital | GDI+ (System.Drawing) |
| DesktopWindow | Hiển thị trong suốt trên desktop, giữ dưới mọi cửa sổ | Win32 layered window |
| HudApp | Điều phối, vị trí theo màn hình, menu khay | WinForms |
| HudConfig | Đọc/ghi cấu hình | JSON |

## Feature cung cấp gì cho từng đối tượng

- **Người dùng máy**: nhìn nhanh tình trạng máy ngay trên desktop; HUD tự chạy khi đăng nhập; đổi giao diện sáng/tối, trái/phải, màn hình chính/phụ, tắt tự khởi động từ menu khay.
- **IT / vận hành**: một file exe portable, cấu hình bằng `config.json`, không cần quyền admin.
- **Dev**: chế độ `--preview` xuất ảnh PNG bằng số liệu thật và `--bench` đo thời gian vẽ, để kiểm tra giao diện và hiệu năng mà không cần chạy HUD.

## Tính năng chính theo mốc thời gian

| Ngày | Nội dung |
|---|---|
| 2026-11-13 | Bản đầu: port theme Orbital và Orbital Light, 5 khối số liệu, cửa sổ desktop, khay hệ thống, hot-reload config |
| 2026-11-13 | Mặc định đặt bên phải; giữ HUD hiển thị khi bấm Win+D |
| 2026-11-13 | Theo repo gốc: hiệu ứng 3 giây chạy liên tục; giảm còn 10 khung/giây theo yêu cầu; tối ưu vẽ (CPU giảm từ khoảng 32% xuống khoảng 10% một nhân) |
| 2026-11-13 | Đọc nhiệt độ CPU, ổ đĩa, GPU từ LibreHardwareMonitor (qua WMI hoặc web server) |
| 2026-11-13 | Tự khởi động cùng Windows, mặc định bật (tắt trong menu khay hoặc `config.json`) |
| 2026-11-18 | Khối STORAGE thêm % thời gian ổ đang bận (giống cột Disk của Task Manager), bên cạnh % dung lượng đã dùng |
| 2026-11-18 | Khối STORAGE vẽ lại: mỗi ổ vật lý (tối đa 2) một vòng tròn kiểu CPU — số lớn là % ổ bận (disk IO), chữ nhỏ và vòng trong là % dung lượng; bên phải 3 dòng READ / WRITE / dung lượng đã dùng / tổng |
| 2026-11-18 | Khối CPU và GPU bỏ dòng TEMP bên phải vì nhiệt độ đã có trong vòng tròn |
| 2026-11-18 | Chữ dễ đọc như bản Ubuntu: nhúng bộ font Noto vào exe thay cho Segoe UI / Bahnschrift, chữ nhỏ vẽ khớp pixel và to hơn 10% (chỉnh bằng `textScale`). Vòng tròn CPU thu nhỏ bằng GPU và ổ đĩa; CLOCK, PROC / THR, THREADS và đồ thị tải CPU xếp gọn bên phải vòng tròn |
| 2026-11-18 | Khối NETWORK dạng hai vòng tròn DOWN / UP (vòng = % băng thông của link đang dùng), bên phải mỗi vòng có PEAK / AVG / TOTAL; tiêu đề hiện tốc độ link |
| 2026-11-18 | Khối MEMORY dạng vòng tròn như GPU (% RAM, GB đã dùng), bên phải USED / AVAILABLE / CACHED / PAGEFILE. Mọi khối giờ đều dùng vòng tròn cùng cỡ; bỏ đồ thị lịch sử mạng để HUD vừa màn 1080p |

## Tech stack

C# trên .NET Framework 4.8 (có sẵn trên Windows 10 1903+ và Windows 11), WinForms, GDI+, P/Invoke Win32, PDH, WMI. Build bằng .NET SDK (project kiểu SDK).

## Quy mô hiện tại

- Khoảng 2.100 dòng C#, 11 file nguồn; file chạy khoảng 840 KB (gồm khoảng 750 KB font Noto nhúng sẵn).
- Khi chạy: RAM khoảng 60–70 MB; CPU khoảng 10% một nhân ở 10 khung/giây (đo trên Xeon E5-2680 v3).
- Vẽ một khung hình: khoảng 5 ms ở kích thước 456×1016.

## Đặc điểm nổi bật

- Cùng theme Orbital với bản Ubuntu (màu, font Noto nhúng sẵn, thang màu nhiệt, hiệu ứng chuyển số). Bố cục riêng cho Windows: cả 5 khối đều có vòng tròn cùng cỡ; ổ đĩa và mạng mỗi thứ hai vòng.
- Đọc counter theo tên tiếng Anh (PDH), nên chạy đúng trên Windows mọi ngôn ngữ.
- `nvidia-smi` gắn vào Job object: HUD thoát hoặc crash thì tiến trình con cũng tắt theo.
- GPU không phải NVIDIA vẫn có tải 3D và VRAM, lấy từ bộ đếm GPU Engine như Task Manager.

## Giới hạn hiện tại

- **Nhiệt độ CPU**: Windows không có API chuẩn ở user mode. HUD hiển thị `--` trừ khi máy đang chạy **LibreHardwareMonitor** (bật Remote Web Server cổng 8085, hoặc bản có WMI), hoặc bo mạch có cảm biến ACPI (hiếm, và cần admin).
- **Nhiệt độ ổ đĩa**: cần LibreHardwareMonitor, hoặc chạy HUD bằng quyền admin.
- Không có load average như Linux; dòng `LOAD` được thay bằng `PROC / THR` (số tiến trình / số luồng).
- Biểu đồ luồng CPU chỉ hiển thị nhóm xử lý đầu tiên (tối đa 64 luồng).
- Khối STORAGE vẽ tối đa 2 ổ vật lý (ổ hệ thống trước); ổ không có ký tự ổ (chưa phân vùng, ổ ảo) không hiện. Tiêu đề phụ vẫn đếm đủ số ổ.
- HUD nằm trên lớp icon desktop: icon nằm sau HUD sẽ bị che, nhưng vẫn click được vì HUD cho chuột xuyên qua.
- Chưa port 3 theme còn lại của bản gốc (tactical, terminal, card).

## Roadmap (tạm dự kiến)

- Tuỳ chọn tích hợp thẳng thư viện LibreHardwareMonitorLib (cần chạy quyền admin qua Task Scheduler).
- Port các theme tactical / terminal / card nếu cần.
- Installer hoặc bản ký số nếu phát hành rộng.

## Tài liệu liên quan

- Chi tiết kỹ thuật: [system.md](system.md)
- Mục lục: [../index.md](../index.md)
- Hướng dẫn cài / dùng: [system-hud-windows/README.md](../../system-hud-windows/README.md)
- Bản gốc Ubuntu: [../system-hud/overview.md](../system-hud/overview.md)
