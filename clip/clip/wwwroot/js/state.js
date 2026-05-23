/* WinClipboard frontend state and namespace */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    const model = {
        currentTab: 'history',
        items: [],
        selectedIndex: -1,
        settingsOpen: false,
        vaultOpen: false,
        currentTheme: 'dark',
        thumbnailCache: {},
        activePanel: null,
        activeModal: null
    };

    function clampSelectedIndex(index) {
        if (!Number.isFinite(index)) return -1;
        if (!model.items.length) return -1;
        if (index < 0) return -1;
        return Math.min(index, model.items.length - 1);
    }

    app.state = {
        get() {
            return model;
        },

        set(patch) {
            Object.assign(model, patch || {});
            if ('selectedIndex' in (patch || {})) {
                model.selectedIndex = clampSelectedIndex(model.selectedIndex);
            }
            return model;
        },

        setItems(nextItems) {
            model.items = Array.isArray(nextItems) ? nextItems : [];
            model.selectedIndex = clampSelectedIndex(model.selectedIndex);
            return model.items;
        },

        getItems() {
            return model.items;
        },

        setSelectedIndex(index) {
            model.selectedIndex = clampSelectedIndex(index);
            return model.selectedIndex;
        },

        getSelectedItem() {
            return model.items[model.selectedIndex] || null;
        },

        setActivePanel(panelNameOrNull) {
            model.activePanel = panelNameOrNull || null;
            model.settingsOpen = model.activePanel === 'settings';
            model.vaultOpen = model.activePanel === 'vault';
            return model.activePanel;
        },

        setActiveModal(modalNameOrNull) {
            model.activeModal = modalNameOrNull || null;
            return model.activeModal;
        },

        getThumbnailCache() {
            return model.thumbnailCache;
        }
    };

    Object.defineProperties(win, {
        currentTab: {
            configurable: true,
            get: () => model.currentTab,
            set: value => { model.currentTab = value; }
        },
        items: {
            configurable: true,
            get: () => model.items,
            set: value => { model.items = Array.isArray(value) ? value : []; }
        },
        selectedIndex: {
            configurable: true,
            get: () => model.selectedIndex,
            set: value => { model.selectedIndex = clampSelectedIndex(Number(value)); }
        },
        settingsOpen: {
            configurable: true,
            get: () => model.settingsOpen,
            set: value => { model.settingsOpen = !!value; }
        },
        vaultOpen: {
            configurable: true,
            get: () => model.vaultOpen,
            set: value => { model.vaultOpen = !!value; }
        },
        currentTheme: {
            configurable: true,
            get: () => model.currentTheme,
            set: value => { model.currentTheme = value; }
        }
    });
})(window);
