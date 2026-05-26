/* WinClipboard card list rendering */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    function render() {
        const container = document.getElementById('item-list');
        const emptyHint = document.getElementById('empty-hint');
        const items = app.state.getItems();
        const selectedIndex = app.state.get().selectedIndex;
        const thumbnailCache = app.state.getThumbnailCache();

        if (!items || items.length === 0) {
            container.innerHTML = '';
            emptyHint.classList.remove('hidden');
            return;
        }

        emptyHint.classList.add('hidden');

        // Disconnect previous observer
        if (render._observer) render._observer.disconnect();

        container.innerHTML = items.map((item, idx) => {
            const isSelected = idx === selectedIndex;
            const shortcut = idx < 9 ? `${idx + 1}` : '';
            const hasImage = item.hasImage;

            let colorDot = '';
            if (item.colorHex) {
                colorDot = `<div class="card-color-dot" data-color="${escapeHtml(item.colorHex)}"></div>`;
            }

            let imageHtml = '';
            let bodyHtml = '';
            if (hasImage) {
                const cached = thumbnailCache[item.id];
                if (cached) {
                    imageHtml = `<img class="card-image" src="${cached}" alt="image" data-idx="${idx}">`;
                } else {
                    imageHtml = `<div class="card-image-placeholder" data-id="${item.id}" data-idx="${idx}"></div>`;
                }
            } else {
                bodyHtml = `<div class="card-preview">${escapeHtml(item.preview || '')}</div>`;
            }

            const tagIcon = getTagIcon(item.tag);
            const typeIcon = getTypeIcon(item.type);

            return `
                <div class="card ${isSelected ? 'selected' : ''} ${hasImage ? 'card-has-image' : ''} card-draggable"
                     data-idx="${idx}"
                     data-id="${item.id}"
                     data-type="${item.type}">
                    ${shortcut ? `<span class="card-shortcut">${shortcut}</span>` : ''}
                    <div class="card-actions">
                        <button class="card-action-btn paste-btn" data-action="paste" data-idx="${idx}" title="${app.i18n.t('action.paste')}">→</button>
                        <button class="card-action-btn" data-action="preview" data-idx="${idx}" title="${app.i18n.t('action.preview')}">🔍</button>
                        <button class="card-action-btn" data-action="favorite" data-idx="${idx}" title="${app.i18n.t('action.pin')}">
                            ${item.isFavorite ? '★' : '☆'}
                        </button>
                        <button class="card-action-btn tag-important-text" data-action="important" data-idx="${idx}" title="${app.i18n.t('action.important')}">●</button>
                        <button class="card-action-btn danger" data-action="delete" data-idx="${idx}" title="${app.i18n.t('action.delete')}">✕</button>
                    </div>
                    ${imageHtml}
                    <div class="card-body">
                        ${colorDot}
                        <div class="card-content">
                            ${bodyHtml}
                            <div class="card-meta">
                                <span class="card-time">${typeIcon} ${item.timeAgo}</span>
                                <span class="card-tag">${tagIcon}</span>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        }).join('');

        // Lazy-load thumbnails using IntersectionObserver
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(async entry => {
                if (!entry.isIntersecting) return;
                const placeholder = entry.target;
                const id = parseInt(placeholder.dataset.id);
                const idx = parseInt(placeholder.dataset.idx);
                observer.unobserve(placeholder);

                if (thumbnailCache[id]) {
                    replacePlaceholder(placeholder, thumbnailCache[id], idx);
                    return;
                }

                const res = await app.bridge.send('getImageThumbnail', { id });
                if (res && res.success && res.base64) {
                    thumbnailCache[id] = res.base64;
                    const live = document.querySelector(`.card-image-placeholder[data-id="${id}"]`);
                    if (live) replacePlaceholder(live, res.base64, idx);
                }
            });
        }, { root: document.getElementById('list-container'), rootMargin: '100px' });

        document.querySelectorAll('.card-image-placeholder').forEach(el => observer.observe(el));
        document.querySelectorAll('.card-color-dot[data-color]').forEach(el => {
            el.style.backgroundColor = el.dataset.color;
        });
        bindListEvents(container);
        render._observer = observer;
    }

    function bindListEvents(container) {
        if (container.dataset.eventsBound === '1') return;
        container.dataset.eventsBound = '1';

        // Double-click detection via click counter (avoids native dblclick issues with
        // innerHTML re-render, drag pointer handling, and anti-click shield timing).
        let lastClickTime = 0;
        let lastClickIdx = -1;
        const DBLCLICK_WINDOW = 400;

        container.addEventListener('click', event => {
            const actionButton = event.target.closest('[data-action]');
            if (actionButton) {
                event.stopPropagation();
                const idx = parseInt(actionButton.dataset.idx, 10);
                switch (actionButton.dataset.action) {
                    case 'paste':
                        app.listActions.paste(idx);
                        return;
                    case 'preview':
                        app.overlays.showPreview(idx);
                        return;
                    case 'favorite':
                        app.listActions.toggleFavorite(idx);
                        return;
                    case 'important':
                        app.listActions.setTag(idx, 'Important');
                        return;
                    case 'delete':
                        app.listActions.delete(idx);
                        return;
                }
            }

            const card = event.target.closest('.card');
            if (!card) return;

            const idx = parseInt(card.dataset.idx, 10);
            const now = Date.now();

            if (idx === lastClickIdx && (now - lastClickTime) < DBLCLICK_WINDOW) {
                // Double-click detected
                app.listActions.onCardDoubleClick(idx);
                lastClickTime = 0;
                lastClickIdx = -1;
                return;
            }

            lastClickTime = now;
            lastClickIdx = idx;
            app.listActions.onCardClick(idx);
        });
    }

    function replacePlaceholder(placeholder, base64, idx) {
        const img = document.createElement('img');
        img.className = 'card-image';
        img.src = base64;
        img.alt = 'image';
        img.dataset.idx = idx;
        placeholder.replaceWith(img);
    }

    function getTagIcon(tag) {
        switch (tag) {
            case 'Important': return '<span class="tag-important-text">★</span>';
            case 'Frequent': return '<span class="tag-frequent-text">📌</span>';
            case 'Script': return '<span class="tag-script-text">💬</span>';
            case 'Temporary': return '<span class="tag-temporary-text">🏷️</span>';
            default: return '';
        }
    }

    function getTypeIcon(type) {
        switch (type) {
            case 'text': return '📝';
            case 'image': return '🖼️';
            case 'files': return '📁';
            default: return '📋';
        }
    }

    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function scrollToSelected() {
        const selectedIndex = app.state.get().selectedIndex;
        const card = document.querySelector(`.card[data-idx="${selectedIndex}"]`);
        if (card) {
            card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }

    app.listView = {
        render,
        replacePlaceholder,
        getTagIcon,
        getTypeIcon,
        escapeHtml,
        scrollToSelected
    };

    // All listView functions are accessed via app.listView.* namespace
})(window);
