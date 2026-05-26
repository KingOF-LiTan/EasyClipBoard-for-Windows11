/* WinClipboard lightweight status toast */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};
    const TOAST_DURATION = 1500;
    let _timer = null;

    function show(message) {
        const el = document.getElementById('toast');
        if (!el) return;
        if (_timer) clearTimeout(_timer);
        el.textContent = message;
        el.classList.add('visible');
        _timer = setTimeout(() => el.classList.remove('visible'), TOAST_DURATION);
    }

    // Convenience shortcuts bound to i18n keys
    function toastCopied()     { show(app.i18n.t('toast.copied')); }
    function toastPinned()     { show(app.i18n.t('toast.pinned')); }
    function toastUnpinned()   { show(app.i18n.t('toast.unpinned')); }
    function toastDeleted()    { show(app.i18n.t('toast.deleted')); }
    function toastSecretCopy() { show(app.i18n.t('toast.secretCopied')); }

    app.toast = {
        show,
        copied: toastCopied,
        pinned: toastPinned,
        unpinned: toastUnpinned,
        deleted: toastDeleted,
        secretCopied: toastSecretCopy
    };
})(window);
