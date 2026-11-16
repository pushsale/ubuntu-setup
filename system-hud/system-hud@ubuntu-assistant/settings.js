// Trang cấu hình System HUD (GTK 4 + libadwaita). Dùng chung cho:
//   settings-app.js  cửa sổ riêng, mở khi bấm biểu tượng HUD trên thanh trên cùng
//   prefs.js         nút "Cài đặt" của extension trong ứng dụng Extensions
// Đọc / ghi thẳng config.json trong thư mục extension; HUD theo dõi file nên áp dụng ngay.
// Không import gì của GNOME Shell.

import Adw from 'gi://Adw';
import GLib from 'gi://GLib';
import Gtk from 'gi://Gtk';

import {DEFAULTS, MIN_INTERVAL_MS} from './defaults.js';

const THEMES = [
    ['orbital', 'Orbital (tối)'],
    ['orbitalLight', 'Orbital sáng'],
    ['card', 'Card'],
    ['tactical', 'Tactical'],
    ['terminal', 'Terminal'],
];
const SIDES = [['left', 'Mép trái'], ['right', 'Mép phải']];
const MONITORS = [['primary', 'Màn hình chính'], ['secondary', 'Màn hình phụ'],
    [0, 'Màn số 0'], [1, 'Màn số 1'], [2, 'Màn số 2'], [3, 'Màn số 3']];

const WRITE_DELAY_MS = 300;

export function configPath(dir) {
    return GLib.build_filenamev([dir, 'config.json']);
}

export function readConfig(dir) {
    try {
        const [ok, bytes] = GLib.file_get_contents(configPath(dir));
        if (ok)
            return {...DEFAULTS, ...JSON.parse(new TextDecoder().decode(bytes))};
    } catch (e) {
        console.warn(`[system-hud] config.json không đọc được, dùng mặc định: ${e.message}`);
    }
    return {...DEFAULTS};
}

function writeConfig(dir, cfg) {
    GLib.file_set_contents(configPath(dir), `${JSON.stringify(cfg, null, 2)}\n`);
}

// Ô số: hiển thị theo đơn vị `scale` (vd giây = ms / 1000), lưu theo đơn vị của config
function spinRow({title, subtitle, lower, upper, step, digits = 0}) {
    return new Adw.SpinRow({
        title, subtitle,
        digits,
        adjustment: new Gtk.Adjustment({lower, upper, step_increment: step, page_increment: step * 10}),
    });
}

function comboRow(title, subtitle, items) {
    return new Adw.ComboRow({title, subtitle, model: Gtk.StringList.new(items.map(([, label]) => label))});
}

function historyText(intervalMs) {
    const sec = 60 * intervalMs / 1000;
    return sec >= 120 ? `${+(sec / 60).toFixed(1)} phút` : `${Math.round(sec)} giây`;
}

// Trả về Adw.PreferencesPage đã nối với config.json trong `dir`
export function buildPage(dir) {
    let cfg = readConfig(dir);
    let writeId = 0;
    let loading = true;

    const save = () => {
        if (loading)
            return;
        if (writeId)
            GLib.source_remove(writeId);
        writeId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, WRITE_DELAY_MS, () => {
            writeId = 0;
            try {
                // đọc lại trước khi ghi để không đè các khoá sửa từ nơi khác (hud-config.sh)
                cfg = {...readConfig(dir), ...cfg};
                writeConfig(dir, cfg);
            } catch (e) {
                console.error(`[system-hud] không ghi được config.json: ${e.message}`);
            }
            return GLib.SOURCE_REMOVE;
        });
    };

    const page = new Adw.PreferencesPage({title: 'System HUD', icon_name: 'utilities-system-monitor-symbolic'});

    // ---- Nhịp cập nhật ----
    const timing = new Adw.PreferencesGroup({
        title: 'Nhịp cập nhật',
        description: 'Thay đổi được áp dụng ngay, không cần khởi động lại HUD.',
    });
    page.add(timing);

    const interval = spinRow({title: 'Chu kỳ lấy số liệu (giây)', subtitle: '',
        lower: MIN_INTERVAL_MS / 1000, upper: 60, step: 0.5, digits: 1});
    const follow = new Adw.SwitchRow({
        title: 'Chuyển số liệu suốt cả chu kỳ',
        subtitle: 'Thời gian chuyển luôn bằng chu kỳ lấy số liệu: số chạy dần liên tục, không có lúc đứng yên',
    });
    const anim = spinRow({title: 'Thời gian chuyển số liệu (giây)',
        subtitle: 'Số và đồ thị chạy dần từ giá trị cũ sang giá trị mới trong khoảng này. 0 = nhảy ngay, không hiệu ứng',
        lower: 0, upper: 60, step: 0.5, digits: 1});
    const fps = spinRow({title: 'Số khung hình mỗi giây',
        subtitle: 'Chỉ tính trong lúc đang chuyển số liệu. Cao thì mượt hơn nhưng tốn CPU: 10 khoảng 25% một nhân, 30 khoảng 38%',
        lower: 1, upper: 60, step: 1});
    [interval, follow, anim, fps].forEach(r => timing.add(r));

    const updateIntervalHint = () => {
        interval.subtitle = `Bao lâu đo CPU, RAM, GPU, ổ đĩa, mạng một lần. Đồ thị giữ 60 mẫu = ${historyText(cfg.intervalMs)}`;
        anim.sensitive = !follow.active;
    };

    interval.connect('notify::value', () => {
        cfg.intervalMs = Math.round(interval.value * 1000);
        if (follow.active) {
            cfg.animMs = cfg.intervalMs;
            anim.value = cfg.animMs / 1000;
        }
        updateIntervalHint();
        save();
    });
    follow.connect('notify::active', () => {
        if (follow.active) {
            cfg.animMs = cfg.intervalMs;
            anim.value = cfg.animMs / 1000;
        }
        updateIntervalHint();
        save();
    });
    anim.connect('notify::value', () => {
        cfg.animMs = Math.round(anim.value * 1000);
        save();
    });
    fps.connect('notify::value', () => {
        cfg.fps = Math.round(fps.value);
        save();
    });

    // ---- Giao diện và vị trí ----
    const look = new Adw.PreferencesGroup({title: 'Giao diện và vị trí'});
    page.add(look);

    const theme = comboRow('Giao diện', '', THEMES);
    const monitor = comboRow('Màn hình', 'Màn phụ: màn đầu tiên không phải màn chính. Màn số: theo thứ tự GNOME đánh', MONITORS);
    const side = comboRow('Mép', '', SIDES);
    const width = spinRow({title: 'Chiều rộng (% màn hình)', subtitle: 'Chiều cao luôn bằng chiều cao vùng làm việc',
        lower: 10, upper: 60, step: 1});
    const gap = spinRow({title: 'Khoảng cách tới mép (px)', subtitle: 'Tới mép màn hình, thanh trên cùng và dock',
        lower: 0, upper: 200, step: 2});
    [theme, monitor, side, width, gap].forEach(r => look.add(r));

    const bindCombo = (row, items, key) => row.connect('notify::selected', () => {
        cfg[key] = items[row.selected][0];
        save();
    });
    bindCombo(theme, THEMES, 'theme');
    bindCombo(monitor, MONITORS, 'monitor');
    bindCombo(side, SIDES, 'side');
    width.connect('notify::value', () => {
        cfg.widthFraction = Math.round(width.value) / 100;
        save();
    });
    gap.connect('notify::value', () => {
        cfg.gap = Math.round(gap.value);
        save();
    });

    // ---- Khác ----
    const other = new Adw.PreferencesGroup({title: 'Khác'});
    page.add(other);
    const indicator = new Adw.SwitchRow({
        title: 'Biểu tượng trên thanh trên cùng',
        subtitle: 'Bấm vào để mở cửa sổ này. Tắt đi thì mở lại bằng lệnh ./hud-config.sh ui trong thư mục system-hud',
    });
    other.add(indicator);
    indicator.connect('notify::active', () => {
        cfg.showIndicator = indicator.active;
        save();
    });

    const reset = new Adw.ButtonRow({title: 'Khôi phục mặc định'});
    other.add(reset);

    const indexOf = (items, value) => Math.max(0, items.findIndex(([v]) => v === value));
    const fill = () => {
        loading = true;
        interval.value = cfg.intervalMs / 1000;
        follow.active = cfg.animMs === cfg.intervalMs;
        anim.value = cfg.animMs / 1000;
        fps.value = cfg.fps;
        theme.selected = indexOf(THEMES, cfg.theme);
        monitor.selected = indexOf(MONITORS, cfg.monitor);
        side.selected = indexOf(SIDES, cfg.side);
        width.value = Math.round(cfg.widthFraction * 100);
        gap.value = cfg.gap;
        indicator.active = cfg.showIndicator !== false;
        updateIntervalHint();
        loading = false;
    };

    reset.connect('activated', () => {
        // giữ màn hình và mép đang dùng: đó là chỗ đặt HUD, không phải "giao diện"
        cfg = {...DEFAULTS, monitor: cfg.monitor, side: cfg.side};
        fill();
        save();
    });

    fill();
    return page;
}
