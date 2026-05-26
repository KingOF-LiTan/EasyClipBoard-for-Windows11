/* WinClipboard list item actions */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    function select(idx) {
        app.state.setSelectedIndex(idx);
        app.listView.render();
    }

    function onCardClick(idx) {
        // Final semantics: single click = select only.
        select(idx);
    }

    async function onCardDoubleClick(idx) {
        // Double click = paste the item.
        select(idx);
        await paste(idx);
    }

    async function paste(idx) {
        const item = app.state.getItems()[idx];
        if (!item) return;
        await app.bridge.send('paste', { id: item.id });
        await app.bridge.send('hideWindow');
    }

    function deleteItem(idx) {
        const item = app.state.getItems()[idx];
        if (!item) return;
        app.overlays.showConfirm(app.i18n.t('confirm.deleteItem'), async () => {
            await app.bridge.send('delete', { id: item.id });
            app.toast.deleted();
            await app.history.refreshList();
        });
    }

    async function toggleFavorite(idx) {
        const item = app.state.getItems()[idx];
        if (!item) return;
        await app.bridge.send('toggleFavorite', { id: item.id });
        app.toast[item.isFavorite ? 'unpinned' : 'pinned']();
        await app.history.refreshList();
    }

    async function setTag(idx, tag) {
        const item = app.state.getItems()[idx];
        if (!item) return;
        await app.bridge.send('updateTag', { id: item.id, tag });
        app.toast.show(app.i18n.t('toast.tagged'));
        await app.history.refreshList();
    }

    app.listActions = {
        select,
        onCardClick,
        onCardDoubleClick,
        paste,
        delete: deleteItem,
        toggleFavorite,
        setTag
    };
})(window);
