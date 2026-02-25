/* ═══════════════════════════════════════════════
   WinClipboard - Web Frontend Application Logic
   ═══════════════════════════════════════════════ */

// ── State ──
let currentTab = 'history';
let items = [];
let selectedIndex = -1;
let settingsOpen = false;
let vaultOpen = false;
let currentTheme = 'dark';
let requestCounter = 0;
const pendingRequests = {};
const thumbnailCache = {}; // id → base64 data URI cache to avoid re-fetching on tab switch

// ── Bridge Communication ──

function sendMessage(action, params = {}) {
    return new Promise((resolve) => {
        const requestId = `req_${++requestCounter}`;
        pendingRequests[requestId] = resolve;
        const msg = { action, ...params, requestId };
        window.chrome.webview.postMessage(msg);
        // Timeout safety
        setTimeout(() => {
            if (pendingRequests[requestId]) {
                delete pendingRequests[requestId];
                resolve(null);
            }
        }, 5000);
    });
}

// Called from C# via ExecuteScriptAsync
window.__bridge_response = function (response) {
    const { requestId, data } = response;
    if (pendingRequests[requestId]) {
        pendingRequests[requestId](data);
        delete pendingRequests[requestId];
    }
};

window.__on_clipboard_updated = function () {
    refreshList();
};

// Error logging proxy to C#
window.onerror = function (msg, url, line, col, error) {
    sendMessage('log', { level: 'error', message: `${msg} at ${line}:${col}` });
};
const originalConsoleError = console.error;
console.error = function (...args) {
    sendMessage('log', { level: 'error', message: args.join(' ') });
    originalConsoleError.apply(console, args);
};

// ── Initialization ──

document.addEventListener('DOMContentLoaded', async () => {
    await loadSettings();
    await refreshList();
    setupKeyboardShortcuts();
    setupDragHandle();
});

function setupDragHandle() {
    let dragLocked = false;
    // Drag on blank areas
    // Strictly excludes any interactive element (including icon children inside buttons)
    document.addEventListener('mousedown', (e) => {
        if (e.button !== 0) return;
        const el = e.target;

        // Block if the clicked element OR any ancestor is interactive
        if (el.closest('button, input, select, textarea, a, [role="button"], .card, .vault-item, .setting-group')) return;

        // Fire drag on any non-interactive element
        if (!dragLocked) {
            e.preventDefault();
            dragLocked = true;
            sendMessage('startDrag').finally(() => {
                setTimeout(() => { dragLocked = false; }, 300); // 300ms debounce
            });
        }
    });
}

async function loadSettings() {
    const settings = await sendMessage('getSettings');
    if (!settings) return;

    // theme: 0=unset(default dark), 1=dark, 2=light
    currentTheme = settings.theme === 2 ? 'light' : 'dark';
    document.body.setAttribute('data-theme', currentTheme);
    document.getElementById('theme-select').value = currentTheme;

    if (settings.bgBase64) {
        setBgImage(settings.bgBase64);
    }

    const maskSlider = document.getElementById('mask-slider');
    maskSlider.value = settings.maskOpacity;
    updateMaskVisual(settings.maskOpacity);

    // Wire autostart toggle
    const autostartToggle = document.getElementById('autostart-toggle');
    autostartToggle.checked = !!settings.autostart;
    autostartToggle.addEventListener('change', () => {
        sendMessage('setAutostart', { enabled: autostartToggle.checked });
    });
}

// ── Tab Switching ──

function switchTab(tab) {
    currentTab = tab;
    document.querySelectorAll('.segment').forEach(s => s.classList.remove('active'));
    document.querySelector(`[data-tab="${tab}"]`).classList.add('active');

    const slider = document.getElementById('segment-slider');
    slider.classList.toggle('right', tab === 'favorites');

    selectedIndex = -1;
    refreshList();
}

// ── List Rendering ──

async function refreshList() {
    const search = document.getElementById('search-input').value;

    // Smart search: type filters
    let typeFilter = null;
    let actualSearch = search;
    if (search.startsWith('/img')) {
        typeFilter = 'image';
        actualSearch = search.slice(4).trim();
    } else if (search.startsWith('/file')) {
        typeFilter = 'files';
        actualSearch = search.slice(5).trim();
    }

    if (currentTab === 'history') {
        items = await sendMessage('getHistory', { search: actualSearch, limit: 200 }) || [];
    } else {
        items = await sendMessage('getFavorites', { search: actualSearch }) || [];
    }

    // Client-side type filter
    if (typeFilter) {
        items = items.filter(i => i.type === typeFilter);
    }

    if (items.length > 0 && selectedIndex < 0) {
        selectedIndex = 0;
    }

    renderList();
}

function renderList() {
    const container = document.getElementById('item-list');
    const emptyHint = document.getElementById('empty-hint');

    if (!items || items.length === 0) {
        container.innerHTML = '';
        emptyHint.classList.remove('hidden');
        return;
    }

    emptyHint.classList.add('hidden');

    // Disconnect previous observer
    if (renderList._observer) renderList._observer.disconnect();

    container.innerHTML = items.map((item, idx) => {
        const isSelected = idx === selectedIndex;
        const shortcut = idx < 9 ? `${idx + 1}` : '';
        const hasImage = item.hasImage;

        // Color dot
        let colorDot = '';
        if (item.colorHex) {
            colorDot = `<div class="card-color-dot" style="background:${item.colorHex}"></div>`;
        }

        // Body: image placeholder or text preview
        let imageHtml = '';
        let bodyHtml = '';
        if (hasImage) {
            const cached = thumbnailCache[item.id];
            if (cached) {
                imageHtml = `<img class="card-image" src="${cached}" alt="image" onclick="event.stopPropagation(); showPreview(${idx})">`;
            } else {
                imageHtml = `<div class="card-image-placeholder" data-id="${item.id}" data-idx="${idx}"></div>`;
            }
        } else {
            bodyHtml = `<div class="card-preview">${escapeHtml(item.preview || '')}</div>`;
        }

        const tagIcon = getTagIcon(item.tag);
        const typeIcon = getTypeIcon(item.type);

        return `
            <div class="card ${isSelected ? 'selected' : ''} ${hasImage ? 'card-has-image' : ''}" 
                 data-idx="${idx}" 
                 onclick="onCardClick(${idx})" 
                 ondblclick="onCardDoubleClick(${idx})">
                ${shortcut ? `<span class="card-shortcut">${shortcut}</span>` : ''}
                <div class="card-actions">
                    <button class="card-action-btn" onclick="event.stopPropagation(); toggleFavorite(${idx})" title="收藏">
                        ${item.isFavorite ? '★' : '☆'}
                    </button>
                    <button class="card-action-btn" onclick="event.stopPropagation(); setTag(${idx}, 'Important')" title="重要" style="color:var(--tag-important)">●</button>
                    <button class="card-action-btn danger" onclick="event.stopPropagation(); deleteItem(${idx})" title="删除">✕</button>
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

            const res = await sendMessage('getImageThumbnail', { id });
            if (res && res.success && res.base64) {
                thumbnailCache[id] = res.base64;
                const live = document.querySelector(`.card-image-placeholder[data-id="${id}"]`);
                if (live) replacePlaceholder(live, res.base64, idx);
            }
        });
    }, { root: document.getElementById('list-container'), rootMargin: '100px' });

    document.querySelectorAll('.card-image-placeholder').forEach(el => observer.observe(el));
    renderList._observer = observer;
}

function replacePlaceholder(placeholder, base64, idx) {
    const img = document.createElement('img');
    img.className = 'card-image';
    img.src = base64;
    img.alt = 'image';
    img.onclick = (e) => { e.stopPropagation(); showPreview(idx); };
    placeholder.replaceWith(img);
}

function getTagIcon(tag) {
    switch (tag) {
        case 'Important': return '<span style="color:var(--tag-important)">★</span>';
        case 'Frequent': return '<span style="color:var(--tag-frequent)">📌</span>';
        case 'Script': return '<span style="color:var(--tag-script)">💬</span>';
        case 'Temporary': return '<span style="color:var(--tag-temporary)">🏷️</span>';
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

// ── Card Interactions ──

function onCardClick(idx) {
    // Single click = select/highlight only
    selectedIndex = idx;
    renderList();
}

async function onCardDoubleClick(idx) {
    // Double click = paste and close
    await pasteItem(idx);
}

async function pasteItem(idx) {
    const item = items[idx];
    if (!item) return;
    await sendMessage('paste', { id: item.id });
    await sendMessage('hideWindow');
}

async function deleteItem(idx) {
    const item = items[idx];
    if (!item) return;
    await sendMessage('delete', { id: item.id });
    await refreshList();
}

async function toggleFavorite(idx) {
    const item = items[idx];
    if (!item) return;
    await sendMessage('toggleFavorite', { id: item.id });
    await refreshList();
}

async function setTag(idx, tag) {
    const item = items[idx];
    if (!item) return;
    await sendMessage('updateTag', { id: item.id, tag });
    await refreshList();
}

// ── Preview ──

function showPreview(idx) {
    const item = items[idx];
    if (!item) return;

    const modal = document.getElementById('preview-modal');
    const content = document.getElementById('preview-content');

    const cachedImg = item.hasImage ? thumbnailCache[item.id] : null;
    if (item.hasImage && cachedImg) {
        content.innerHTML = `<img src="${cachedImg}" alt="preview">`;
    } else if (item.hasImage && !cachedImg) {
        // Not in cache yet - fetch it
        content.innerHTML = '<div style="text-align:center;padding:24px;color:var(--text-muted)">加载中...</div>';
        modal.classList.remove('hidden');
        sendMessage('getImageThumbnail', { id: item.id }).then(res => {
            if (res && res.success && res.base64) {
                thumbnailCache[item.id] = res.base64;
                document.getElementById('preview-content').innerHTML = `<img src="${res.base64}" alt="preview">`;
                // Also update any live placeholder on the list
                const live = document.querySelector(`.card-image-placeholder[data-id="${item.id}"]`);
                if (live) replacePlaceholder(live, res.base64, idx);
            }
        });
        return;
    } else {
        content.innerHTML = `<pre>${escapeHtml(item.preview || '[空]')}</pre>`;
    }

    modal.classList.remove('hidden');
}

function closePreview() {
    document.getElementById('preview-modal').classList.add('hidden');
}

// ── Keyboard Shortcuts ──

function setupKeyboardShortcuts() {
    document.addEventListener('keydown', async (e) => {
        const isSearchInput = document.activeElement?.id === 'search-input';
        const inInput = document.activeElement?.tagName === 'INPUT' || document.activeElement?.tagName === 'TEXTAREA' || document.activeElement?.tagName === 'SELECT';

        // Escape -> close panels/preview or hide window
        if (e.key === 'Escape') {
            if (!document.getElementById('preview-modal').classList.contains('hidden')) {
                closePreview();
                return;
            }
            if (document.getElementById('vault-add-modal') && !document.getElementById('vault-add-modal').classList.contains('hidden')) {
                closeVaultAddModal();
                return;
            }
            if (document.getElementById('confirm-modal') && !document.getElementById('confirm-modal').classList.contains('hidden')) {
                document.getElementById('confirm-modal').classList.add('hidden');
                return;
            }
            if (settingsOpen) { toggleSettings(); return; }
            if (vaultOpen) { toggleVault(); return; }
            sendMessage('hideWindow');
            return;
        }

        // Ctrl+F -> focus search
        if (e.ctrlKey && e.key.toLowerCase() === 'f') {
            e.preventDefault();
            document.getElementById('search-input').focus();
            return;
        }

        // Arrow up/down: navigate list everywhere
        if (e.key === 'ArrowDown') {
            if (inInput && !isSearchInput) return; // Allow only in search input, not vault/settings input
            e.preventDefault(); // Prevent cursor moving in search input
            if (selectedIndex < items.length - 1) {
                selectedIndex++;
                renderList();
                scrollToSelected();
            }
            return;
        }
        if (e.key === 'ArrowUp') {
            if (inInput && !isSearchInput) return; // Allow only in search input, not vault/settings input
            e.preventDefault();
            if (selectedIndex > 0) {
                selectedIndex--;
                renderList();
                scrollToSelected();
            }
            return;
        }

        // Enter: paste selected
        if (e.key === 'Enter') {
            if (inInput && !isSearchInput) return; // Typing enter in textarea or vault input = normal behavior
            e.preventDefault();
            if (isSearchInput) {
                document.activeElement.blur();
            }
            if (selectedIndex >= 0 && selectedIndex < items.length && items.length > 0) {
                await pasteItem(selectedIndex);
            }
            return;
        }

        // If inside any input at this point, do not handle space or quick paste
        if (inInput) return;

        // Space: preview selected
        if (e.key === ' ' && selectedIndex >= 0) {
            e.preventDefault();
            showPreview(selectedIndex);
            return;
        }

        // Number keys 1-9: quick paste
        if (e.key >= '1' && e.key <= '9') {
            const idx = parseInt(e.key) - 1;
            if (idx >= 0 && idx < items.length) {
                e.preventDefault();
                await pasteItem(idx);
            }
            return;
        }
    });

    // Search input debounce (real-time updating)
    let searchTimeout;
    document.getElementById('search-input').addEventListener('input', () => {
        clearTimeout(searchTimeout);
        searchTimeout = setTimeout(() => {
            selectedIndex = items.length > 0 ? 0 : -1;
            refreshList();
        }, 50);
    });
}

function scrollToSelected() {
    const card = document.querySelector(`.card[data-idx="${selectedIndex}"]`);
    if (card) {
        card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
}

// ── Settings ──

function toggleSettings() {
    settingsOpen = !settingsOpen;
    document.getElementById('settings-panel').classList.toggle('hidden', !settingsOpen);
    if (vaultOpen) { vaultOpen = false; document.getElementById('vault-panel').classList.add('hidden'); }
}

function changeTheme(value) {
    currentTheme = value;
    document.body.setAttribute('data-theme', value);
    sendMessage('setTheme', { theme: value === 'dark' ? 1 : 2 });
}

async function pickBackground() {
    const res = await sendMessage('selectBackgroundImage');
    if (res && res.success && res.base64) {
        setBgImage(res.base64);
    }
}

function clearBackground() {
    const bg = document.getElementById('bg-layer');
    bg.classList.remove('active');
    bg.style.backgroundImage = '';
    sendMessage('clearBackground');
}

function setBgImage(base64Data) {
    const bgLayer = document.getElementById('bg-layer');
    bgLayer.style.backgroundImage = `url('${base64Data}')`;
    bgLayer.classList.add('active');
}

let maskSaveTimeout;
function updateMask(value) {
    updateMaskVisual(value);
    // Debounce saving to avoid spamming the bridge
    clearTimeout(maskSaveTimeout);
    maskSaveTimeout = setTimeout(() => sendMessage('setMaskOpacity', { opacity: parseFloat(value) }), 400);
}

function updateMaskVisual(opacity) {
    const overlay = document.getElementById('overlay-layer');
    if (currentTheme === 'dark') {
        overlay.style.background = `rgba(0, 0, 0, ${opacity})`;
    } else {
        overlay.style.background = `rgba(255, 255, 255, ${opacity})`;
    }
}

// ── Vault (Sensitive Items) ──

function toggleVault() {
    vaultOpen = !vaultOpen;
    document.getElementById('vault-panel').classList.toggle('hidden', !vaultOpen);
    if (settingsOpen) { settingsOpen = false; document.getElementById('settings-panel').classList.add('hidden'); }
    if (vaultOpen) refreshVault();
}

async function refreshVault(search) {
    const data = await sendMessage('getSensitiveItems', { search: search || '' });
    if (!data) return;
    renderVault(data);
}

function renderVault(vaultItems) {
    const container = document.getElementById('vault-list');
    if (!vaultItems || vaultItems.length === 0) {
        container.innerHTML = '<div style="text-align:center;color:var(--text-muted);padding:20px">暂无保存的敏感信息</div>';
        return;
    }

    container.innerHTML = vaultItems.map(item => {
        const icon = getSensitiveIcon(item.sensitiveType);
        const name = item.alias || item.username || '未命名';
        const sub = item.username ? `👤 ${item.username}` : (item.sensitiveType || '');
        const remarkHtml = item.remark ? `<div class="vault-remark" style="font-size: 11px; color: var(--text-muted); margin-top: 2px;">💬 ${escapeHtml(item.remark)}</div>` : '';

        return `
            <div class="vault-item">
                <span class="vault-icon">${icon}</span>
                <div class="vault-info">
                    <div class="vault-name">${escapeHtml(name)}</div>
                    <div class="vault-sub">${escapeHtml(sub)}</div>
                    ${remarkHtml}
                </div>
                <div class="vault-actions">
                    ${item.username ? `<button class="card-action-btn" onclick="copyUsername(${item.id})" title="复制账号">👤</button>` : ''}
                    <button class="card-action-btn" onclick="copySecret(${item.id})" title="复制密码">🔑</button>
                    <button class="card-action-btn danger" onclick="deleteSecret(${item.id})" title="删除">✕</button>
                </div>
            </div>
        `;
    }).join('');
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
    const result = await sendMessage('decryptSecret', { id });
    if (result?.text) {
        // Copy via C# bridge (pasteText action)
        await sendMessage('pasteText', { text: result.text });
        await sendMessage('hideWindow');
    }
}

async function copyUsername(id) {
    const result = await sendMessage('getUsername', { id });
    if (result?.success && result.text) {
        await sendMessage('pasteText', { text: result.text });
        await sendMessage('hideWindow');
    }
}

async function deleteSecret(id) {
    await sendMessage('deleteSecret', { id });
    await refreshVault();
}

function searchVault(query) {
    refreshVault(query);
}

function addSecret() {
    document.getElementById('vault-add-alias').value = '';
    document.getElementById('vault-add-username').value = '';
    document.getElementById('vault-add-password').value = '';
    document.getElementById('vault-add-remark').value = '';
    document.getElementById('vault-add-modal').classList.remove('hidden');
    document.getElementById('vault-add-alias').focus();
}

function closeVaultAddModal() {
    document.getElementById('vault-add-modal').classList.add('hidden');
}

async function submitVaultAdd() {
    const alias = document.getElementById('vault-add-alias').value.trim() || '未命名';
    const username = document.getElementById('vault-add-username').value.trim() || undefined;
    const content = document.getElementById('vault-add-password').value.trim();
    const remark = document.getElementById('vault-add-remark').value.trim() || undefined;

    if (!content) {
        alert("密码内容不能为空！");
        return;
    }

    // Defaulting to Password as requested (no need for specific types)
    await sendMessage('addSecret', {
        alias,
        username,
        content,
        remark,
        sensitiveType: 'Password'
    });

    closeVaultAddModal();
    await refreshVault();
}

// ── Clear History ──

function clearHistory() {
    showConfirm('确定清空所有历史记录？此操作不可恢复。', async () => {
        await sendMessage('clearHistory');
        await refreshList();
    });
}

// ── Custom Confirm Modal ──

function showConfirm(message, onConfirm) {
    const modal = document.getElementById('confirm-modal');
    document.getElementById('confirm-message').textContent = message;
    modal.classList.remove('hidden');
    // Wire buttons (once - remove old listeners by cloning)
    const btnOk = document.getElementById('confirm-ok');
    const btnCancel = document.getElementById('confirm-cancel');
    const newOk = btnOk.cloneNode(true);
    const newCancel = btnCancel.cloneNode(true);
    btnOk.parentNode.replaceChild(newOk, btnOk);
    btnCancel.parentNode.replaceChild(newCancel, btnCancel);
    newOk.addEventListener('click', () => { modal.classList.add('hidden'); onConfirm(); });
    newCancel.addEventListener('click', () => { modal.classList.add('hidden'); });
}

// ── Window Appear Animation ──

function playShowAnimation() {
    document.body.classList.remove('anim-hide');
    document.body.classList.add('anim-show');
}

function playHideAnimation(callback) {
    document.body.classList.remove('anim-show');
    document.body.classList.add('anim-hide');
    setTimeout(callback, 180);
}

// Override hideWindow to animate first
async function hideWindowAnimated() {
    playHideAnimation(() => sendMessage('hideWindow'));
}

// Called from C# after window is shown
window.__on_window_shown = function () {
    playShowAnimation();
    refreshList();

    // Explicitly focus the search input so keyboard navigation works immediately
    setTimeout(() => {
        const searchInput = document.getElementById('search-input');
        if (searchInput && currentTab !== 'favorites' && !vaultOpen && !settingsOpen) {
            searchInput.focus();
        } else {
            document.body.focus();
        }
    }, 50);
};
