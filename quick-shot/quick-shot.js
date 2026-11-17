// Quick Shot: chụp vùng màn hình rồi vẽ chữ, mũi tên, khung lên ảnh; copy hoặc lưu file.
//   gjs -m quick-shot.js              chọn vùng bằng giao diện chụp của GNOME, rồi mở cửa sổ vẽ
//   gjs -m quick-shot.js ảnh.png      mở thẳng một ảnh PNG có sẵn để vẽ
// Phím Print gọi lệnh thứ nhất (install.sh gán). Một tiến trình duy nhất: bấm Print khi đang
// mở cửa sổ thì tiến trình đang chạy nhận lệnh và mở thêm cửa sổ mới.

import Adw from 'gi://Adw?version=1';
import Gdk from 'gi://Gdk?version=4.0';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import Gtk from 'gi://Gtk?version=4.0';
import Pango from 'gi://Pango';
import PangoCairo from 'gi://PangoCairo';
import Cairo from 'cairo';
import System from 'system';

const APP_ID = 'com.ubuntu_assistant.QuickShot';

// Độ dày nét (px của ảnh) và cỡ chữ theo lựa chọn Mảnh / Vừa / Đậm
const SIZES = [
    {label: 'Mảnh', line: 2, font: 16},
    {label: 'Vừa', line: 4, font: 24},
    {label: 'Đậm', line: 7, font: 36},
];
const DEFAULT_COLOR = '#e01b24';
// công cụ đầu tiên là công cụ được chọn sẵn khi mở cửa sổ
const TOOLS = [
    ['rect', 'Khung', 'R'],
    ['arrow', 'Mũi tên', 'A'],
    ['text', 'Chữ', 'T'],
];

// ---------------- Chụp màn hình qua xdg-desktop-portal ----------------

// interactive = true: GNOME hiện giao diện chọn vùng (như phím Print), trả về ảnh vùng đã chọn.
// Không cần quyền lưu sẵn trong PermissionStore (quyền chỉ bị hỏi khi chụp không tương tác).
function portalScreenshot(interactive) {
    return new Promise((resolve, reject) => {
        const bus = Gio.DBus.session;
        const token = `quickshot${GLib.random_int()}`;
        const sender = bus.get_unique_name().slice(1).replace(/\./g, '_');
        const requestPath = `/org/freedesktop/portal/desktop/request/${sender}/${token}`;

        // đăng ký nghe Response trước khi gọi để không lỡ tín hiệu
        const subId = bus.signal_subscribe('org.freedesktop.portal.Desktop',
            'org.freedesktop.portal.Request', 'Response', requestPath, null,
            Gio.DBusSignalFlags.NONE, (_c, _s, _p, _i, _sig, params) => {
                bus.signal_unsubscribe(subId);
                const [code, results] = params.recursiveUnpack();
                if (code === 0 && results.uri)
                    resolve(Gio.File.new_for_uri(results.uri).get_path());
                else
                    resolve(null);  // 1 = người dùng huỷ (Esc), 2 = lỗi khác
            });

        const options = {
            handle_token: new GLib.Variant('s', token),
            interactive: new GLib.Variant('b', interactive),
        };
        bus.call('org.freedesktop.portal.Desktop', '/org/freedesktop/portal/desktop',
            'org.freedesktop.portal.Screenshot', 'Screenshot',
            new GLib.Variant('(sa{sv})', ['', options]), null,
            Gio.DBusCallFlags.NONE, -1, null, (conn, res) => {
                try {
                    conn.call_finish(res);
                } catch (e) {
                    bus.signal_unsubscribe(subId);
                    reject(e);
                }
            });
    });
}

// ---------------- Vẽ chú thích ----------------

function rgbaOf(color) {
    return [color.red, color.green, color.blue, color.alpha];
}

function fontDesc(size) {
    return Pango.FontDescription.from_string(`Sans Bold ${size}px`);
}

function drawArrow(cr, a) {
    const {x1, y1, x2, y2, width} = a;
    const len = Math.hypot(x2 - x1, y2 - y1);
    if (len < 1)
        return;
    const head = Math.min(Math.max(12, width * 4.5), len);
    const ang = Math.atan2(y2 - y1, x2 - x1);
    const spread = Math.PI / 7;
    // thân dừng ở giữa đầu mũi tên để đầu nhọn không bị nét tròn làm cùn
    const bx = x2 - Math.cos(ang) * head * 0.6;
    const by = y2 - Math.sin(ang) * head * 0.6;
    cr.setSourceRGBA(...a.color);
    cr.setLineWidth(width);
    cr.setLineCap(Cairo.LineCap.ROUND);
    cr.moveTo(x1, y1);
    cr.lineTo(bx, by);
    cr.stroke();
    cr.moveTo(x2, y2);
    cr.lineTo(x2 - head * Math.cos(ang - spread), y2 - head * Math.sin(ang - spread));
    cr.lineTo(x2 - head * Math.cos(ang + spread), y2 - head * Math.sin(ang + spread));
    cr.closePath();
    cr.fill();
}

function drawRect(cr, a) {
    const x = Math.min(a.x1, a.x2), y = Math.min(a.y1, a.y2);
    cr.setSourceRGBA(...a.color);
    cr.setLineWidth(a.width);
    cr.setLineJoin(Cairo.LineJoin.MITER);
    cr.rectangle(x, y, Math.abs(a.x2 - a.x1), Math.abs(a.y2 - a.y1));
    cr.stroke();
}

function drawText(cr, a) {
    if (!a.text)
        return;
    const layout = PangoCairo.create_layout(cr);
    layout.set_font_description(fontDesc(a.size));
    layout.set_text(a.text, -1);
    cr.moveTo(a.x, a.y);
    // viền mảnh màu tương phản để chữ đọc được trên mọi nền
    PangoCairo.layout_path(cr, layout);
    const [r, g, b] = a.color;
    const light = 0.299 * r + 0.587 * g + 0.114 * b > 0.6;
    cr.setSourceRGBA(...(light ? [0, 0, 0, 0.55] : [1, 1, 1, 0.75]));
    cr.setLineWidth(Math.max(2, a.size / 8));
    cr.setLineJoin(Cairo.LineJoin.ROUND);
    cr.strokePreserve();
    cr.setSourceRGBA(...a.color);
    cr.fill();
}

function drawAnnotation(cr, a) {
    cr.save();
    if (a.type === 'arrow')
        drawArrow(cr, a);
    else if (a.type === 'rect')
        drawRect(cr, a);
    else if (a.type === 'text')
        drawText(cr, a);
    cr.restore();
}

// Ảnh gốc + mọi chú thích, đúng kích thước gốc
function renderSurface(image, annotations) {
    const out = new Cairo.ImageSurface(Cairo.Format.ARGB32, image.getWidth(), image.getHeight());
    const cr = new Cairo.Context(out);
    cr.setSourceSurface(image, 0, 0);
    cr.paint();
    annotations.forEach(a => drawAnnotation(cr, a));
    cr.$dispose();
    return out;
}

function timestampName() {
    return `Screenshot-${GLib.DateTime.new_now_local().format('%Y-%m-%d_%H-%M-%S')}.png`;
}

// ---------------- Cửa sổ vẽ ----------------

class EditorWindow {
    constructor(app, imagePath) {
        this.image = Cairo.ImageSurface.createFromPNG(imagePath);
        this.W = this.image.getWidth();
        this.H = this.image.getHeight();
        this.annotations = [];
        this.draft = null;      // hình đang kéo hoặc chữ đang gõ
        this.tool = TOOLS[0][0];
        this.color = new Gdk.RGBA();
        this.color.parse(DEFAULT_COLOR);
        this.sizeIdx = 1;
        this.view = {s: 1, ox: 0, oy: 0};

        this._build(app);
    }

    _build(app) {
        const header = new Adw.HeaderBar();

        // công cụ: nhóm nút bật / tắt, chỉ một nút được chọn
        const toolBox = new Gtk.Box({css_classes: ['linked']});
        this.toolButtons = {};
        let first = null;
        for (const [id, label, key] of TOOLS) {
            const b = new Gtk.ToggleButton({label, tooltip_text: `${label} (phím ${key})`});
            if (first)
                b.group = first;
            else
                first = b;
            b.connect('toggled', () => {
                if (b.active)
                    this.tool = id;
            });
            this.toolButtons[id] = b;
            toolBox.append(b);
        }
        this.toolButtons[TOOLS[0][0]].active = true;
        header.pack_start(toolBox);

        const colorBtn = new Gtk.ColorDialogButton({
            dialog: new Gtk.ColorDialog({with_alpha: false, title: 'Chọn màu'}),
            rgba: this.color,
            tooltip_text: 'Màu',
        });
        colorBtn.connect('notify::rgba', () => {
            this.color = colorBtn.rgba;
        });
        header.pack_start(colorBtn);

        const sizeDrop = Gtk.DropDown.new_from_strings(SIZES.map(s => s.label));
        sizeDrop.selected = this.sizeIdx;
        sizeDrop.tooltip_text = 'Độ dày nét và cỡ chữ';
        sizeDrop.connect('notify::selected', () => {
            this.sizeIdx = sizeDrop.selected;
        });
        header.pack_start(sizeDrop);

        const undoBtn = new Gtk.Button({icon_name: 'edit-undo-symbolic', tooltip_text: 'Hoàn tác (Ctrl+Z)'});
        undoBtn.connect('clicked', () => this.undo());
        header.pack_start(undoBtn);

        const copyBtn = new Gtk.Button({label: 'Copy', css_classes: ['suggested-action'],
            tooltip_text: 'Copy ảnh vào clipboard rồi đóng (Ctrl+C)'});
        copyBtn.connect('clicked', () => this.copy());
        const saveBtn = new Gtk.Button({label: 'Lưu…', tooltip_text: 'Lưu thành file PNG (Ctrl+S)'});
        saveBtn.connect('clicked', () => this.save());
        header.pack_end(copyBtn);
        header.pack_end(saveBtn);

        // vùng vẽ
        this.area = new Gtk.DrawingArea({hexpand: true, vexpand: true});
        this.area.set_cursor_from_name('crosshair');
        this.area.set_draw_func((_a, cr, w, h) => this._draw(cr, w, h));

        const drag = new Gtk.GestureDrag();
        drag.connect('drag-begin', (_g, x, y) => this._dragBegin(x, y));
        drag.connect('drag-update', (g, dx, dy) => this._dragUpdate(g, dx, dy));
        drag.connect('drag-end', (g, dx, dy) => this._dragEnd(g, dx, dy));
        this.area.add_controller(drag);

        this.overlay = new Gtk.Overlay({child: this.area});
        this.textEdit = null;
        this.toasts = new Adw.ToastOverlay({child: this.overlay});
        const view = new Adw.ToolbarView({content: this.toasts});
        view.add_top_bar(header);

        // cửa sổ vừa ảnh, không vượt quá 1600x1000 và không nhỏ hơn 640x400
        this.win = new Adw.ApplicationWindow({
            application: app,
            title: `Quick Shot  ${this.W}×${this.H}`,
            default_width: Math.min(Math.max(this.W + 24, 640), 1600),
            default_height: Math.min(Math.max(this.H + 70, 400), 1000),
            content: view,
        });

        this._addShortcuts();
    }

    _addShortcuts() {
        const sc = new Gtk.ShortcutController();
        const add = (accel, fn) => sc.add_shortcut(new Gtk.Shortcut({
            trigger: Gtk.ShortcutTrigger.parse_string(accel),
            action: Gtk.CallbackAction.new(() => {
                fn();
                return true;
            }),
        }));
        add('<Control>c', () => this.copy());
        add('<Control>s', () => this.save());
        add('<Control>z', () => this.undo());
        // Esc: đang gõ chữ thì bỏ chữ đó, không thì đóng cửa sổ
        add('Escape', () => {
            if (this.textEdit)
                this._finishText(false);
            else
                this.win.close();
        });
        for (const [id, , key] of TOOLS)
            add(key.toLowerCase(), () => {
                this.toolButtons[id].active = true;
            });
        this.win.add_controller(sc);
    }

    present() {
        this.win.present();
    }

    // ---- toạ độ: màn hình (widget) <-> ảnh ----
    _toImage(x, y) {
        const {s, ox, oy} = this.view;
        return [
            Math.min(Math.max((x - ox) / s, 0), this.W),
            Math.min(Math.max((y - oy) / s, 0), this.H),
        ];
    }

    _draw(cr, w, h) {
        // nền tối, ảnh thu nhỏ cho vừa (không phóng to quá kích thước gốc), đặt giữa
        cr.setSourceRGB(0.16, 0.16, 0.17);
        cr.paint();
        const s = Math.min(1, w / this.W, h / this.H);
        const ox = Math.floor((w - this.W * s) / 2);
        const oy = Math.floor((h - this.H * s) / 2);
        this.view = {s, ox, oy};

        cr.translate(ox, oy);
        cr.scale(s, s);
        cr.setSourceSurface(this.image, 0, 0);
        cr.getSource().setFilter(s < 1 ? Cairo.Filter.GOOD : Cairo.Filter.NEAREST);
        cr.paint();
        this.annotations.forEach(a => drawAnnotation(cr, a));
        if (this.draft)
            drawAnnotation(cr, this.draft);
        cr.$dispose();
    }

    _style() {
        const size = SIZES[this.sizeIdx];
        return {color: rgbaOf(this.color), width: size.line, size: size.font};
    }

    // ---- kéo chuột: mũi tên, khung; bấm (không kéo) với công cụ Chữ: mở ô nhập ----
    _dragBegin(x, y) {
        // bấm ra chỗ khác khi đang gõ chữ: chốt chữ đó
        this._finishText(true);
        if (this.tool === 'text')
            return;
        const [ix, iy] = this._toImage(x, y);
        this.draft = {type: this.tool, x1: ix, y1: iy, x2: ix, y2: iy, ...this._style()};
    }

    _dragUpdate(g, dx, dy) {
        if (!this.draft || this.draft.type === 'text')
            return;
        const [, sx, sy] = g.get_start_point();
        [this.draft.x2, this.draft.y2] = this._toImage(sx + dx, sy + dy);
        this.area.queue_draw();
    }

    _dragEnd(g, dx, dy) {
        const [, sx, sy] = g.get_start_point();
        if (this.tool === 'text') {
            if (Math.hypot(dx, dy) < 6)
                this._openTextEntry(sx, sy);
            return;
        }
        const d = this.draft;
        this.draft = null;
        // bỏ qua cú bấm nhầm không kéo
        if (d && Math.hypot((d.x2 - d.x1) * this.view.s, (d.y2 - d.y1) * this.view.s) >= 4)
            this.annotations.push(d);
        this.area.queue_draw();
    }

    // Ô nhập nằm ngay dưới điểm bấm, trên cùng mặt vẽ (không dùng popover: popover là bề mặt
    // riêng, dễ mất phím). Chữ hiện trực tiếp trên ảnh trong lúc gõ.
    _openTextEntry(x, y) {
        this._finishText(true);
        const [ix, iy] = this._toImage(x, y);
        const style = this._style();
        // điểm bấm là giữa dòng chữ đầu tiên
        const draft = {type: 'text', x: ix, y: Math.max(0, iy - style.size * 0.6), text: '', ...style};
        this.draft = draft;

        const entry = new Gtk.Entry({
            width_chars: 26,
            placeholder_text: 'Gõ chữ, Enter để xong, Esc để bỏ',
            halign: Gtk.Align.START,
            valign: Gtk.Align.START,
            margin_start: Math.max(0, Math.round(x) - 4),
            margin_top: Math.round(y) + Math.round(style.size * 0.6 * this.view.s) + 8,
        });
        entry.connect('changed', () => {
            draft.text = entry.text;
            this.area.queue_draw();
        });
        entry.connect('activate', () => this._finishText(true));
        this.textEdit = {entry, draft};
        this.overlay.add_overlay(entry);
        entry.grab_focus();
    }

    // keep = true: giữ chữ đã gõ (ô trống thì bỏ); false: huỷ
    _finishText(keep) {
        const edit = this.textEdit;
        if (!edit)
            return;
        this.textEdit = null;
        if (keep && edit.draft.text.trim())
            this.annotations.push(edit.draft);
        if (this.draft === edit.draft)
            this.draft = null;
        this.overlay.remove_overlay(edit.entry);
        this.area.queue_draw();
    }

    undo() {
        this.annotations.pop();
        this.area.queue_draw();
    }

    _toast(text) {
        this.toasts.add_toast(new Adw.Toast({title: text, timeout: 3}));
    }

    // Copy bằng wl-copy: nó tự chạy nền giữ dữ liệu clipboard, nên đóng cửa sổ ngay vẫn dán được.
    // (Clipboard của GTK mất dữ liệu khi tiến trình thoát trên Wayland.)
    copy() {
        this._finishText(true);
        const tmp = GLib.build_filenamev([GLib.get_user_runtime_dir(), `quick-shot-${GLib.random_int()}.png`]);
        try {
            const surface = renderSurface(this.image, this.annotations);
            surface.writeToPNG(tmp);
            const proc = Gio.Subprocess.new(['wl-copy', '--type', 'image/png'],
                Gio.SubprocessFlags.STDIN_PIPE);
            const [, bytes] = GLib.file_get_contents(tmp);
            proc.communicate_async(new GLib.Bytes(bytes), null, (p, res) => {
                GLib.unlink(tmp);
                try {
                    p.communicate_finish(res);
                    if (p.get_successful())
                        this.win.close();
                    else
                        this._toast('Copy thất bại: wl-copy báo lỗi');
                } catch (e) {
                    this._toast(`Copy thất bại: ${e.message}`);
                }
            });
        } catch (e) {
            GLib.unlink(tmp);
            this._toast(`Copy thất bại: ${e.message}`);
        }
    }

    save() {
        this._finishText(true);
        const dir = GLib.build_filenamev([
            GLib.get_user_special_dir(GLib.UserDirectory.DIRECTORY_PICTURES) ?? GLib.get_home_dir(),
            'Screenshots']);
        GLib.mkdir_with_parents(dir, 0o755);
        const dialog = new Gtk.FileDialog({
            title: 'Lưu ảnh',
            initial_folder: Gio.File.new_for_path(dir),
            initial_name: timestampName(),
        });
        dialog.save(this.win, null, (d, res) => {
            let file;
            try {
                file = d.save_finish(res);
            } catch {
                return;  // bấm Huỷ
            }
            let path = file.get_path();
            if (!/\.png$/i.test(path))
                path += '.png';
            try {
                renderSurface(this.image, this.annotations).writeToPNG(path);
                this.win.close();
            } catch (e) {
                this._toast(`Không lưu được: ${e.message}`);
            }
        });
    }
}

// ---------------- Ứng dụng ----------------

const app = new Adw.Application({
    application_id: APP_ID,
    flags: Gio.ApplicationFlags.HANDLES_OPEN,
});

function openEditor(path) {
    try {
        new EditorWindow(app, path).present();
    } catch (e) {
        console.error(`[quick-shot] không mở được ảnh ${path}: ${e.message}`);
    }
}

// Mỗi lần kích hoạt (phím Print) là một lần chụp. hold() giữ tiến trình sống trong lúc
// giao diện chụp của GNOME đang mở (lúc đó chưa có cửa sổ nào của mình).
app.connect('activate', () => {
    app.hold();
    portalScreenshot(true)
        .then(path => {
            if (path)
                openEditor(path);
        })
        .catch(e => console.error(`[quick-shot] chụp màn hình lỗi: ${e.message}`))
        .finally(() => app.release());
});

app.connect('open', (_a, files) => {
    files.forEach(f => openEditor(f.get_path()));
});

// Kiểm thử không cần màn hình: QUICK_SHOT_SELFTEST=out.png gjs -m quick-shot.js in.png
// vẽ vài chú thích mẫu lên in.png rồi ghi ra out.png
const selftest = GLib.getenv('QUICK_SHOT_SELFTEST');
if (selftest) {
    const image = Cairo.ImageSurface.createFromPNG(System.programArgs[0]);
    const W = image.getWidth(), H = image.getHeight();
    const red = [0.88, 0.11, 0.14, 1], yellow = [0.96, 0.83, 0.18, 1];
    renderSurface(image, [
        {type: 'rect', x1: W * 0.1, y1: H * 0.1, x2: W * 0.5, y2: H * 0.45, color: red, width: 4},
        {type: 'arrow', x1: W * 0.85, y1: H * 0.85, x2: W * 0.52, y2: H * 0.47, color: red, width: 4},
        {type: 'arrow', x1: W * 0.2, y1: H * 0.9, x2: W * 0.35, y2: H * 0.6, color: yellow, width: 7},
        {type: 'text', x: W * 0.1, y: H * 0.55, text: 'Chú thích tiếng Việt có dấu', color: red, size: 24},
        {type: 'text', x: W * 0.55, y: H * 0.1, text: 'Chữ vàng', color: yellow, size: 36},
    ]).writeToPNG(selftest);
} else {
    app.run([System.programInvocationName, ...System.programArgs]);
}
