/* WinClipboard keyboard shortcuts and hotkey recorder */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};
    let initialized = false;

    function getModifierMask(event) {
        let modInt = win._mods || 0;
        if (event.ctrlKey) modInt |= 0x0002;
        if (event.altKey) modInt |= 0x0001;
        if (event.shiftKey) modInt |= 0x0004;
        if (event.metaKey) modInt |= 0x0008;
        return modInt;
    }

    function bindGlobalShortcuts() {
        document.addEventListener('keydown', async (e) => {
            const isSearchInput = document.activeElement?.id === 'search-input';
            const inInput = document.activeElement?.tagName === 'INPUT' || document.activeElement?.tagName === 'TEXTAREA' || document.activeElement?.tagName === 'SELECT';
            const state = app.state.get();

            if (e.key === 'Escape') {
                app.overlays.closeTopOrHideWindow();
                return;
            }

            if (e.ctrlKey && e.key.toLowerCase() === 'f') {
                e.preventDefault();
                document.getElementById('search-input').focus();
                return;
            }

            if (e.key === 'ArrowDown') {
                if (inInput && !isSearchInput) return;
                e.preventDefault();
                if (state.selectedIndex < state.items.length - 1) {
                    app.state.setSelectedIndex(state.selectedIndex + 1);
                    app.listView.render();
                    app.listView.scrollToSelected();
                }
                return;
            }

            if (e.key === 'ArrowUp') {
                if (inInput && !isSearchInput) return;
                e.preventDefault();
                if (state.selectedIndex > 0) {
                    app.state.setSelectedIndex(state.selectedIndex - 1);
                    app.listView.render();
                    app.listView.scrollToSelected();
                }
                return;
            }

            if (e.key === 'Enter') {
                if (inInput && !isSearchInput) return;
                e.preventDefault();
                if (isSearchInput) {
                    document.activeElement.blur();
                }

                if (app.state.get().selectedIndex < 0 && app.state.getItems().length > 0) {
                    app.state.setSelectedIndex(0);
                }

                const selectedIndex = app.state.get().selectedIndex;
                if (selectedIndex >= 0 && selectedIndex < app.state.getItems().length && app.state.getItems().length > 0) {
                    await app.listActions.paste(selectedIndex);
                }
                return;
            }

            if (inInput) return;

            if (e.key === ' ' && app.state.get().selectedIndex >= 0) {
                e.preventDefault();
                app.overlays.showPreview(app.state.get().selectedIndex);
                return;
            }

            if (e.key >= '1' && e.key <= '9') {
                const idx = parseInt(e.key) - 1;
                if (idx >= 0 && idx < app.state.getItems().length) {
                    e.preventDefault();
                    await app.listActions.paste(idx);
                }
            }
        });
    }

    function bindModifierTracking() {
        win._mods = 0;

        win.addEventListener('keydown', (ev) => {
            if (ev.key === 'Control') win._mods |= 0x0002;
            if (ev.key === 'Alt') win._mods |= 0x0001;
            if (ev.key === 'Shift') win._mods |= 0x0004;
            if (ev.key === 'Meta') win._mods |= 0x0008;
        }, true);

        win.addEventListener('keyup', (ev) => {
            if (ev.key === 'Control') win._mods &= ~0x0002;
            if (ev.key === 'Alt') win._mods &= ~0x0001;
            if (ev.key === 'Shift') win._mods &= ~0x0004;
            if (ev.key === 'Meta') win._mods &= ~0x0008;
        }, true);

        win.addEventListener('blur', () => { win._mods = 0; });
    }

    function bindShortcutRecorder(shortcutInput) {
        if (!shortcutInput) return;

        shortcutInput.addEventListener('focus', () => {
            win._recorderActive = true;
            win._mods = 0;
            shortcutInput.dataset.original = shortcutInput.value;
            shortcutInput.value = app.i18n.t('hint.shortcutReady');
            shortcutInput.classList.add('recording');
        });

        shortcutInput.addEventListener('blur', () => {
            win._recorderActive = false;
            win._mods = 0;
            shortcutInput.classList.remove('recording', 'error', 'success');
            if (!shortcutInput.dataset.committed) {
                shortcutInput.value = shortcutInput.dataset.original || '';
            }
            delete shortcutInput.dataset.committed;
        });

        shortcutInput.addEventListener('keydown', async (e) => {
            e.preventDefault();
            e.stopPropagation();

            if (e.key === 'Escape') { shortcutInput.blur(); return; }

            const isModifier = ['Control', 'Alt', 'Shift', 'Meta'].includes(e.key);
            if (isModifier) return;

            const modInt = getModifierMask(e);
            const displayMods = [];
            if (modInt & 0x0002) displayMods.push('Ctrl');
            if (modInt & 0x0001) displayMods.push('Alt');
            if (modInt & 0x0004) displayMods.push('Shift');
            if (modInt & 0x0008) displayMods.push('Win');

            if (modInt === 0) {
                shortcutInput.value = app.i18n.t('hint.shortcutComboRequired');
                shortcutInput.classList.remove('recording');
                shortcutInput.classList.add('error');
                setTimeout(() => {
                    shortcutInput.value = app.i18n.t('hint.shortcutReady');
                    shortcutInput.classList.remove('error');
                    shortcutInput.classList.add('recording');
                }, 1500);
                return;
            }

            const codeStr = e.code;
            const displayStr = displayMods.join(' + ') + ' + ' + codeStr;
            shortcutInput.value = displayStr;

            const res = await app.bridge.send('setShortcut', { modifiers: modInt, code: codeStr });
            if (res && res.success) {
                shortcutInput.dataset.original = displayStr;
                shortcutInput.dataset.committed = '1';
                shortcutInput.classList.remove('recording', 'error');
                shortcutInput.classList.add('success');
                setTimeout(() => {
                    shortcutInput.classList.remove('success');
                    shortcutInput.blur();
                }, 1200);
            } else {
                shortcutInput.value = app.i18n.t('hint.shortcutError');
                shortcutInput.classList.remove('recording');
                shortcutInput.classList.add('error');
                setTimeout(() => {
                    shortcutInput.value = shortcutInput.dataset.original || '';
                    shortcutInput.classList.remove('error');
                    shortcutInput.blur();
                }, 2000);
            }
        });
    }

    function bindSearchInput() {
        let searchTimeout;
        const searchInput = document.getElementById('search-input');
        if (!searchInput) return;

        searchInput.addEventListener('input', () => {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                app.state.setSelectedIndex(app.state.getItems().length > 0 ? 0 : -1);
                app.history.refreshList();
            }, 50);
        });
    }

    function init() {
        if (initialized) return;
        initialized = true;
        bindGlobalShortcuts();
        bindModifierTracking();
        bindShortcutRecorder(document.getElementById('shortcut-input'));
        bindSearchInput();
    }

    app.keyboard = {
        init,
        bindGlobalShortcuts,
        bindShortcutRecorder,
        getModifierMask
    };

    // Keyboard is initialized via app.keyboard.init()
})(window);
