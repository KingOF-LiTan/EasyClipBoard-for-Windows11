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

        // ── Shield duration: 1500ms to cover DoDragDrop and any latent mouse events ──
        const SHIELD_MS = 1500;

        function resetDragState() {
            if (dragCard) {
                dragCard.classList.remove('card-dragging');
                dragCard = null;
            }
            dragFired = false;
            if (win.__isDragging) {
                setTimeout(() => { win.__isDragging = false; }, SHIELD_MS);
            }
        }

        document.addEventListener('pointerdown', (e) => {
            if (e.button !== 0) return; // left button only
            const card = e.target.closest('.card-draggable');
            if (!card) return;
            dragCard = card;
            dragStartX = e.clientX;
            dragStartY = e.clientY;
            dragFired = false;
            // Don't preventDefault here, as it kills the native click event.
        });

        document.addEventListener('pointermove', (e) => {
            if (!dragCard || dragFired || e.buttons !== 1) return;

            const dx = e.clientX - dragStartX;
            const dy = e.clientY - dragStartY;

            if (Math.hypot(dx, dy) < 12) return;

            dragFired = true;
            dragCard.classList.add('card-dragging');
            win.__isDragging = true;

            const id = parseInt(dragCard.dataset.id, 10);
            if (!isNaN(id)) {
                // Fire-and-forget: native DoDragDrop blocks until drag ends
                app.bridge.send('startDrag', { id }).then(() => {
                    // Drag completed on native side — clean up immediately
                    resetDragState();
                }).catch(() => {
                    resetDragState();
                });
            } else {
                resetDragState();
            }
        });

        const cleanup = () => {
            if (dragCard) dragCard.classList.remove('card-dragging');
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
        document.addEventListener('pointercancel', () => {
            resetDragState();
        });
    }

    app.drag = {
        setupDragHandle,
        initCardDrag
    };

    initCardDrag();
})(window);
