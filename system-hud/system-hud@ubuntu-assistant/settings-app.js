// Cửa sổ cấu hình System HUD, chạy độc lập: gjs -m settings-app.js
// HUD mở file này khi bấm biểu tượng trên thanh trên cùng. Chỉ một cửa sổ:
// mở lần nữa thì đưa cửa sổ đang có lên trước.

import Adw from 'gi://Adw?version=1';
import GLib from 'gi://GLib';
import Gtk from 'gi://Gtk?version=4.0';
import Gio from 'gi://Gio';
import System from 'system';

import {buildPage} from './settings.js';

const DIR = GLib.path_get_dirname(GLib.filename_from_uri(import.meta.url)[0]);

const app = new Adw.Application({
    application_id: 'com.ubuntu_assistant.SystemHud.Settings',
    flags: Gio.ApplicationFlags.DEFAULT_FLAGS,
});

app.connect('activate', () => {
    let win = app.active_window;
    if (!win) {
        const view = new Adw.ToolbarView();
        view.add_top_bar(new Adw.HeaderBar());
        view.content = buildPage(DIR);
        win = new Adw.ApplicationWindow({
            application: app,
            title: 'Cấu hình System HUD',
            default_width: 560,
            default_height: 760,
            content: view,
        });
    }
    win.present();
});

app.run([System.programInvocationName]);
