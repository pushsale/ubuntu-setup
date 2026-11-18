# System HUD — Chi tiết kỹ thuật

Đọc xong file này là đủ để sửa System HUD mà không cần hỏi lại. Bối cảnh cho người không đọc code: [overview.md](overview.md).

## 1. Tổng quan, tech stack, cách chạy

System HUD là GNOME Shell extension, UUID `system-hud@ubuntu-assistant` (tên cũ: Sci-Fi HUD, `scifi-hud@ubuntu-assistant`).
- Đặt một `St.DrawingArea` vào `Main.layoutManager._backgroundGroup`, tức lớp hình nền.
- Mỗi lần repaint, vẽ toàn bộ bảng bằng Cairo theo theme đang chọn.
- Cấu hình bằng `config.json`; có cửa sổ cấu hình GTK riêng (`settings-app.js`), mở khi bấm biểu tượng HUD trên thanh trên cùng.
- Không có D-Bus, không cần quyền root.

Môi trường: Ubuntu 26.04, GNOME Shell 50.1 (Wayland), GPU NVIDIA GTX 1660 (`nvidia-smi` có sẵn trong driver).

Lệnh (chạy trong `system-hud/`):

```bash
./install.sh [theme]                   # chép file, giữ config.json đã có, nạp lại extension (không cần đăng xuất)
./hud-config.sh                        # xem cấu hình đang cài
./hud-config.sh theme orbitalLight     # đổi 1 khoá; HUD áp dụng ngay nhờ theo dõi file
./hud-config.sh monitor secondary
./hud-config.sh ui                     # mở cửa sổ cấu hình (giống bấm biểu tượng trên thanh trên cùng)
gjs -m tools/preview.js <thư-mục-ra> [giây] [hình-nền.png] [theme,...]   # xuất ảnh xem trước theme
gjs -m tools/anim-preview.js <thư-mục-ra> [theme] [intervalMs] [animMs] [fps]  # xuất khung hình hiệu ứng, đo thời gian vẽ
gnome-extensions info system-hud@ubuntu-assistant
journalctl -b --no-pager | grep system-hud
```

Lần đầu cài, hoặc sau khi đổi UUID, phải đăng xuất rồi đăng nhập lại: trên Wayland, shell đang chạy không nạp được UUID mới. Từ đó về sau chỉ cần chạy `./install.sh`.

## 2. Kiến trúc và pipeline

```
extension.js ── import('./core.js?v=<Date.now()>') ──► HudCore (core.js)
                                                         │
     enable(): loadConfig ─► new Sensors().start() ─► tạo St.DrawingArea(reactive=false) trong _backgroundGroup
               │                    │                     ├─ 'child-added' của group → _raise() (luôn trên ảnh nền)
               │                    │                     ├─ 'monitors-changed', 'workareas-changed' → _relayout()
               │                    │                     └─ Gio.FileMonitor config.json → nạp lại cfg, _relayout, đổi timer, bật/tắt biểu tượng
               │                    └─ nvidia-smi -lms 1000 (luồng, đọc dòng bất đồng bộ) → state.gpu
               └─ _syncIndicator: PanelMenu.Button trên thanh trên cùng ── bấm ──► gjs -m settings-app.js
                                                                                  └─ settings.js buildPage ── ghi ──► config.json
               ▼
     timer intervalMs (≥500) ─► sensors.update() ─► _onSample(): animator.push(state)
                                                      └─ timer 1000/fps ms chạy trong animMs: animator.frame(now) → queue_repaint
     'repaint' ─► drawHud(cr, w, h, displayState, theme)  (hud.js, hàm thuần)
                   ├─ scale k = min(w/480, h/neededH)
                   ├─ theme có draw() riêng (card) → tự vẽ toàn bộ
                   └─ còn lại: nền panel cache theo theme+kích thước → SECTIONS (header, CPU, MEM, GPU, DISK, NET) → footer → overlay
```

## 3. Luồng chi tiết (tham chiếu code)

Đường dẫn tính từ gốc repo.

| Bước | Code |
|---|---|
| Nạp core.js kèm mã phiên bản; core.js nạp tiếp sensors/hud/animator cùng mã đó | [extension.js](../../system-hud/system-hud@ubuntu-assistant/extension.js), [core.js:13-17](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Mặc định cấu hình (dùng chung core và cửa sổ cấu hình), đọc config.json | `DEFAULTS` [defaults.js](../../system-hud/system-hud@ubuntu-assistant/defaults.js), `loadConfig` [core.js:19](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Né dock trái tự ẩn (dash-to-dock không trừ vào work area) | `dockMargin` [core.js:31](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Gắn vào lớp hình nền, theo dõi màn hình và config | `enable` [core.js:51](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Biểu tượng trên thanh trên cùng, mở cửa sổ cấu hình | `_syncIndicator` [core.js:121](../../system-hud/system-hud@ubuntu-assistant/core.js), `_openSettings` 148 |
| Cửa sổ cấu hình: dựng trang, đọc/ghi config.json (trễ 300 ms) | `buildPage` [settings.js](../../system-hud/system-hud@ubuntu-assistant/settings.js); app riêng [settings-app.js](../../system-hud/system-hud@ubuntu-assistant/settings-app.js); Extensions app [prefs.js](../../system-hud/system-hud@ubuntu-assistant/prefs.js) |
| Hẹn giờ lấy mẫu | `_startTimer` [core.js:166](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Mẫu mới → chạy hiệu ứng | `_onSample` [core.js:178](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Chọn màn hình (`primary`/`secondary`/số) | `_monitorIndex` [core.js:240](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Tính vị trí/kích thước theo work area, mép, dock | `_relayout` [core.js:253](../../system-hud/system-hud@ubuntu-assistant/core.js) |
| Khởi tạo cảm biến (chọn file nhiệt độ, ổ gốc, danh sách ổ, GPU AMD) | `Sensors` constructor [sensors.js:76](../../system-hud/system-hud@ubuntu-assistant/sensors.js) |
| Một lần đo | `update` [sensors.js:152](../../system-hud/system-hud@ubuntu-assistant/sensors.js) |
| CPU / RAM / disk / net | `_readCpu` [sensors.js:183](../../system-hud/system-hud@ubuntu-assistant/sensors.js), `_readMem` 225, `_readDiskUsage` 254, `_readDisk` 269, `_findDisks` 309, `_readDisks` 374, `_readLinkSpeed` 394, `_readNet` 413 |
| NVIDIA: luồng `nvidia-smi` | `_startNvidia` [sensors.js:460](../../system-hud/system-hud@ubuntu-assistant/sensors.js), `_parseNvidia` 498 |
| Nội suy | `Animator` [animator.js:33](../../system-hud/system-hud@ubuntu-assistant/animator.js) |
| Vẽ | `drawHud` [hud.js:1509](../../system-hud/system-hud@ubuntu-assistant/hud.js), `SECTIONS` [hud.js:1494](../../system-hud/system-hud@ubuntu-assistant/hud.js); khối `drawCpu` 1352, `drawMem` 1371, `drawGpu` 1392, `drawDisk` 1415 (+ `diskCell` 1434), `drawNet` 1467 (+ `netCell` 1480, `rateFraction` 1458) |
| Theme | tactical [hud.js:226](../../system-hud/system-hud@ubuntu-assistant/hud.js), terminal 531, `makeOrbital` 792 → orbital 1059 / orbitalLight 1066, card 1073, `THEMES` 1326 |

Chi tiết từng bước:
- **Nhiệt độ CPU**: tìm trong `/sys/class/hwmon/*` có `name` là `coretemp`, `k10temp` hoặc `zenpower`, ưu tiên nhãn `Package id 0`, `Tctl`, `Tdie`.
- **Nhiệt độ ổ đĩa**: hwmon `nvme` nhãn `Composite`, hoặc `drivetemp`.
- **CPU**: % tính từ hiệu số `/proc/stat` giữa 2 lần đo; tần số lấy từ `/proc/cpuinfo`. `threadCount` = phần sau dấu `/` ở trường 4 của `/proc/loadavg` (tổng luồng của hệ thống); `procs` = số thư mục tên số trong `/proc`. `load[3]` vẫn đọc (theme `card` dùng) nhưng bố cục chung không hiện.
- **Danh sách ổ vật lý** (`_findDisks`, 10 tick một lần): duyệt `/proc/mounts`, bỏ `loop`/`ram`/`zram`/`sr`/`fd` và mount dưới `/boot`; `/dev/mapper/<tên>` đổi thành `dm-N` qua `/sys/block/dm-*/dm/name`; phân vùng → ổ cha bằng cách tìm trong `/sys/block/<ổ>/`, `dm-N` lần theo `slaves/`. Dung lượng = tổng `query_filesystem_info` của các phân vùng (mỗi phân vùng một lần dù mount nhiều chỗ). Model `/sys/block/<ổ>/device/model`; nhiệt độ `device/hwmon*/temp1_input` (NVMe) hoặc `device/hwmon/hwmon*/temp1_input` (drivetemp). Ổ chứa `/` đứng đầu.
- **% ổ bận** (`_readDisks`, mỗi tick): `/sys/block/<ổ>/stat` trường 10 `io_ticks` (ms có I/O) → `Δio_ticks / (Δt × 10)` kẹp 0–100, như "Active time" của Task Manager; đọc/ghi từ trường 3 / 7 (sector 512 byte). Mẫu đầu `active = null` (gauge hiện `--`).
- **Tốc độ link** (`_readLinkSpeed`, 30 tick một lần và khi đổi card): `/sys/class/net/<iface>/speed` (Mbps) → byte/s; lỗi hoặc ≤ 0 thì 0 (bố cục coi như 1 Gbps).
- **Lịch số đo**:
  - Dung lượng ổ: 10 tick một lần (`statvfs`).
  - IP: 30 tick một lần, chạy `ip -4 -o addr show dev <iface>`.
  - Card mạng: lấy từ route mặc định trong `/proc/net/route`.
- **GPU NVIDIA**:
  - Chạy `nvidia-smi --query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,power.limit,fan.speed,clocks.gr --format=csv,noheader,nounits -lms 1000` một lần và giữ tiến trình. Mỗi giây một dòng, đọc bằng `read_line_async`.
  - `stop()` huỷ việc đọc và `force_exit` tiến trình.
- **GPU AMD**: chỉ có % tải, đọc từ `/sys/class/drm/*/device/gpu_busy_percent`, và chỉ khi không có `nvidia-smi`.
- **Animator**:
  - Nội suy tuyến tính mọi số giữa mẫu cũ và mẫu mới, easing `easeOutCubic`.
  - Mảng lịch sử (khoá kết thúc bằng `history`/`Hist`) không nội suy; thay vào đó `state.shift` (1→0) làm đồ thị trượt.
  - Mảng đối tượng cùng độ dài (`disks`) nội suy từng phần tử; phần tử có `name` khác mẫu trước (thứ tự ổ đổi) thì lấy luôn giá trị mới.
  - `tick` lẻ dùng cho chuyển động trang trí.
  - Mẫu đầu tiên hiện ngay, không có hiệu ứng.

## 4. Data model

Không có database. `Sensors.state` là input duy nhất của `drawHud` (xem [sensors.js:81](../../system-hud/system-hud@ubuntu-assistant/sensors.js)):

```
{ tick, historySeconds, shift (do Animator thêm),
  sys:  {hostname, os, kernel, uptime},
  cpu:  {model, threads, usage, perCore[], temp, freq, freqMax, load[3], procs, threadCount, history[]},
  mem:  {total, used, percent, cached, swapTotal, swapUsed, swapPercent, history[]},
  gpu:  {available, name, util, temp, memUsed, memTotal, memPercent, power, powerLimit, fan, clock, history[]},
  disk: {mount:'/', device, total, used, percent, readRate, writeRate, readHist[], writeHist[], temp},   // chỉ theme card dùng
  disks: [{name:'nvme0n1', mount:'/ /data', device(model), total, used, percent, readRate, writeRate,
           active (% bận, null ở mẫu đầu), temp}],                                              // ổ chứa / đứng đầu
  net:  {iface, ip, rxRate, txRate, rxTotal, txTotal, linkSpeed (byte/s, 0 = không rõ), rxHist[], txHist[]} }
```

- Lịch sử giữ `HISTORY = 60` mẫu. Hằng số này khai báo 2 lần, ở [sensors.js:8](../../system-hud/system-hud@ubuntu-assistant/sensors.js) và [hud.js:7](../../system-hud/system-hud@ubuntu-assistant/hud.js), phải luôn khớp nhau.
- Đơn vị: byte, byte/giây, °C, %, MHz, W.
- Giá trị không đọc được là `null`; theme hiển thị `--`.

## 5. API / giao diện giữa các module

- **Theme object** (dùng bởi bố cục chung):
  - Vẽ khung: `panel(cr, W, H, s)`, `header(cr, x, y, w, h, s)`, `footer(...)`, `overlay(cr, W, H, s)`.
  - Thành phần: `section`, `label`, `value`, `stat`, `bar`, `gauge`, `graph`, `cores`; tuỳ chọn `item(cr, x, y, label, value, color)` (mục hai dòng, chỉ orbital có; theme khác ghép từ `label` + `value`).
  - `gauge(cr, cx, cy, r, pct, o)`: `o.temp`, `o.label`, `o.small` như cũ; thêm `o.color` (màu vòng), `o.text` + `o.noPct` (chữ số lớn), `o.top` + `o.topColor` (chữ nhỏ phía trên, mặc định nhiệt độ), `o.topLabel` (kiểu nhãn), `o.inner` + `o.innerColor` (cung/vòng trong, mặc định nhiệt độ; `null` = không có). Cả tactical, terminal, orbital đều nhận đủ các khoá này.
  - Màu theo mức: `level(p)` (bố cục gọi `T.level`).
  - Màu: `tempColor`, bảng màu `c`.
  - Theme có `draw(cr, W, H, s)` và `neededH` (hiện là `card`) thì tự vẽ toàn bộ và bỏ qua `SECTIONS`.
- **Theme mới**: thêm vào hud.js rồi đăng ký ở `THEMES`. `loadConfig` đổi tên theme không có trong `THEMES` thành `orbital`.
- **Không có API ngoài.** Giao tiếp duy nhất với bên ngoài là `config.json`. Theo dõi bằng `Gio.FileMonitor`, chỉ phản ứng với `CHANGES_DONE_HINT`/`CREATED`.
  - `hud-config.sh` ghi đè tại chỗ → `CHANGES_DONE_HINT`.
  - Cửa sổ cấu hình ghi bằng `GLib.file_set_contents` (file tạm rồi đổi tên) → `DELETED`, `CREATED`, `CHANGES_DONE_HINT`. Đã thử trên máy này.
- **Cửa sổ cấu hình** (`settings.js` `buildPage(dir)` → `Adw.PreferencesPage`): GTK 4 + libadwaita 1.9, không import gì của GNOME Shell. Mỗi lần đổi một ô thì chờ 300 ms rồi ghi; trước khi ghi đọc lại file để không đè khoá sửa từ nơi khác. App riêng có `application_id` `com.ubuntu_assistant.SystemHud.Settings` nên chỉ mở một cửa sổ.

## 6. Cấu hình và tác động khi đổi

File `~/.local/share/gnome-shell/extensions/system-hud@ubuntu-assistant/config.json`:
- `install.sh` giữ nguyên file này nếu đã có.
- Đổi bằng `hud-config.sh <khoá> <giá trị>` hoặc sửa tay; HUD áp dụng ngay.

| Khoá | Mặc định | Đang dùng | Tác động |
|---|---|---|---|
| `theme` | orbital | orbital | `tactical` / `terminal` / `orbital` / `orbitalLight` / `card`. Sai tên → orbital |
| `monitor` | primary | secondary | `primary`, `secondary` (màn đầu tiên không phải màn chính) hoặc số thứ tự. Đổi màn chính trong Settings thì `secondary` đổi theo |
| `side` | left | left | `left` / `right`. Bên trái màn chính tự cộng thêm chỗ cho dock tự ẩn |
| `widthFraction` | 0.25 | 0.25 | Tỉ lệ chiều rộng màn hình; chiều cao luôn bằng work area trừ `gap` |
| `gap` | 12 | 12 | Khoảng cách tới mép, top bar, dock |
| `intervalMs` | 5000 (trước 2026-11-16: 3000) | 5000 | Chu kỳ đo (tối thiểu 500). Giảm thì tốn CPU hơn và đồ thị 60 mẫu phủ ít thời gian hơn (5000 → 5 phút) |
| `animMs` | 5000 (2026-11-13: 3000; trước đó 900) | 5000 (chuyển liên tục suốt chu kỳ đo) | Thời gian hiệu ứng; `0` = tắt hiệu ứng (vẽ 1 lần mỗi mẫu). Cửa sổ cấu hình có công tắc "chuyển suốt cả chu kỳ" giữ `animMs` = `intervalMs` |
| `showIndicator` | true | true | Biểu tượng trên thanh trên cùng; bấm vào mở cửa sổ cấu hình. Tắt thì mở cửa sổ bằng `./hud-config.sh ui` |
| `fps` | 10 (trước 2026-11-13: 30) | 10 (người dùng chọn 2026-11-13. gnome-shell với hiệu ứng liên tục: 30 fps khoảng 43% một nhân, CPU giữ xung turbo ~72°C; 15 fps khoảng 31%; 5 fps khoảng 22% nhưng giật) | Số khung/giây khi đang chạy hiệu ứng |

Thiết lập ngoài cũng ảnh hưởng tới HUD:
- **Màn chính** (`~/.config/monitors.xml`): quyết định `primary`/`secondary`.
- **dash-to-dock** (`dock-position`, `dock-fixed`, `dash-max-icon-size`): quyết định khoảng né dock.

## 7. Giao diện

| Theme | Mô tả | Code |
|---|---|---|
| tactical | Khung cơ giáp cam/đen, lục giác, sọc cảnh báo | [hud.js:235](../../system-hud/system-hud@ubuntu-assistant/hud.js) |
| terminal | Màn CRT phốt-pho hổ phách, chữ đơn cách, scanline | hud.js:536 |
| orbital (đang dùng) | Giao diện tàu vũ trụ: nét trắng, điểm nhấn san hô/cyan, vòng đo (gauge) tròn cho CPU/GPU, nền xanh tím trong mờ | hud.js:796, 1044 |
| orbitalLight | Orbital nền trắng, chữ navy, thang màu đậm hơn | hud.js:1051 |
| card | Thẻ sáng kiểu ứng dụng GNOME, vòng donut xanh, bố cục riêng (`draw`) | hud.js:1058 |

- **Bố cục chung**, thiết kế cho bề rộng 480 px rồi co giãn:
  - header 74 px: tên máy, OS, kernel, giờ;
  - CPU 172 px: vòng đo r 58 (nhiệt độ trong vòng), cột phải từ `x + 150`: CLOCK, PROC / THR, từng luồng, đồ thị tải;
  - RAM 172 px: vòng % RAM (chữ trên = GB đã dùng), USED, AVAILABLE, CACHE, SWAP + thanh;
  - GPU 172 px: vòng tải (nhiệt độ trong vòng), POWER, FAN, CLOCK, VRAM + thanh;
  - Disk 186 px: mỗi ổ vật lý một ô (tối đa 2, chia đôi bề rộng): vòng = % ổ bận (cung trong + chữ trên = % dung lượng), READ / WRITE / USED, dòng dưới mount · model · nhiệt độ;
  - Network 172 px: hai ô DOWN / UP, vòng = % tốc độ link (dưới 5% link thì 5% đầu vòng theo tỉ lệ so với đỉnh lịch sử), PEAK / AVG / TOTAL; tiêu đề phụ thêm tốc độ link;
  - Bố cục các khối giống bản Windows (`system-hud-windows/src/Hud.cs`); khác nhau chỉ ở nhãn (`CACHE`/`SWAP` so với `CACHED`/`PAGEFILE`) và mount point thay cho ký tự ổ;
  - footer 22 px: uptime.
- **Màu theo mức tải/nhiệt**: `heat()`/`heatLight()` [hud.js:167](../../system-hud/system-hud@ubuntu-assistant/hud.js).
- **Ngày 2026-11-13** (#23): vòng đo GPU phóng to (r 44 → 58, tâm `x+68, top+68`), cỡ số trong vòng nhỏ tính theo bán kính (`r × 0.66`).
- **Ngày 2026-11-12**, theo yêu cầu "đỡ rối", đã bỏ khỏi `gauge()` của orbital:
  - thước 12 vạch mỗi 30°;
  - 2 cung trang trí xoay chậm bên ngoài.

  Giữ lại: vòng nền, cung giá trị, cung nhiệt độ bên trong.

## 8. Lịch sử tính năng

| Ngày | Thay đổi |
|---|---|
| 2026-11-12 | #1/#2: bảng theo dõi hệ thống, rồi Sci-Fi HUD với các theme tactical, terminal, orbital, card |
| 2026-11-12 | Animator (chuyển mượt, đồ thị trượt), cache nền panel, `orbitalLight`, theo dõi config.json, chọn màn hình/mép |
| 2026-11-12 | File khởi động mỏng; install.sh chờ INACTIVE/ACTIVE (sửa lỗi nằm ở INACTIVE sau khi cài lại) |
| 2026-11-12 | Gauge orbital bỏ thước vạch và 2 cung xoay ngoài |
| 2026-11-13 | #21: thử `animMs` = `intervalMs` = 3000 (hiệu ứng chạy suốt chu kỳ). #22: làm tròn GPU Clock/Fan (số nội suy hiện 10 chữ số thập phân). #23: vòng đo GPU r 44 → 58 |
| 2026-11-16 | #34: chu kỳ lấy số liệu và thời gian chuyển 3 s → 5 s; cửa sổ cấu hình (biểu tượng trên thanh trên cùng, `settings-app.js`, `prefs.js`); `defaults.js` dùng chung; `hud-config.sh ui` |
| 2026-11-18 | Bỏ dòng `TEMP` ở CPU, GPU (đã có trong vòng) và dòng `LOAD`. Port bố cục bản Windows (commit c668173): gauge r 58 cho mọi khối, `gauge` nhận tuỳ chọn chung, `item`; sensors thêm `disks` (ổ vật lý, % bận từ `io_ticks`), `net.linkSpeed`, `cpu.procs`/`threadCount`; animator nội suy mảng đối tượng. Không port font nhúng và `textScale` (chỉ để bản Windows khớp cỡ chữ Pango của bản này) |
| 2026-11-13 | #18: đổi tên Sci-Fi HUD → System HUD (UUID mới). Trên máy này đã gỡ hẳn UUID cũ và đăng nhập lại để nạp sạch; `install.sh` không giữ code chuyển đổi (bản cũ chưa từng được cài ở máy khác) |

## 9. Cấu trúc thư mục

```
system-hud/
├─ install.sh                  cài / nạp lại; [theme] đặt theme
├─ hud-config.sh               xem / đổi 1 khoá trong config.json đã cài
├─ system-hud@ubuntu-assistant/
│  ├─ metadata.json            uuid, name "System HUD", shell-version ["50"]
│  ├─ extension.js             file khởi động mỏng (gần như không sửa)
│  ├─ core.js                  HudCore: gắn vào shell, bố cục, timer, theo dõi config
│  ├─ sensors.js               đọc số liệu (không phụ thuộc gnome-shell, chạy được bằng gjs)
│  ├─ animator.js              nội suy giữa 2 mẫu
│  ├─ defaults.js              DEFAULTS dùng chung cho core.js và cửa sổ cấu hình
│  ├─ settings.js              trang cấu hình (Adw.PreferencesPage), đọc/ghi config.json
│  ├─ settings-app.js          cửa sổ cấu hình chạy riêng (gjs -m), mở từ biểu tượng trên thanh
│  ├─ prefs.js                 nút Cài đặt trong ứng dụng Extensions (dùng settings.js)
│  ├─ hud.js                   drawHud + 5 theme (Cairo/Pango, không phụ thuộc gnome-shell)
│  └─ config.json              cấu hình mặc định
└─ tools/
   ├─ preview.js               ảnh PNG xem trước mọi theme trên hình nền mô phỏng 1920x1080
   └─ anim-preview.js          xuất các khung hình của một lần chuyển số liệu + đo thời gian vẽ
```

## 10. Quy ước phát triển

- Comment và log bằng tiếng Việt; log có tiền tố `[system-hud]`.
- `sensors.js`, `animator.js`, `hud.js`, `defaults.js`, `settings.js` không được import gì của GNOME Shell (`resource:///org/gnome/shell/...`), để `tools/` chạy được bằng `gjs -m`.
- Thêm hoặc đổi module con: nạp kèm `VERSION_QUERY` trong core.js, nếu không bản cũ sẽ bị cache.
- **Sửa giao diện**:
  1. Xuất ảnh bằng `tools/preview.js` để xem trước.
  2. Chạy `./install.sh`.
  3. Nhìn trên máy thật.
  4. Nếu đổi hiệu ứng, đo bằng `tools/anim-preview.js`: thời gian vẽ mỗi khung phải nhỏ hơn hẳn 1000/fps ms.
- Không vẽ thứ không đổi theo khung hình vào đường vẽ chính; đưa vào `panel()` (được cache).
- `cr.$dispose()` sau mỗi lần vẽ (đã có trong handler repaint) để tránh rò bộ nhớ Cairo.

## 11. Workflow xử lý issue

1. `gnome-extensions info system-hud@ubuntu-assistant`: phải là ACTIVE. Nếu INACTIVE hay ERROR, xem `journalctl -b | grep -E "system-hud|JS ERROR"`.
2. HUD nằm sai màn hoặc sai vị trí: kiểm tra màn chính (Settings → Displays), `monitor`/`side` trong config, và cấu hình dock.
3. Thiếu số liệu GPU: chạy `nvidia-smi` bằng tay; thiếu nhiệt độ: `cat /sys/class/hwmon/*/name`.
4. Lỗi vẽ (`[system-hud] lỗi khi vẽ`): tái hiện ngoài shell bằng `gjs -m tools/preview.js /tmp/x 5 "" <theme>`.
5. Fix xong: thêm mục vào Common Pitfalls bên dưới.

## 12. Onboarding checklist cho session mới

Thứ tự đọc:
1. Memory: memory Claude Code của project (nằm ngoài repo, trên máy phát triển: `~/.claude/projects/<thư-mục-project>/memory/`)
2. [../index.md](../index.md)
3. File này
4. `core.js`, rồi `hud.js` (phần theme cần sửa)

Code path cheatsheet:

| Cần làm | File |
|---|---|
| Thêm / sửa số liệu | `sensors.js` (state + hàm `_read*`), rồi chỗ hiển thị trong `hud.js` (`drawCpu`…`drawNet` hoặc `card.draw`) |
| Sửa kiểu dáng một theme | `hud.js`, khối theme tương ứng |
| Thêm theme | `hud.js` + `THEMES`; thêm tên vào chú thích `DEFAULTS.theme` và `hud-config.sh` |
| Đổi chiều cao các khối | `SECTIONS` trong `hud.js` |
| Đổi vị trí / né dock / chọn màn | `core.js` `_relayout`, `_monitorIndex`, `dockMargin` |
| Đổi hiệu ứng | `animator.js`, `animMs`/`fps` |
| Thêm khoá cấu hình | `defaults.js`, chỗ dùng trong `core.js`, thêm ô trong `settings.js`, bảng ở mục 6 |
| Sửa cửa sổ cấu hình | `settings.js` (xem trước: dựng `buildPage` trong cửa sổ gjs rồi chụp bằng `Gtk.WidgetPaintable`) |
| Đổi cách cài | `install.sh` (thêm file mới vào danh sách `cp`) |

Lệnh hay dùng:

```bash
cd system-hud   # từ thư mục gốc repo
./install.sh && gnome-extensions info system-hud@ubuntu-assistant | grep State
./hud-config.sh
gjs -m tools/preview.js /tmp/hud-preview 10
```

Trạng thái runtime (2026-11-16):
- Đã cài `system-hud@ubuntu-assistant` và bật trong danh sách.
- UUID cũ `scifi-hud@ubuntu-assistant` đã gỡ hẳn (thư mục và danh sách bật).
- Cấu hình: orbital, màn phụ HDMI-3, mép trái, lấy số liệu và chuyển 5 s, 10 fps.
- `prefs.js` mới cài: ứng dụng Extensions chỉ hiện nút Cài đặt sau lần đăng nhập tới; biểu tượng trên thanh dùng được ngay.

## 13. Architectural Decisions & Rationale

| Quyết định | Lý do |
|---|---|
| Vẽ trong `_backgroundGroup` (lớp hình nền) | HUD luôn dưới mọi cửa sổ, không nhận chuột, không chiếm chỗ trên workspace; đi theo hình nền khi đổi màn hình |
| `_raise()` khi group có child mới | Đổi hình nền hoặc màn hình thì GNOME thêm actor nền mới lên trên, che mất HUD |
| Vẽ bằng Cairo trên một DrawingArea, không dựng cây St widget | Tự do tạo hình (gauge, chamfer, glow); dễ xuất ảnh xem trước ngoài shell |
| `hud.js` là hàm thuần `drawHud(cr, w, h, state, theme)` | Test và xem trước bằng gjs, không cần đăng nhập lại |
| Một tiến trình `nvidia-smi -lms 1000` chạy liên tục | Khởi động `nvidia-smi` mỗi lần đo tốn khoảng 100 ms CPU; chạy luồng thì rẻ và không chặn main loop |
| Animator nội suy + timer khung hình chỉ chạy trong `animMs` | Mượt mà vẫn nhẹ: khoảng 27 khung mỗi 3 giây, không vẽ liên tục |
| Cache nền panel theo `theme:w×h` | Phần khung tĩnh chiếm nhiều thời gian vẽ nhất |
| Cấu hình qua `config.json` + FileMonitor, không làm GSettings schema | Không phải biên dịch schema, đổi bằng một lệnh, áp dụng ngay |
| File khởi động mỏng `core.js?v=` | GJS cache ESM theo URL; tránh phải đăng xuất mỗi lần sửa |
| `install.sh` chờ trạng thái giữa disable và enable | `disable` bất đồng bộ; `enable` gửi sớm bị bỏ qua |
| Mở cấu hình bằng biểu tượng trên thanh trên cùng, không bấm thẳng lên HUD | Desktop Icons (`ding@rastersoft.com`, bật mặc định trên Ubuntu) đặt cửa sổ trong suốt phủ cả desktop, trên lớp hình nền, nên nhận hết cú bấm ở vùng HUD. Chen HUD lên trên cửa sổ đó thì phải tự xếp lại thứ tự mỗi khi cửa sổ đổi chồng: dễ vỡ |
| Cửa sổ cấu hình là app GTK riêng (`gjs -m settings-app.js`), `prefs.js` chỉ bọc lại | Shell chỉ biết extension có `prefs.js` từ lần đăng nhập nạp nó (`openExtensionPrefs` kiểm `hasPrefs`); app riêng chạy được ngay sau `install.sh`, và mở được từ terminal |
| `extension.js` không đổi khi thêm cửa sổ cấu hình | `extension.js` bị cache tới lần đăng nhập sau; mọi thứ mới nằm trong `core.js` và module nạp kèm `VERSION_QUERY` |
| Tên "System HUD" | Người dùng chọn ngày 2026-11-13 (thay "Sci-Fi HUD"); đổi tên cần hỏi trước |
| Bố cục các khối theo bản Windows (2026-11-18) | Chủ dự án yêu cầu bản Ubuntu đổi theo bản Windows. Một bố cục chung cho tactical / terminal / orbital, nên `gauge` của cả ba theme nhận cùng bộ tuỳ chọn thay vì thêm `diskGauge` / `valueGauge` / `rateGauge` riêng như Hud.cs |
| Vòng mạng tính tuyến tính theo tốc độ link, 5% đầu theo đỉnh lịch sử | Lấy theo bản Windows: thang log làm vài KB/s chiếm 1/4 vòng, dễ hiểu nhầm |
| % ổ bận từ `io_ticks` | Cùng nghĩa "Active time" của Task Manager (bản Windows dùng `100 - % Idle Time`) |

## 14. Common Pitfalls

| Vấn đề | Nguyên nhân | Cách xử lý |
|---|---|---|
| Cài lại xong HUD biến mất, extension INACTIVE | Bật lại khi shell chưa tắt xong extension | `install.sh` chờ INACTIVE rồi mới enable, chờ ACTIVE |
| Sửa code nhưng HUD vẫn chạy bản cũ | Cache ESM (trước khi có file khởi động mỏng), hoặc module con import không kèm `VERSION_QUERY` | Luôn import module con kèm `VERSION_QUERY` |
| HUD (và menu Ubuntu) nhảy sang màn khác | Màn chính bị đổi (monitors.xml lưu lại sau khi một màn mất tín hiệu thoáng qua); `monitor: secondary` tính theo màn chính | Đặt lại màn chính (DP-2) trong Settings → Displays hoặc qua `ApplyMonitorsConfig` |
| HUD bị hình nền che sau khi đổi wallpaper | Actor nền mới được thêm lên trên | Đã xử lý bằng `child-added` → `_raise()` |
| Đổi UUID: HUD mới không hiện ngay | Wayland không nạp extension mới khi shell đang chạy | Đăng xuất / đăng nhập; bản cũ vẫn chạy tới lúc đó |
| Xoá thư mục của extension đang chạy: extension biến mất, log `ImportError … core.js: No such file` | Tắt một extension thì GNOME tắt rồi bật lại các extension đứng sau nó trong danh sách; lúc bật lại, extension đã mất file nên nạp `core.js` thất bại (gặp khi đổi tên HUD rồi gỡ Allow Locked Remote Desktop) | Đổi tên / gỡ extension: bỏ khỏi `enabled-extensions` rồi mới xoá thư mục, sau đó đăng xuất và đăng nhập lại để nạp sạch; không giữ bản tạm |
| Số liệu hiện dạng `726.3962025246043 MHz` | Animator nội suy mọi số; chỗ hiển thị không làm tròn (lộ rõ khi `animMs` dài) | Làm tròn khi định dạng (`Math.round` cho Clock, Fan). Thêm số liệu mới: luôn định dạng bằng `toFixed`/`Math.round`/`fmtBytes` |
| Đổi `fps` trong config không có tác dụng | Khi `animMs >= intervalMs` timer khung hình chạy liên tục, không bao giờ tạo lại với fps mới | Theo dõi config: fps đổi thì huỷ và tạo lại timer khung hình (#24) |
| `nvidia-smi` báo GPU 30–37% khi HUD chạy hiệu ứng liên tục | % tải tính theo xung hiện tại; GPU đang ở xung thấp (≈405 MHz) nên việc nhỏ cũng hiện % cao | Xem công suất: bật/tắt HUD chỉ chênh ≈0,4 W (8,3 so với 7,9 W). Chi phí thật là CPU gnome-shell (đo 2026-11-13: tắt HUD 17%, hiệu ứng 0,9 s 25%, liên tục 30 fps 38%, 15 fps 29%, 10 fps 25%) |
| Gauge orbital rối mắt | Quá nhiều chi tiết trang trí | Đã bỏ thước vạch và cung xoay (2026-11-12); không thêm lại khi chưa hỏi |
| Bấm lên HUD không có tác dụng gì | Cửa sổ của Desktop Icons (ding) phủ lớp hình nền | Dùng biểu tượng trên thanh trên cùng hoặc `./hud-config.sh ui` |
| Gtk-WARNING `Failed to set text … from markup` ở cửa sổ cấu hình | `title`/`subtitle` của Adw row được hiểu là Pango markup; chuỗi có `<…>` hoặc `&` hỏng | Không dùng `<`, `>`, `&` trong chữ của row (hoặc thoát bằng `GLib.markup_escape_text`) |
| Log có `JS ERROR … grabbedsOldRect` | Lỗi của extension tiling-assistant | Không liên quan HUD |

## 15. Memory files & external resources

- Memory: memory Claude Code của project (nằm ngoài repo, trên máy phát triển: `~/.claude/projects/<thư-mục-project>/memory/`)
- Mã nguồn: `system-hud/` trong repo
- Extension đã cài: `~/.local/share/gnome-shell/extensions/system-hud@ubuntu-assistant/`
- Repo: `github.com/pushsale/ubuntu-setup` (public)
- Tham khảo: GJS guide (gjs.guide), Cairo/Pango API, `man nvidia-smi`, `Documentation/hwmon` của kernel
