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

    async function deleteItem(idx) {
        const item = app.state.getItems()[idx];
        if (!item) return;
        await app.bridge.send('delete', { id: item.id });
        await app.history.refreshList();
    }

    async function toggleFavorite(idx) {
        const item = app.state.getItems()[idx];
        if (!item) return;
        await app.bridge.send('toggleFavorite', { id: item.id });
        await app.history.refreshList();
    }

    async function setTag(idx, tag) {
        const item = app.state.getItems()[idx];
        if (!item) return;
        await app.bridge.send('updateTag', { id: item.id, tag });
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
