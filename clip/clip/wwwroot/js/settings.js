/* WinClipboard settings UI and persistence */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};
    let maskSaveTimeout;

    async function load() {
        const settings = await app.bridge.send('getSettings');
        if (!settings || app.bridge.isFailure(settings)) return;

        const currentTheme = settings.theme === 2 ? 'light' : 'dark';
        app.state.set({ currentTheme });
        document.body.setAttribute('data-theme', currentTheme);
        document.getElementById('theme-select').value = currentTheme;
        document.getElementById('lang-select').value = settings.language || 'zh';

        const shortcutInput = document.getElementById('shortcut-input');
        if (shortcutInput && settings.code) {
            let modifierStr = [];
            if (settings.modifiers & 0x0002) modifierStr.push("Ctrl");
            if (settings.modifiers & 0x0001) modifierStr.push("Alt");
            if (settings.modifiers & 0x0004) modifierStr.push("Shift");
            if (settings.modifiers & 0x0008) modifierStr.push("Win");
            let display = modifierStr.join(" + ") + (modifierStr.length ? " + " : "") + settings.code;
            shortcutInput.value = display;
        }

        if (settings.bgBase64) {
            setBgImage(settings.bgBase64);
        }

        const maskSlider = document.getElementById('mask-slider');
        if (maskSlider) maskSlider.value = settings.maskOpacity;
        updateMaskVisual(settings.maskOpacity);

        const autostartToggle = document.getElementById('autostart-toggle');
        if (autostartToggle) {
            autostartToggle.checked = !!settings.autostart;
            autostartToggle.addEventListener('change', () => {
                app.bridge.send('setAutostart', { enabled: autostartToggle.checked });
            });
        }
    }

    function toggle() {
        app.overlays.togglePanel('settings');
    }

    function changeTheme(value) {
        app.state.set({ currentTheme: value });
        document.body.setAttribute('data-theme', value);
        app.bridge.send('setTheme', { theme: value === 'dark' ? 1 : 2 });

        const slider = document.getElementById('mask-slider');
        updateMaskVisual(slider ? slider.value : 0.6);
    }

    async function changeLanguage(value) {
        // C# will push the new locale JSON and call applyTranslations().
        await app.bridge.send('setLanguage', { language: value });
        const langSelect = document.getElementById('lang-select');
        if (langSelect) langSelect.value = value;
    }

    async function pickBackground() {
        const res = await app.bridge.send('selectBackgroundImage');
        if (res && res.success && res.base64) {
            setBgImage(res.base64);
        }
    }

    function clearBackground() {
        const bg = document.getElementById('bg-layer');
        bg.classList.remove('active');
        bg.style.backgroundImage = '';
        app.bridge.send('clearBackground');

        const slider = document.getElementById('mask-slider');
        updateMaskVisual(slider ? slider.value : 0.6);
    }

    function setBgImage(base64Data) {
        const bgLayer = document.getElementById('bg-layer');
        bgLayer.style.backgroundImage = `url('${base64Data}')`;
        bgLayer.classList.add('active');

        const slider = document.getElementById('mask-slider');
        updateMaskVisual(slider ? slider.value : 0.6);
    }

    function changeMaskOpacity(value) {
        updateMaskVisual(value);
        clearTimeout(maskSaveTimeout);
        maskSaveTimeout = setTimeout(() => app.bridge.send('setMaskOpacity', { opacity: parseFloat(value) }), 400);
    }

    function updateMaskVisual(opacity) {
        const isImageActive = document.getElementById('bg-layer').classList.contains('active');
        const currentTheme = app.state.get().currentTheme;

        if (isImageActive) {
            if (currentTheme === 'dark') {
                document.documentElement.style.setProperty('--bg-overlay', `rgba(0, 0, 0, ${opacity})`);
            } else {
                document.documentElement.style.setProperty('--bg-overlay', `rgba(255, 255, 255, ${opacity})`);
            }
        } else {
            document.documentElement.style.removeProperty('--bg-overlay');
        }
    }

    function bindEvents() {
        document.getElementById('btn-settings')?.addEventListener('click', toggle);
        document.querySelector('[data-action="close-settings"]')?.addEventListener('click', toggle);
        document.getElementById('lang-select')?.addEventListener('change', event => changeLanguage(event.target.value));
        document.getElementById('theme-select')?.addEventListener('change', event => changeTheme(event.target.value));
        document.getElementById('btn-pick-bg')?.addEventListener('click', pickBackground);
        document.getElementById('btn-clear-bg')?.addEventListener('click', clearBackground);
        document.getElementById('mask-slider')?.addEventListener('change', event => changeMaskOpacity(event.target.value));
    }

    app.settings = {
        load,
        toggle,
        changeTheme,
        changeLanguage,
        pickBackground,
        clearBackground,
        setBgImage,
        changeMaskOpacity,
        updateMaskVisual,
        bindEvents
    };
})(window);
