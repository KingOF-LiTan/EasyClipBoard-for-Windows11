/* WinClipboard frontend startup */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    document.addEventListener('DOMContentLoaded', async () => {
        app.i18n.applyTranslations();
        app.history.bindEvents();
        app.settings.bindEvents();
        app.vault.bindEvents();
        app.overlays.bindEvents();
        await app.settings.load();
        await app.history.refreshList();
        app.keyboard.init();
        app.drag.setupDragHandle();
    });
})(window);
