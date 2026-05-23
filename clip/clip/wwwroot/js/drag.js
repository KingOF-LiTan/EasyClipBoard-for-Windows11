/* WinClipboard native drag initiation */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};

    function setupDragHandle() {
        // Left empty as requested by user to disable custom JS drag handle.
    }

    function initCardDrag() {
        let dragCard = null;
        let dragStartX = 0;
        let dragStartY = 0;
        let dragFired = false;

        document.addEventListener('pointerdown', (e) => {
            if (e.button !== 0) return; // left button only
            const card = e.target.closest('.card-draggable');
            if (!card) return;
            dragCard = card;
            dragStartX = e.clientX;
            dragStartY = e.clientY;
            dragFired = false;
            // Don't preventDefault here, as it kills the native click event for onCardClick.
        });

        document.addEventListener('pointermove', (e) => {
            if (!dragCard || dragFired || e.buttons !== 1) return;

            const dx = e.clientX - dragStartX;
            const dy = e.clientY - dragStartY;

            // Increase threshold slightly so jittered clicks don't become drags and break copy.
            if (Math.hypot(dx, dy) < 12) return;

            dragFired = true;
            const id = parseInt(dragCard.dataset.id, 10);
            if (!isNaN(id)) {
                dragCard.style.opacity = '0.55';

                // Turn on anti-click shield since native drag may synthesize mouse events.
                win.__isDragging = true;

                // Critical: Do NOT await this message; native DoDragDrop blocks.
                app.bridge.send('startDrag', { id });

                setTimeout(() => {
                    if (dragCard) dragCard.style.opacity = '';
                    dragCard = null;
                    setTimeout(() => win.__isDragging = false, 1500);
                }, 100);
            }
        });

        const cleanup = () => {
            if (dragCard) dragCard.style.opacity = '';
            dragCard = null;
        };

        document.addEventListener('click', (e) => {
            if (win.__isDragging) {
                e.stopPropagation();
                e.preventDefault();
            }
        }, true);

        document.addEventListener('pointerup', cleanup);
        document.addEventListener('pointerleave', cleanup);
        document.addEventListener('pointercancel', cleanup);
    }

    app.drag = {
        setupDragHandle,
        initCardDrag
    };

    initCardDrag();
})(window);
