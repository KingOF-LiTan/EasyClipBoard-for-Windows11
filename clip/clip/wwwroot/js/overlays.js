/* WinClipboard panels, modals, preview, and window animation */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    const panelIds = {
        settings: 'settings-panel',
        vault: 'vault-panel'
    };

    function isVisible(id) {
        const el = document.getElementById(id);
        return !!el && !el.classList.contains('hidden');
    }

    function openPanel(panelName) {
        Object.keys(panelIds).forEach(name => {
            const el = document.getElementById(panelIds[name]);
            if (el) el.classList.toggle('hidden', name !== panelName);
        });
        app.state.setActivePanel(panelName);
    }

    function closePanel(panelName) {
        const el = document.getElementById(panelIds[panelName]);
        if (el) el.classList.add('hidden');
        if (app.state.get().activePanel === panelName) {
            app.state.setActivePanel(null);
        }
    }

    function togglePanel(panelName) {
        if (app.state.get().activePanel === panelName) {
            closePanel(panelName);
            return false;
        }

        openPanel(panelName);
        return true;
    }

    function openModal(modalName) {
        app.state.setActiveModal(modalName);
    }

    function closeModal(modalName) {
        if (app.state.get().activeModal === modalName) {
            app.state.setActiveModal(null);
        }
    }

    function showPreview(idx) {
        const item = app.state.getItems()[idx];
        if (!item) return;

        if (item.hasImage || item.type === 'image') {
            app.bridge.send('showImagePreviewWindow', { id: item.id });
            return;
        }

        const modal = document.getElementById('preview-modal');
        const content = document.getElementById('preview-content');
        const escapeHtml = app.listView.escapeHtml;

        if (item.type === 'text') {
            content.innerHTML = `<div class="preview-loading">${app.i18n.t('preview.loading')}</div>`;
            modal.classList.remove('hidden');
            openModal('preview');
            app.bridge.send('getFullText', { id: item.id }).then(res => {
                if (res && res.success) {
                    document.getElementById('preview-content').innerHTML = `<pre class="preview-text">${escapeHtml(res.text || app.i18n.t('preview.empty'))}</pre>`;
                } else {
                    document.getElementById('preview-content').innerHTML = `<pre>${escapeHtml(item.preview || app.i18n.t('preview.empty'))}</pre>`;
                }
            });
            return;
        } else {
            content.innerHTML = `<pre>${escapeHtml(item.preview || app.i18n.t('preview.empty'))}</pre>`;
        }

        modal.classList.remove('hidden');
        openModal('preview');
    }

    function closePreview() {
        document.getElementById('preview-modal').classList.add('hidden');
        closeModal('preview');
    }

    function showConfirm(message, onConfirm) {
        const modal = document.getElementById('confirm-modal');
        document.getElementById('confirm-message').textContent = message;
        modal.classList.remove('hidden');
        openModal('confirm');

        // Wire buttons once by replacing old listener-bearing nodes.
        const btnOk = document.getElementById('confirm-ok');
        const btnCancel = document.getElementById('confirm-cancel');
        const newOk = btnOk.cloneNode(true);
        const newCancel = btnCancel.cloneNode(true);
        btnOk.parentNode.replaceChild(newOk, btnOk);
        btnCancel.parentNode.replaceChild(newCancel, btnCancel);
        newOk.addEventListener('click', () => {
            modal.classList.add('hidden');
            closeModal('confirm');
            onConfirm();
        });
        newCancel.addEventListener('click', () => {
            modal.classList.add('hidden');
            closeModal('confirm');
        });
    }

    function playShowAnimation() {
        document.body.classList.remove('anim-hide');
        document.body.classList.add('anim-show');
    }

    function playHideAnimation(callback) {
        document.body.classList.remove('anim-show');
        document.body.classList.add('anim-hide');
        setTimeout(callback, 180);
    }

    async function hideWindowAnimated() {
        playHideAnimation(() => app.bridge.send('hideWindow'));
    }

    function closeTopOrHideWindow() {
        if (isVisible('preview-modal')) {
            closePreview();
            return;
        }
        if (isVisible('vault-add-modal') && app.vault) {
            app.vault.closeAddModal();
            return;
        }
        if (isVisible('confirm-modal')) {
            document.getElementById('confirm-modal').classList.add('hidden');
            closeModal('confirm');
            return;
        }
        if (isVisible('settings-panel')) {
            closePanel('settings');
            return;
        }
        if (isVisible('vault-panel')) {
            closePanel('vault');
            return;
        }

        app.bridge.send('hideWindow');
    }

    win.__on_window_shown = function () {
        playShowAnimation();
        if (app.history && app.history.refreshList) {
            app.history.refreshList();
        }
    };

    function bindEvents() {
        const modal = document.getElementById('preview-modal');
        const content = document.getElementById('preview-content');
        modal?.addEventListener('click', closePreview);
        content?.addEventListener('click', event => event.stopPropagation());
    }

    app.overlays = {
        openPanel,
        closePanel,
        togglePanel,
        openModal,
        closeModal,
        showPreview,
        closePreview,
        showConfirm,
        playShowAnimation,
        playHideAnimation,
        hideWindowAnimated,
        closeTopOrHideWindow,
        bindEvents
    };

    // Required by C# callback
    win.hideWindowAnimated = hideWindowAnimated;
})(window);
