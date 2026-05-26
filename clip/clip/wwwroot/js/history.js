/* WinClipboard history and favorites loading */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};
    let _chipFilter = null;

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

        // Slash command takes priority; chip filter is fallback only when no slash was typed
        if (!typeFilter && _chipFilter) typeFilter = _chipFilter;

        return { search: actualSearch, typeFilter };
    }

    function updateChipActive(filter) {
        document.querySelectorAll('.filter-chip').forEach(chip => {
            chip.classList.toggle('active', chip.dataset.filter === filter);
        });
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

        // Sync chip active state: slash command wins, chip reflects it
        const activeChip = (!typeFilter) ? 'all' :
            (typeFilter === 'files') ? 'file' : typeFilter;
        updateChipActive(activeChip);

        app.state.setItems(nextItems);
        if (nextItems.length > 0 && app.state.get().selectedIndex < 0) {
            app.state.setSelectedIndex(0);
        }

        app.listView.render();
    }

    function setChipFilter(filter) {
        // Map chip values to item type values
        if (filter === 'all') {
            _chipFilter = null;
        } else if (filter === 'text') {
            _chipFilter = 'text';
        } else if (filter === 'file') {
            _chipFilter = 'files';
        } else {
            _chipFilter = filter; // 'image' stays 'image'
        }

        // Keep search focus if it was focused
        const wasFocused = document.activeElement?.id === 'search-input';
        refreshList().then(() => {
            if (wasFocused) document.getElementById('search-input')?.focus();
        });
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

        document.querySelectorAll('.filter-chip[data-filter]').forEach(chip => {
            chip.addEventListener('click', () => setChipFilter(chip.dataset.filter));
        });
    }

    app.history = {
        switchTab,
        refreshList,
        parseSearch,
        clearHistory,
        setChipFilter,
        bindEvents
    };
})(window);
