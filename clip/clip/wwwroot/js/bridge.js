/* WinClipboard WebView2 bridge communication */
(function (win) {
    const app = win.WinClipboard = win.WinClipboard || {};
    const pendingRequests = {};
    let requestCounter = 0;

    function failure(error, action) {
        return { success: false, error, action };
    }

    function isFailure(result) {
        return !!result && typeof result === 'object' && result.success === false;
    }

    function logFailure(action, result) {
        if (!isFailure(result)) return;
        const message = `Bridge action "${action}" failed: ${result.error || 'unknown error'}`;
        originalConsoleError.call(console, message);
        if (action !== 'log') {
            send('log', { level: 'error', message });
        }
    }

    function send(action, params = {}) {
        return new Promise((resolve) => {
            const requestId = `req_${++requestCounter}`;
            pendingRequests[requestId] = (data) => {
                logFailure(action, data);
                resolve(data);
            };
            const msg = { action, ...params, requestId };

            if (!win.chrome || !win.chrome.webview || !win.chrome.webview.postMessage) {
                delete pendingRequests[requestId];
                const result = failure('webviewUnavailable', action);
                logFailure(action, result);
                resolve(result);
                return;
            }

            win.chrome.webview.postMessage(msg);

            // Timeout safety (skip for file picker which requires user interaction)
            if (action !== 'selectBackgroundImage') {
                setTimeout(() => {
                    if (pendingRequests[requestId]) {
                        delete pendingRequests[requestId];
                        const result = failure('timeout', action);
                        logFailure(action, result);
                        resolve(result);
                    }
                }, 5000);
            }
        });
    }

    app.bridge = {
        send,
        isFailure,
        _pendingRequests: pendingRequests
    };

    // Called from C# via ExecuteScriptAsync
    win.__bridge_response = function (response) {
        const { requestId, data } = response;
        if (pendingRequests[requestId]) {
            pendingRequests[requestId](data);
            delete pendingRequests[requestId];
        }
    };

    win.__on_clipboard_updated = function () {
        if (app.history && app.history.refreshList) {
            app.history.refreshList();
        }
    };

    // Error logging proxy to C#
    win.onerror = function (msg, url, line, col, error) {
        send('log', { level: 'error', message: `${msg} at ${line}:${col}` });
    };

    const originalConsoleError = console.error;
    console.error = function (...args) {
        send('log', { level: 'error', message: args.join(' ') });
        originalConsoleError.apply(console, args);
    };

    win.addEventListener('error', function (e) {
        alert("JS Error: " + e.message + " at " + e.filename + ":" + e.lineno);
    });

    win.addEventListener('unhandledrejection', function (e) {
        alert("Unhandled Promise Rejection: " + (e.reason && e.reason.message ? e.reason.message : e.reason));
    });
})(window);
