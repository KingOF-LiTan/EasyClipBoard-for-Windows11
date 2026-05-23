/* WinClipboard localization helpers */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    function t(key) {
        if (!win.INITIAL_LOCALE) return key;
        return win.INITIAL_LOCALE[key] || key;
    }

    function applyTranslations() {
        document.querySelectorAll('[data-i18n]').forEach(el => {
            const key = el.getAttribute('data-i18n');
            el.textContent = t(key);
        });

        document.querySelectorAll('[data-i18n-prop]').forEach(el => {
            const parts = el.getAttribute('data-i18n-prop').split(':');
            if (parts.length === 2) {
                el.setAttribute(parts[0], t(parts[1]));
            }
        });
    }

    app.i18n = {
        t,
        applyTranslations
    };

    // Required by C# setLanguage ExecuteScriptAsync callback
    win.applyTranslations = applyTranslations;
})(window);
