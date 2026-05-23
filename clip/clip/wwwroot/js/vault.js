/* WinClipboard sensitive vault */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    function toggle() {
        const opened = app.overlays.togglePanel('vault');
        if (opened) refresh();
    }

    async function refresh(search) {
        const data = await app.bridge.send('getSensitiveItems', { search: search || '' });
        render(Array.isArray(data) ? data : []);
    }

    function render(vaultItems) {
        const container = document.getElementById('vault-list');
        if (!vaultItems || vaultItems.length === 0) {
            container.innerHTML = `<div class="vault-empty">${app.i18n.t('vault.empty')}</div>`;
            return;
        }

        container.innerHTML = vaultItems.map(item => {
            const icon = getSensitiveIcon(item.sensitiveType);
            const name = item.alias || item.username || app.i18n.t('vault.unnamed');
            const sub = item.username ? `👤 ${item.username}` : (item.sensitiveType || '');
            const remarkHtml = item.remark ? `<div class="vault-remark">💬 ${app.listView.escapeHtml(item.remark)}</div>` : '';

            return `
                <div class="vault-item">
                    <span class="vault-icon">${icon}</span>
                    <div class="vault-info">
                        <div class="vault-name">${app.listView.escapeHtml(name)}</div>
                        <div class="vault-sub">${app.listView.escapeHtml(sub)}</div>
                        ${remarkHtml}
                    </div>
                    <div class="vault-actions">
                        ${item.username ? `<button class="card-action-btn" data-vault-action="copy-username" data-id="${item.id}" title="${app.i18n.t('action.copyUsername')}">👤</button>` : ''}
                        <button class="card-action-btn" data-vault-action="copy-secret" data-id="${item.id}" title="${app.i18n.t('action.copyPassword')}">🔑</button>
                        <button class="card-action-btn danger" data-vault-action="delete-secret" data-id="${item.id}" title="${app.i18n.t('action.delete')}">✕</button>
                    </div>
                </div>
            `;
        }).join('');
        bindVaultListEvents(container);
    }

    function bindVaultListEvents(container) {
        if (container.dataset.eventsBound === '1') return;
        container.dataset.eventsBound = '1';
        container.addEventListener('click', event => {
            const button = event.target.closest('[data-vault-action]');
            if (!button) return;
            const id = parseInt(button.dataset.id, 10);
            switch (button.dataset.vaultAction) {
                case 'copy-username':
                    copyUsername(id);
                    break;
                case 'copy-secret':
                    copySecret(id);
                    break;
                case 'delete-secret':
                    deleteSecret(id);
                    break;
            }
        });
    }

    function getSensitiveIcon(type) {
        switch (type) {
            case 'Password': return '🔑';
            case 'Credential': return '👤';
            case 'ApiKey': return '🔐';
            case 'PrivateKey': return '🗝️';
            default: return '🔒';
        }
    }

    async function copySecret(id) {
        const result = await app.bridge.send('decryptSecret', { id });
        if (result?.text) {
            await app.bridge.send('pasteText', { text: result.text });
            await app.bridge.send('hideWindow');
        }
    }

    async function copyUsername(id) {
        const result = await app.bridge.send('getUsername', { id });
        if (result?.success && result.text) {
            await app.bridge.send('pasteText', { text: result.text });
            await app.bridge.send('hideWindow');
        }
    }

    async function deleteSecret(id) {
        await app.bridge.send('deleteSecret', { id });
        await refresh();
    }

    function search(query) {
        refresh(query);
    }

    function addSecret() {
        document.getElementById('vault-add-alias').value = '';
        document.getElementById('vault-add-username').value = '';
        document.getElementById('vault-add-password').value = '';
        document.getElementById('vault-add-remark').value = '';
        document.getElementById('vault-add-modal').classList.remove('hidden');
        app.state.setActiveModal('vaultAdd');
        document.getElementById('vault-add-alias').focus();
    }

    function closeAddModal() {
        document.getElementById('vault-add-modal').classList.add('hidden');
        app.state.setActiveModal(null);
    }

    async function submitAdd() {
        const alias = document.getElementById('vault-add-alias').value.trim() || app.i18n.t('vault.unnamed');
        const username = document.getElementById('vault-add-username').value.trim() || undefined;
        const content = document.getElementById('vault-add-password').value.trim();
        const remark = document.getElementById('vault-add-remark').value.trim() || undefined;

        if (!content) {
            alert(app.i18n.t('vault.error.passwordRequired'));
            return;
        }

        await app.bridge.send('addSecret', {
            alias,
            username,
            content,
            remark,
            sensitiveType: 'Password'
        });

        closeAddModal();
        await refresh();
    }

    function bindEvents() {
        document.getElementById('btn-vault')?.addEventListener('click', toggle);
        document.querySelector('[data-action="close-vault"]')?.addEventListener('click', toggle);
        document.getElementById('btn-add-secret')?.addEventListener('click', addSecret);
        document.getElementById('vault-search')?.addEventListener('input', event => search(event.target.value));
        document.getElementById('vault-add-cancel')?.addEventListener('click', closeAddModal);
        document.getElementById('vault-add-ok')?.addEventListener('click', submitAdd);
    }

    app.vault = {
        toggle,
        refresh,
        render,
        getSensitiveIcon,
        copySecret,
        copyUsername,
        deleteSecret,
        search,
        addSecret,
        closeAddModal,
        submitAdd,
        bindEvents
    };
})(window);
