/* WinClipboard history and favorites loading */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    function parseSearch(rawSearch) {
        let typeFilter = null;
        let actualSearch = rawSearch || '';

        if (actualSearch.startsWith('/img')) {
            typeFilter = 'image';
            actualSearch = actualSearch.slice(4).trim();
        } else if (actualSearch.startsWith('/file')) {
            typeFilter = 'files';
            actualSearch = actualSearch.slice(5).trim();
        }

        return { search: actualSearch, typeFilter };
    }

    function switchTab(tab) {
        app.state.set({ currentTab: tab });
        document.querySelectorAll('.segment').forEach(s => s.classList.remove('active'));
        const activeSegment = document.querySelector(`[data-tab="${tab}"]`);
        if (activeSegment) activeSegment.classList.add('active');

        const slider = document.getElementById('segment-slider');
        if (slider) slider.classList.toggle('right', tab === 'favorites');

        app.state.setSelectedIndex(-1);
        refreshList();
    }

    async function refreshList() {
        const searchInput = document.getElementById('search-input');
        const { search, typeFilter } = parseSearch(searchInput ? searchInput.value : '');
        const state = app.state.get();

        let nextItems;
        if (state.currentTab === 'history') {
            const data = await app.bridge.send('getHistory', { search, limit: 200 });
            nextItems = Array.isArray(data) ? data : [];
        } else {
            const data = await app.bridge.send('getFavorites', { search });
            nextItems = Array.isArray(data) ? data : [];
        }

        if (typeFilter) {
            nextItems = nextItems.filter(i => i.type === typeFilter);
        }

        app.state.setItems(nextItems);
        if (nextItems.length > 0 && app.state.get().selectedIndex < 0) {
            app.state.setSelectedIndex(0);
        }

        app.listView.render();
    }

    function clearHistory() {
        app.overlays.showConfirm(app.i18n.t('confirm.clearHistory'), async () => {
            await app.bridge.send('clearHistory');
            await refreshList();
        });
    }

    function bindEvents() {
        document.querySelectorAll('.segment[data-tab]').forEach(button => {
            button.addEventListener('click', () => switchTab(button.dataset.tab));
        });
        document.getElementById('btn-clear')?.addEventListener('click', clearHistory);
    }

    app.history = {
        switchTab,
        refreshList,
        parseSearch,
        clearHistory,
        bindEvents
    };
})(window);
