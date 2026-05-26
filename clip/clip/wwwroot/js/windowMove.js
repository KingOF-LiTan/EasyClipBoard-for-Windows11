/* WinClipboard window move handle */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    function init() {
        const handle = document.getElementById('window-drag-handle');
        if (!handle) return;

        handle.addEventListener('pointerdown', (e) => {
            if (e.button !== 0) return;
            e.preventDefault();
            app.bridge.send('startWindowMove');
        });
    }

    app.windowMove = { init };
})(window);
