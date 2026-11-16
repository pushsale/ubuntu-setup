// Cấu hình mặc định của System HUD. Dùng chung cho core.js (trong gnome-shell)
// và cửa sổ cấu hình (settings.js, chạy bằng gjs ngoài shell): không import gì của GNOME Shell.

export const DEFAULTS = {
    theme: 'orbital',     // tactical | terminal | orbital | orbitalLight | card
    monitor: 'primary',   // 'primary' | 'secondary' | số thứ tự màn hình (0, 1, ...)
    side: 'left',         // 'left' | 'right'
    widthFraction: 0.25,  // tỉ lệ chiều rộng so với màn hình được chọn
    gap: 12,              // khoảng cách tới mép / dock / top bar
    intervalMs: 5000,     // chu kỳ lấy mẫu số liệu
    animMs: 5000,         // thời gian hiệu ứng chuyển số liệu (0 = tắt hiệu ứng); bằng intervalMs = chuyển liên tục
    fps: 10,              // số khung hình/giây trong lúc chạy hiệu ứng (30 tốn CPU gấp đôi, nóng máy)
    showIndicator: true,  // biểu tượng trên thanh trên cùng, bấm vào để mở cửa sổ cấu hình
};

export const MIN_INTERVAL_MS = 500;
