// Nút "Cài đặt" của System HUD trong ứng dụng Extensions. Dùng chung trang với settings-app.js.
// Shell chỉ biết extension có prefs.js từ lần đăng nhập nạp nó; trước đó dùng biểu tượng trên thanh trên cùng.

import {ExtensionPreferences} from 'resource:///org/gnome/Shell/Extensions/js/extensions/prefs.js';

import {buildPage} from './settings.js';

export default class SystemHudPreferences extends ExtensionPreferences {
    fillPreferencesWindow(window) {
        window.add(buildPage(this.path));
    }
}
