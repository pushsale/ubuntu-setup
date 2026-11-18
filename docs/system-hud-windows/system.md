# System HUD (Windows) — Chi tiết kỹ thuật

## Tổng quan

Ứng dụng WinForms không có form: một cửa sổ layered (trong suốt từng pixel) gắn vào desktop, cùng một icon ở khay hệ thống. Cứ 3 giây, một luồng nền đọc số liệu vào `HudState`. `Animator` nội suy giữa hai mẫu, luồng UI vẽ 10 khung/giây bằng GDI+ vào một DIB section rồi đẩy lên màn hình bằng `UpdateLayeredWindow`.

Port từ extension GNOME `system-hud@ubuntu-assistant` (thư mục `system-hud/` cùng repo, tài liệu [../system-hud/system.md](../system-hud/system.md)). Mã nguồn Windows nằm ở `system-hud-windows/`; mọi đường dẫn `src/...` trong file này tính từ thư mục đó. Tương ứng giữa các file:

| Ubuntu | Windows |
|---|---|
| `sensors.js` (/proc, /sys, nvidia-smi) | `src/Sensors.cs`, `src/Pdh.cs`, `src/LhmHttp.cs` |
| `animator.js` | `src/State.cs` (`Animator`, `Interp`) |
| `hud.js` (`makeOrbital`, phần BỐ CỤC) | `src/Hud.cs` + `src/Gfx.cs` (thay Cairo/Pango) |
| `core.js` (St.DrawingArea trong background group) | `src/HudApp.cs` + `src/DesktopWindow.cs` |
| `config.json` + `hud-config.sh` | `src/HudConfig.cs` + menu khay |
| `tools/preview.js` | `SystemHud.exe --preview` |

### Tech stack

- C# (`LangVersion latest`), target **.NET Framework 4.8**, project kiểu SDK (`SystemHud.csproj`).
- Reference: `System.Drawing`, `System.Windows.Forms`, `System.Management` (WMI), `System.Web.Extensions` (JSON).
- Manifest `app.manifest`: PerMonitorV2 DPI, `asInvoker` (không cần admin).

### Cách chạy

```bash
cd system-hud-windows
# build (cần .NET SDK >= 5; .NET Framework 4.8 Developer Pack hoặc mạng để tải reference assemblies)
dotnet build -c Release
# hoặc build + gom vào dist/
build.cmd

# chạy
bin/Release/net48/SystemHud.exe

# ảnh xem trước bằng số liệu thật: --preview <png> [giây-lấy-mẫu=6] [theme=orbital] [rộng=456] [cao=1016]
bin/Release/net48/SystemHud.exe --preview out.png 5 orbitalLight

# đo thời gian vẽ: --bench <txt> [số-khung=150]
bin/Release/net48/SystemHud.exe --bench bench.txt 150

# log chẩn đoán z-order (Show Desktop): đặt biến môi trường trước khi chạy
set SYSTEMHUD_LOG=C:\temp\hud.log
```

Build sẽ lỗi `MSB3027 file is locked` nếu HUD đang chạy: tắt trước (`taskkill /IM SystemHud.exe /F`; `build.cmd` tự làm việc này).

## Kiến trúc + pipeline

```
luồng nền (System.Threading.Timer, one-shot, tự hẹn lần sau)
  Sensors.Update()                       Sensors.cs:107
    Init() lần đầu: PDH counters, ổ hệ thống (WMI), dò LHM, khởi động nvidia-smi
    đọc ngoài khoá: ReadHwTemps / ReadAcpiTemp / ReadStorageTemp / ReadPageFile (WMI, HTTP)
    trong khoá _gate: ReadCpu, ReadMem, ReadDisk, ReadNet, GPU fallback, uptime
  Sensors.Snapshot() -> HudState.Clone() (deep copy)
  _ui.BeginInvoke(OnSample)
                       |
luồng UI (message loop WinForms)
  OnSample (HudApp.cs:109): Animator.Push + Frame -> Render; bật _frameTimer (1000/fps ms)
  OnFrame  (HudApp.cs:122): Animator.Frame -> Render; tắt timer khi hiệu ứng xong
  Render   (HudApp.cs:132): Graphics.FromImage(Surface.Bitmap) -> Hud.Draw -> DesktopWindow.Present
  Hud.Draw (Hud.cs:460):
     nền panel cache vào bitmap theo key theme:w x h (gradient, vầng sáng, sao, dấu +)
     ScaleTransform(k), k = min(w/480, h/NeededH)  -> toạ độ thiết kế 480px như hud.js
     6 khối: Header, CPU, Memory, GPU, Storage, Network + Footer
  DesktopWindow.Present (DesktopWindow.cs:161): UpdateLayeredWindow(ULW_ALPHA) từ DIB 32bpp premultiplied
```

nvidia-smi chạy liên tục (`-lms 1000`) trên luồng đọc stdout riêng: `ParseNvidia` (Sensors.cs:569) ghi vào `_s.Gpu` dưới khoá `_gate`.

## Luồng chi tiết

### Lấy mẫu — `HudApp.Sample` (HudApp.cs:82)

- `Interlocked` chống chạy chồng.
- Mẫu đầu chưa có tải CPU và tốc độ (cần hai lần đọc để lấy hiệu số), nên mẫu thứ hai được lấy sau 1 giây; từ đó cách đều `IntervalMs`, **trừ đi thời gian đã đọc cảm biến**. Nhờ vậy `animMs = intervalMs` cho chuyển động liền mạch.

### Nguồn số liệu — `Sensors`

| Số liệu | Nguồn | Hàm |
|---|---|---|
| Tên máy, OS, kernel | `Environment.MachineName`, registry `Windows NT\CurrentVersion` (build >= 22000 thì đổi "Windows 10" thành "Windows 11") | `ReadOsInfo` |
| CPU tổng | `GetSystemTimes` (hiệu số idle / kernel+user) | `ReadCpu` Sensors.cs:155 |
| CPU từng luồng | `NtQuerySystemInformation(8)`; chỉ nhóm xử lý hiện tại (≤64) | `ReadPerCore` Sensors.cs:187 |
| Xung CPU | PDH `\Processor Information(_Total)\% Processor Performance` × `~MHz` registry (cách Task Manager tính) | `ReadCpu` |
| PROC / THR | `GetPerformanceInfo` (thay load average của Linux) | `ReadCpu` |
| RAM | `GlobalMemoryStatusEx`; Cached = Standby (Core+Normal+Reserve) + Modified Page List (PDH), giống Task Manager | `ReadMem` Sensors.cs:221 |
| Pagefile | WMI `Win32_PageFileUsage` (mỗi 10 mẫu) | `ReadPageFile` Sensors.cs:244 |
| Danh sách ổ | Mỗi ổ vật lý **có ký tự ổ** là một `DiskState` trong `HudState.Disks`. Lấy từ tên instance PDH `PhysicalDisk` dạng `0 C:`, `1 D: F:` (số ổ + ký tự ổ); instance không có ký tự (ổ ảo, chưa phân vùng) và `_Total` bị bỏ. Ổ hệ thống đứng đầu (số ổ tìm qua WMI `ASSOCIATORS OF` LogicalDisk→Partition→DiskDrive), còn lại theo số ổ | `ReadDisks` Sensors.cs:299, `FindSystemDisk` Sensors.cs:260 |
| Model ổ | WMI `Win32_DiskDrive` (Index → Model), đọc một lần khi khởi tạo | `FindSystemDisk` |
| Dung lượng | Tổng `DriveInfo` của các ký tự ổ trên ổ vật lý đó (mỗi 10 mẫu, cache theo ký tự trong `_volUsage`) | `ReadDisks` |
| Đọc/ghi đĩa | PDH `\PhysicalDisk(*)\Disk Read/Write Bytes/sec` | `ReadDisks` |
| IO (% ổ bận) | PDH `\PhysicalDisk(*)\% Idle Time`, lấy `100 - idle` kẹp 0–100 (cách Task Manager tính "Active time"). Không dùng `% Disk Time` vì cộng dồn hàng đợi, vượt 100 (đo được 505) | `ReadDisks` |
| Nhiệt độ ổ | LibreHardwareMonitor chỉ cho một số → gán cho ổ hệ thống; ổ khác lấy WMI `MSFT_StorageReliabilityCounter` theo `DeviceId` = số ổ (cần admin, lỗi một lần là tắt) | `ReadDisks`, `ReadStorageTemps` Sensors.cs:362 |
| Mạng | `GetBestInterface(8.8.8.8)` → `NetworkInterface` có IPv4 Index trùng → `GetIPStatistics` (64-bit) | `ReadNet` Sensors.cs:384 |
| Tốc độ link | `NetworkInterface.Speed` (bit/s, Wi-Fi là tốc độ kết nối hiện tại) / 8 → `NetState.LinkSpeed` byte/s; ≤ 0 thì 0 (không rõ) | `ReadNet` |
| GPU NVIDIA | `nvidia-smi -i 0 --query-gpu=... -lms 1000` | `StartNvidia` Sensors.cs:512 |
| GPU khác | PDH `\GPU Engine(*engtype_3D)\Utilization Percentage` (gom theo luid, lấy max), `\GPU Adapter Memory(*)\Dedicated Usage`; tên + VRAM tổng từ registry class display | `ReadGpuFallback` Sensors.cs:593, `FindGpuFromRegistry` Sensors.cs:613 |
| Nhiệt CPU/ổ/GPU | LibreHardwareMonitor: WMI `root\LibreHardwareMonitor` / `root\OpenHardwareMonitor`, hoặc HTTP `http://127.0.0.1:8085/data.json` | `ReadHwTemps` Sensors.cs:450, `LhmHttp.ReadTemperatures` LhmHttp.cs:19 |
| Nhiệt CPU dự phòng | WMI `root\WMI MSAcpi_ThermalZoneTemperature` (cần admin; bỏ nếu ngoài 0–125°C) | `ReadAcpiTemp` Sensors.cs:491 |

Nguồn nhiệt LHM: chưa tìm thấy thì cứ 10 mẫu dò lại một lần, để LHM bật sau HUD vẫn được nhận. Chọn nhiệt CPU theo thứ tự tên `CPU Package` → `Core (Tctl/Tdie)` → `Core (Tctl)` → `Core (Tdie)` → `Core Max` → `Core Average` → giá trị lớn nhất của nhóm cpu.

### Nội suy — `Animator` / `Interp` (State.cs)

- `Interp.Value` (State.cs:77) duyệt field bằng reflection: `double`/`double?` thì nội suy tuyến tính; `double[]` có tên kết thúc bằng `History`/`Hist` thì lấy mảng mới (phần trượt do `Shift` lo); mảng khác cùng độ dài thì nội suy từng phần tử; mảng đối tượng (`DiskState[]`) cùng độ dài thì nội suy từng phần tử, riêng `DiskState` khác `Mount` (thứ tự ổ đổi) thì lấy luôn giá trị mới; class con thì đệ quy; còn lại lấy giá trị mới.
- `Frame` (State.cs:149): easeOutCubic; `Tick = to.Tick - 1 + e` (đèn SYNC nhấp nháy); `Shift = 1 - e` (đồ thị trượt trái một bước).

### Vẽ — `Hud` / `Orbital` / `Gfx`

- Bố cục `Sections` (Hud.cs:443): cao 74 / 172 / 172 / 172 / 186 / 172 (gốc 74 / 242 / 128 / 176 / 152 / 178; bản Windows dùng gauge r 58 cho mọi khối nên mỗi khối cao khoảng 32 + 136; tổng phải giữ quanh 1040 để HUD trên màn 1080p không bị co — hiện `NeededH` = 1058, co về 98%), footer 22, pad 20, `DesignW = 480`. Khoảng trống còn dư chia đều giữa các khối, giống `drawHud` trong hud.js.
- Các hàm theme (Hud.cs): `Panel`:131, `Gauge`:205, `DiskGauge`:230, `ValueGauge`:255, `RateGauge`:274 (ba gauge sau là bản Windows thêm), `Graph`:310 (Bezier `(mx,py),(mx,qy)` như Cairo `curveTo`), `Cores`, `Stat`, `Big`, `Bar`, `Footer`.
- Khối: `DrawCpu`:512, `DrawMem`:533, `DrawGpu`:554, `DrawDisk`:581 (+ `DiskCell`), `DrawNet`:628 (+ `NetCell`).
- `Gfx` thay Cairo/Pango:
  - `Text` (Gfx.cs:188): căn trái/giữa/phải, valign top/middle/bottom/baseline, giãn ký tự (`spacing`) bằng cách vẽ từng ký tự. Chiều cao dòng = `LineSpacing/EmHeight × size`.
  - `DrawCached` (Gfx.cs:238): cache bitmap chữ theo `(chuỗi, font, màu, spacing, tỉ lệ k)`, dán 1:1 vào lưới pixel thiết bị; tối đa 800 mục, đầy thì xoá hết. Chỉ bật trong `Hud.Draw` (`Gfx.Scale = k`).
  - `LinearPad` (Gfx.cs:336): gradient tuyến tính kiểu Cairo `EXTEND_PAD`.
  - `StrokeArc`: góc radian chiều kim đồng hồ, 0 = hướng 3 giờ, giống `cairo.arc`.
- Font (`Fonts`, Gfx.cs): dùng đúng bộ Noto của bản Ubuntu, **nhúng trong exe**:
  - File ở `fonts/` (`NotoSans-Light`, `-Regular`, `-Medium`, `-SemiCondensedMedium`, `NotoSansMono-CondensedMedium`, bản hinted), nhúng bằng `EmbeddedResource` với tên `fonts.<file>.ttf`, nạp một lần qua `PrivateFontCollection.AddMemoryFont` (vùng nhớ `AllocCoTaskMem` không giải phóng vì GDI+ cần nó suốt đời tiến trình).
  - Vai trò → family: Thin = Noto Sans Light, Light = Noto Sans, Title = Noto Sans Medium, Label = Noto Sans SemiCondensed Medium, Mono = Noto Sans Mono Condensed Medium. `Find` tìm trong font nhúng trước, rồi font cài trên máy; dự phòng Segoe UI / Bahnschrift / Consolas.
  - Font đã cắt bằng fonttools còn Latin + tiếng Việt + dấu câu, mũi tên, hình học (`▲▼`), tổng khoảng 750 KB. Lệnh cắt: `python -m fontTools.subset <file> --unicodes="U+0020-007E,U+00A0-024F,U+0300-036F,U+1EA0-1EF9,U+2000-206F,U+20AB,U+2190-21FF,U+2212,U+25A0-25FF" --layout-features='*' --name-IDs='*' --name-languages='*'`. Nguồn: `github.com/notofonts/notofonts.github.io` (`fonts/<Family>/hinted/ttf/`), giấy phép SIL OFL ở `fonts/OFL.txt`. Thêm ký tự ngoài dải trên thì phải cắt lại, nếu không GDI+ vẽ ô vuông.
  - `TextRenderingHint.AntiAliasGridFit` (nét chữ nhỏ sắc hơn `AntiAlias` một chút).
  - `Fonts.TextScale` (config `textScale`, mặc định 1.1, kẹp 0.8–1.4): nhân cỡ cho chữ **dưới 18 px** (nhãn, số liệu, tiêu đề). Đo trên ảnh chụp bản Ubuntu thì chữ Pango to hơn GDI+ cùng cỡ px khoảng 10–15%. Chữ lớn (tên máy 26, giờ 40, số % trong vòng tròn) không nhân để không chạm nhau (thử nhân hết thì tên máy chạm đồng hồ).
  - Ảnh chữ trong cache vẽ với `PixelOffsetMode.None` (không dùng `Half` như hình học) và làm tròn vị trí từng ký tự về pixel thiết bị (`DrawRaw(..., snapK)`), để nét đã hinting không bị lệch nửa pixel thành nhoè. `TextContrast` 0 / 4 / 12 đã thử, khác biệt không đáng kể nên để mặc định.

### Cửa sổ desktop — `DesktopWindow`

- Style: `WS_POPUP`; ExStyle `LAYERED | TRANSPARENT (chuột xuyên qua) | TOOLWINDOW (không lên taskbar/Alt+Tab) | NOACTIVATE`.
- Chủ sở hữu (`GWLP_HWNDPARENT`) = `Progman`.
- `WM_WINDOWPOSCHANGING`: ép `hwndInsertAfter = HWND_BOTTOM`, trừ khi chính HUD đang tự đặt vị trí (`_placing`).
- Show Desktop (Win+D): Explorer đưa một `WorkerW` lên trên mọi cửa sổ thường. `CheckDesktopLayer` (DesktopWindow.cs:103) chạy mỗi 300 ms và mỗi khi focus đổi (`SetWinEventHook EVENT_SYSTEM_FOREGROUND`). Nếu thấy `WorkerW` hiển thị nằm trên HUD thì `PlaceAbove` (DesktopWindow.cs:137) đặt HUD ngay trên nó; khi lớp đó hạ xuống thì trả HUD về đáy.
- `TaskbarCreated` (Explorer khởi động lại): gắn lại vào Progman mới và relayout.
- `WM_DISPLAYCHANGE` / `WM_SETTINGCHANGE` / `WM_DPICHANGED` / `SystemEvents.DisplaySettingsChanged` → `Relayout`.
- `Surface`: DIB section top-down 32bpp, bọc bằng `Bitmap(..., Format32bppPArgb, bits)`: GDI+ vẽ thẳng vào bộ nhớ mà `UpdateLayeredWindow` đọc, không copy.

### Vị trí — `HudApp.Relayout` (HudApp.cs:167)

`width = round(screen.Width × widthFraction) - 2×gap`; trái: `wa.Left + gap`, phải: `wa.Right - width - gap`; cao từ `wa.Top + gap` tới `wa.Bottom - gap`. `wa` = `Screen.WorkingArea` (đã trừ taskbar). Màn hình: `"primary"`, `"secondary"` (màn hình đầu tiên không phải primary), hoặc số thứ tự trong `Screen.AllScreens`.

## Data model

`HudState` (State.cs), các field public:

```
HudState { Tick, Shift, Sys, Cpu, Mem, Gpu, Disk, Net }
SysState  { Hostname, Os, Kernel, Uptime(s) }
CpuState  { Model, Threads, Usage%, PerCore%[], Temp°C?, Freq GHz?, Procs, ThreadCount, History%[60] }
MemState  { Total, Used, Percent, Cached, SwapTotal, SwapUsed, SwapPercent (byte), History[60] }
GpuState  { Available, Name, Util%, Temp?, Power W?, PowerLimit W?, Fan%?, Clock MHz?, MemUsed, MemTotal, MemPercent, History[60] }
DiskState { Mount "C:", Device (model), Total, Used, Percent, ReadRate, WriteRate (B/s), Temp?, ReadHist[60], WriteHist[60] }
NetState  { Iface, Ip, RxRate, TxRate (B/s), RxTotal, TxTotal (byte), RxHist[60], TxHist[60] }
```

`Sensors.HISTORY = 60` mẫu. Thêm field mới: chỉ cần khai báo public, `Interp` tự xử lý. Mảng lịch sử phải đặt tên kết thúc bằng `History` hoặc `Hist`.

## API / giao thức

Không có API ra ngoài. Giao tiếp vào:
- nvidia-smi: CSV `name, utilization.gpu, temperature.gpu, memory.used(MiB), memory.total(MiB), power.draw, power.limit, fan.speed, clocks.gr`; giá trị `[N/A]` → null → hiển thị `--`.
- LHM HTTP `data.json`: cây node `{Text, Value "45.0 °C", ImageURL, SensorId?, Children[]}`; loại phần cứng suy ra từ `ImageURL` của node cha nếu không có `SensorId`.

## Settings — `config.json`

Vị trí: cạnh `SystemHud.exe`; thư mục đó không ghi được (vd Program Files) thì dùng `%APPDATA%\SystemHud\config.json`. Chưa có file thì tự tạo với giá trị mặc định. Sửa file là áp dụng ngay (FileSystemWatcher, debounce 300 ms). JSON lỗi thì dùng mặc định cho tới lần lưu đúng sau.

| Khoá | Mặc định | Tác động |
|---|---|---|
| `theme` | `orbital` | `orbital` (tối) hoặc `orbitalLight` (sáng); tên khác bị bỏ qua |
| `monitor` | `primary` | `primary` / `secondary` / số thứ tự màn hình |
| `side` | `right` | `left` / `right` (bản Ubuntu mặc định `left`) |
| `widthFraction` | `0.25` | Rộng theo tỉ lệ màn hình (0.1–1); chữ tự co giãn theo `k` |
| `gap` | `12` | Khoảng cách tới mép / taskbar (px) |
| `intervalMs` | `3000` | Chu kỳ lấy mẫu (tối thiểu 500) |
| `animMs` | `3000` | Thời gian hiệu ứng; bằng `intervalMs` = chuyển liên tục; `0` = tắt hiệu ứng, chỉ vẽ khi có mẫu |
| `fps` | `10` | Khung/giây trong lúc hiệu ứng; CPU tỉ lệ thuận (khoảng 1% một nhân cho mỗi fps) |
| `autostart` | `true` | Mỗi lần chạy / đổi config, HUD đồng bộ `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SystemHud` = đường dẫn exe đang chạy (`ApplyAutostart` trong HudApp.cs); `false` thì xoá mục đó. File config cũ thiếu khoá này được ghi bổ sung khi nạp |

## UI

- Bảng HUD: header (tên máy, giờ, ngày, OS · uptime, kernel), 01 CPU, 02 MEMORY, 03 GPU, 04 STORAGE, 05 NETWORK, footer "ORBITAL SYSTEMS INTERFACE · SYNC".
- Khay hệ thống (chuột phải icon): giao diện tối/sáng, trái/phải, màn hình chính/phụ, khởi động cùng Windows (bật/tắt khoá `autostart` trong config), mở `config.json` bằng Notepad, thoát.

## Lịch sử tính năng

| Ngày | Nội dung |
|---|---|
| 2026-11-13 | Port bản đầu (theme Orbital / Orbital Light), cửa sổ desktop, khay, hot-reload config |
| 2026-11-13 | Mặc định `side = right`; theo dõi lớp WorkerW để HUD hiện khi Show Desktop |
| 2026-11-13 | Đồng bộ commit gốc `9302f35` (`animMs` 3000); `fps` 10; lấy mẫu cách đều; tối ưu vẽ (AssumeLinear + cache chữ): 15 → 5 ms/khung |
| 2026-11-13 | Nhiệt độ từ LibreHardwareMonitor qua HTTP `data.json`, dò lại định kỳ |
| 2026-11-13 | Tự khởi động cùng Windows mặc định bật (khoá `autostart`), trước đó phải tự bật trong menu khay |

## Cấu trúc thư mục

```
docs/system-hud-windows/ tài liệu (overview.md, file này)
system-hud-windows/
├─ README.md             hướng dẫn cài / dùng / gỡ cho người dùng
├─ SystemHud.csproj      net48, WinExe
├─ app.manifest          PerMonitorV2 DPI, asInvoker
├─ build.cmd             build + gom vào dist/ (dist/, bin/, obj/ không commit)
├─ fonts/                font Noto nhúng vào exe (đã cắt Latin + tiếng Việt) + OFL.txt (giấy phép)
└─ src/
   ├─ Program.cs         Main, single instance, --preview, --bench
   ├─ HudApp.cs          điều phối, timer, vị trí, khay hệ thống
   ├─ DesktopWindow.cs   Surface (DIB) + cửa sổ layered gắn desktop, xử lý Win+D
   ├─ Hud.cs             theme Orbital, bố cục, Heat, Fmt
   ├─ Gfx.cs             lớp vẽ trên GDI+ (chữ, cung, gradient, cache chữ), Fonts
   ├─ State.cs           HudState + Interp + Animator
   ├─ Sensors.cs         thu thập số liệu
   ├─ Pdh.cs             wrapper PDH (tên counter tiếng Anh)
   ├─ LhmHttp.cs         đọc nhiệt từ web server LibreHardwareMonitor
   ├─ HudConfig.cs       config.json
   └─ Native.cs          P/Invoke
```

## Quy ước phát triển

- Comment tiếng Việt, giải thích **vì sao**, giống code bản Ubuntu.
- Toạ độ vẽ giữ nguyên số của `hud.js` để dễ đối chiếu khi bản gốc đổi; khác biệt cố ý thì ghi comment.
- Không dùng `PerformanceCounter` (tên bị bản địa hoá), mà dùng `Pdh.Add` với tên tiếng Anh.
- Mọi lệnh gọi WMI/HTTP đặt ngoài khoá `_gate` và bọc try/catch; nguồn hỏng thì tắt nguồn đó (cờ `_acpiOk`, `_storageTempOk`…), không để lỗi lặp lại mỗi mẫu.
- Đổi gì ở phần vẽ thì chạy `--preview` (so ảnh) và `--bench` (thời gian vẽ không được tăng đáng kể).

## Workflow xử lý issue

1. Lỗi số liệu: chạy `--preview` để xem số đọc được, đối chiếu Task Manager.
2. Lỗi giao diện: `--preview` cho cả hai theme, so với ảnh bản Ubuntu.
3. Lỗi z-order / Show Desktop: đặt `SYSTEMHUD_LOG`, chạy HUD, tái hiện, đọc log `PlaceAbove` / `desktop layer lowered`.
4. Lỗi hiệu năng: `--bench` có số ms của từng khối.
5. Sửa xong: thêm vào Common Pitfalls nếu là bẫy mới, cập nhật lịch sử tính năng.

## Onboarding checklist cho session mới

1. Đọc: memory của project → [../index.md](../index.md) → file này.
2. Code path cheatsheet:

| Cần làm | File |
|---|---|
| Thêm / sửa số liệu | `src/Sensors.cs` (+ field trong `src/State.cs`) |
| Sửa giao diện, toạ độ, màu | `src/Hud.cs` |
| Sửa cách vẽ chữ, gradient, cung | `src/Gfx.cs` |
| Sửa vị trí, timer, menu khay | `src/HudApp.cs` |
| Sửa hành vi cửa sổ (z-order, Win+D, click xuyên) | `src/DesktopWindow.cs` |
| Thêm khoá cấu hình | `src/HudConfig.cs` (Load + Save + bảng Settings ở trên) |
| Thêm P/Invoke | `src/Native.cs` |

3. Lệnh hay dùng: `taskkill /IM SystemHud.exe /F`, `dotnet build -c Release`, `--preview`, `--bench` (xem "Cách chạy").
4. Trạng thái runtime: chạy từ `bin/Release/net48/SystemHud.exe` hoặc `dist/SystemHud.exe`, config nằm cạnh exe. Không có test tự động; kiểm thử bằng `--preview` / `--bench` và quan sát trực tiếp.

## Đồng bộ với bản Ubuntu

Bản Ubuntu nằm cùng repo (`system-hud/`). Khi có commit mới, xem `git log -- system-hud` và ánh xạ theo bảng ở mục Tổng quan; sửa cả hai bản trong cùng commit nếu thay đổi áp dụng chung (vd mặc định `animMs`, `fps`). Khác biệt cố ý so với bản Ubuntu:

- `side` mặc định `right` (yêu cầu của chủ dự án cho Windows; bản Ubuntu `left`).
- `autostart` (chỉ có ở Windows; Ubuntu do GNOME bật extension).
- Dòng `LOAD` → `PROC / THR`; `SWAP` → `PAGEFILE`; `CACHE` → `CACHED`.
- NETWORK vẽ khác bản gốc (bản gốc: hai số lớn DOWNLINK / UPLINK + đồ thị gương cao 74 + dòng RX / SCALE / TX). Bản Windows (`DrawNet`, `NetCell`, `Orbital.RateGauge` trong Hud.cs; yêu cầu chủ dự án 2026-11-18):
  - Hai ô DOWN / UP (cách nhau 16), mỗi ô một gauge r 58: vòng = **% băng thông đang dùng, tuyến tính** theo tốc độ link (không rõ link thì 125 MB/s = 1 Gbps). Lúc dùng bình thường vòng gần rỗng, chỉ đầy khi tải nặng. Đã thử thang log (1 KB/s → link) nhưng vài KB/s đã chiếm khoảng 1/4 vòng, dễ hiểu nhầm, chủ dự án chọn tính đúng tỉ lệ (2026-11-18). Màu: down = `C.Main`, up = `C.Accent`.
  - Trong vòng: nhãn `▼ DOWN` / `UP ▲` (màu của vòng), số tốc độ lớn (thu 14% khi dài hơn 3 ký tự), đơn vị bên dưới.
  - Bên phải mỗi vòng: PEAK (max lịch sử), AVG (trung bình lịch sử; lịch sử = `HISTORY` 60 mẫu), TOTAL (byte từ lúc card bật).
  - Tiêu đề phụ thêm tốc độ link (`· 72 Mbps`). Bỏ đồ thị lịch sử và dòng RX / SCALE / TX (đồ thị bỏ để nhường chiều cao cho vòng tròn MEMORY). Khối cao 172.
- MEMORY vẽ khác bản gốc (bản gốc: hàng USED / % / TOTAL + thanh + CACHE / SWAP). Bản Windows (`DrawMem`, `Orbital.ValueGauge`; yêu cầu chủ dự án 2026-11-18): bố cục như GPU — gauge r 58 (số lớn = % RAM, chữ nhỏ trên = GB đã dùng màu theo thang nhiệt, nhãn `RAM USED`); cột phải từ `x + 150`: USED, AVAILABLE (tổng − đã dùng), CACHED, PAGEFILE (màu cảnh báo khi > 50%) + thanh % pagefile. Khối cao 172. GPU rút từ 176 xuống 172 cho đều.
- Khối CPU (theo yêu cầu chủ dự án 2026-11-18): bỏ dòng `TEMP` bên phải (nhiệt độ đã hiện trong vòng tròn); vòng tròn cỡ GPU (r 58, `small`) thay vì r 70; cột phải từ `x + 150`, gọn trong chiều cao vòng tròn: CLOCK, PROC / THR, THREADS (cao 38), đồ thị tải CPU ngay dưới (đáy ở `top + 134`, ngang đáy vòng tròn). Khối cao 172 thay vì 242. (Đã thử đồ thị chạy hết bề ngang dưới vòng tròn: cách xa phần trên và sát khối MEMORY, chủ dự án không chọn.) Khối GPU cũng bỏ dòng `TEMP`; POWER / FAN / CLOCK giãn cách 28 (gốc 21), VRAM cách 32, để giữ nguyên chiều cao khối.
- STORAGE vẽ khác hẳn bản gốc (bản gốc: một ổ `/`, hàng USED / % / TOTAL + thanh + READ / WRITE / TEMP + đồ thị). Bản Windows (`DrawDisk` Hud.cs:581, `DiskCell` Hud.cs:602, `Orbital.DiskGauge` Hud.cs:230):
  - Mỗi ổ vật lý một ô, tối đa 2 ô chia đôi bề rộng (cách nhau 16, mỗi ô rộng khoảng 200). Tiêu đề phụ: số ổ · tổng đã dùng / tổng dung lượng.
  - Gauge bên trái ô, cùng cỡ gauge GPU (r 58, chữ số lớn `r × 0.66`), cùng kiểu gauge CPU: **số lớn + vòng ngoài = % disk IO** (% thời gian ổ bận), nhãn `DISK IO`. Vòng trong mảnh + chữ nhỏ trên số lớn = % dung lượng đã dùng (vị trí nhiệt độ ở gauge CPU), màu theo thang nhiệt.
  - Bên phải gauge 3 mục READ, WRITE, USED, mỗi mục nhãn ở trên, số ở dưới (chỗ còn lại chỉ khoảng 70 px, không đủ cho nhãn và số trên cùng dòng). USED = `đã dùng/tổng`, cùng đơn vị theo tổng qua `Fmt.SizePair` (TB lấy 1 chữ số lẻ cho vừa), màu theo % dung lượng.
  - Dòng dưới cùng: ký tự ổ · model (cắt bớt cho vừa ô) · nhiệt độ nếu có.
  - Bỏ đồ thị đọc/ghi. Khối cao 186.
  - Quyết định bố cục theo yêu cầu chủ dự án (2026-11-18): % disk IO là số chính; dung lượng GB đưa ra ngoài vòng tròn; vòng tròn cỡ bằng GPU.
  - % ổ bận bên Linux lấy được từ `io_ticks` trong `/proc/diskstats` nếu muốn port ngược.
- Chỉ có theme `orbital` và `orbitalLight`.

Chưa đồng bộ (chờ chủ dự án quyết, 2026-11-16): bản Ubuntu đã đổi `intervalMs`/`animMs` mặc định 3000 → 5000 và có cửa sổ cấu hình (#34); bản Windows vẫn 3000 và chỉ cấu hình bằng sửa `config.json`.

## Architectural Decisions & Rationale

| Quyết định | Lý do |
|---|---|
| .NET Framework 4.8 thay vì .NET 8/10 | Có sẵn trên mọi Windows 10/11 nên exe chạy ngay, không bắt cài runtime; self-contained .NET hiện đại nặng trên 100 MB vì WinForms không trim được |
| GDI+ thay vì WPF / Direct2D / Electron | Ánh xạ gần 1:1 với Cairo của bản gốc (path, arc, Bezier, gradient), ít phụ thuộc, nhẹ |
| Cửa sổ layered + `UpdateLayeredWindow` trên DIB section dùng chung với `Bitmap` | Trong suốt từng pixel có anti-alias; không copy buffer mỗi khung |
| Owner = Progman + ép `HWND_BOTTOM` | Tương đương "background group" của GNOME: luôn dưới mọi cửa sổ, không lên taskbar |
| Không reparent vào WorkerW (kiểu wallpaper engine) | Layered child trong WorkerW kém ổn định; cách owner + dò z-order đơn giản và đủ |
| Dò z-order mỗi 300 ms cho Win+D, không chỉ dựa vào hook | Đã thử chỉ dùng `EVENT_SYSTEM_FOREGROUND`: Show Desktop không phải lúc nào cũng đổi foreground, nên HUD bị che |
| PDH `PdhAddEnglishCounter` | `PerformanceCounter` dùng tên bản địa hoá, hỏng trên Windows không phải tiếng Anh |
| Nhiệt độ qua LibreHardwareMonitor bên ngoài, không nhúng driver | Đọc nhiệt CPU cần kernel driver và quyền admin; driver WinRing0 từng bị Defender gắn cờ. HUD giữ chạy không cần admin |
| `CompositingQuality.AssumeLinear` | `HighQuality` (gamma-correct) chậm khoảng 4 lần; Cairo cũng trộn tuyến tính nên hình giống bản gốc hơn |
| Cache ảnh chữ | `DrawString` của GDI+ đắt; cache giảm từ 7,8 xuống 5,2 ms/khung |
| nvidia-smi trong Job object `KILL_ON_JOB_CLOSE` | HUD bị kill / crash thì không để lại tiến trình nvidia-smi mồ côi |

## Common Pitfalls

- **`CompositingQuality.HighQuality` làm vẽ chậm khoảng 4 lần** (20 ms/khung, khoảng 32% một nhân ở 15 fps). Giữ `AssumeLinear`.
- **Win+D che HUD**: Explorer đưa `WorkerW` lên vị trí khoảng 60 trong z-order, trên HUD. Owner = Progman không đủ (Progman không được nâng, WorkerW mới được nâng). Xem `CheckDesktopLayer`.
- **Chụp màn hình kiểm thử không thấy HUD**: `Graphics.CopyFromScreen` dùng BitBlt không có `CAPTUREBLT` nên bỏ qua cửa sổ layered. Phải BitBlt với `0x00CC0020 | 0x40000000`.
- **PowerShell `$null` → chuỗi rỗng** khi gọi P/Invoke `FindWindow($null, ...)`: trả 0. Gọi qua C# helper hoặc dùng `[NullString]::Value`.
- **Build lỗi MSB3027**: exe đang chạy khoá file; tắt HUD trước.
- **Mẫu đầu toàn số 0** (CPU, tốc độ): cần hai lần đọc; đã xử lý bằng lấy mẫu thứ hai sau 1 giây.
- **`ProductName` của Windows 11 vẫn ghi "Windows 10"**: dựa vào `CurrentBuild >= 22000`.
- **Máy nhiều luồng** (48+): vạch luồng thu nhỏ theo bề rộng (`lw = min(4, cw×0.6)`); quá 64 luồng chỉ thấy nhóm đầu.
- **HUD không tự chạy sau khi khởi động lại máy** (bản đầu): tự khởi động là tuỳ chọn tắt mặc định. Nay mặc định bật qua `autostart`; mục Run trỏ tới exe đang chạy, nên chạy từ `dist\` thì Run trỏ sang `dist\`.
- **Chữ mảnh, khó đọc hơn bản Ubuntu** (bản đầu): máy Windows thường không có Noto Light / Medium / SemiCondensed / Mono Condensed nên rơi về Segoe UI / Bahnschrift, nét mảnh và x-height thấp hơn. Đã sửa bằng cách nhúng font vào exe; đừng bỏ thư mục `fonts/` khỏi build. Sau đó vẫn hơi nhoè do vẽ chữ với `PixelOffsetMode.Half` và ký tự giãn chữ đặt ở toạ độ lẻ; đã sửa thêm và tăng cỡ chữ nhỏ 10% (`textScale`).
- **`% Disk Time` vượt 100%** (đo được 505 khi ổ bận hết): counter cộng dồn độ dài hàng đợi. Dùng `100 - % Idle Time`.
- **Thêm field kiểu mảng đối tượng vào `HudState`**: `Interp` cũ gọi `Activator.CreateInstance` trên kiểu mảng → crash. Nay có nhánh `type.IsArray` riêng.
- **WMI `MSAcpi_ThermalZoneTemperature`**: thường "Not supported", hoặc trả số cố định (27.8°C). Lọc ngoài 0–125 và tắt nguồn khi lỗi.

## Memory files & external resources

- Code: `system-hud-windows/` (repo `github.com/pushsale/ubuntu-setup`)
- Bản Ubuntu: `system-hud/`, tài liệu [../system-hud/system.md](../system-hud/system.md)
- LibreHardwareMonitor (nguồn nhiệt độ tuỳ chọn): `https://github.com/LibreHardwareMonitor/LibreHardwareMonitor`
- Memory: memory Claude Code của project trên máy phát triển (`%USERPROFILE%\.claude\projects\<thư-mục-project>\memory\`), nằm ngoài repo
