(() => {
    const term = new window.Terminal({
        allowProposedApi: true,
        convertEol: true,
        disableStdin: false,
        fontFamily: "'Cascadia Code', 'Consolas', 'Segoe UI', monospace",
        fontSize: 13,
        cursorBlink: true,
        cursorStyle: 'bar',
        theme: {
            background: '#0f172a',
            foreground: '#e2e8f0',
            cursor: '#38bdf8',
            selection: 'rgba(56, 189, 248, 0.35)'
        }
    });

    const fitAddon = new window.FitAddon.FitAddon();
    const linkAddon = new window.WebLinksAddon.WebLinksAddon((event, uri) => {
        if (window.chrome?.webview) {
            window.chrome.webview.postMessage({ type: 'open-link', uri });
        } else {
            window.open(uri, '_blank');
        }
    });

    term.loadAddon(fitAddon);
    term.loadAddon(linkAddon);

    const terminalRef = document.getElementById('terminal');
    term.open(terminalRef);

    const post = (payload) => window.chrome?.webview?.postMessage(payload);

    const registerPresetButtons = () => {
        const container = document.querySelector('[data-role="preset-commands"]');
        if (!container) {
            return;
        }

        container.addEventListener('click', event => {
            const button = event.target?.closest?.('button[data-command]');
            if (!button) {
                return;
            }

            const command = button.getAttribute('data-command');
            if (!command) {
                return;
            }

            post({ type: 'input', data: command });
            term.focus();
        });
    };

    const sendSize = () => {
        fitAddon.fit();
        if (typeof term.cols === 'number' && typeof term.rows === 'number') {
            post({ type: 'resize', cols: term.cols, rows: term.rows });
        }
    };

    const notifyReady = () => {
        sendSize();
        post({ type: 'ready', cols: term.cols, rows: term.rows });
    };

    window.addEventListener('resize', () => {
        sendSize();
    });

    term.onData(data => {
        post({ type: 'input', data });
    });

    term.attachCustomKeyEventHandler(event => {
        const ctrl = event.ctrlKey || event.metaKey;
        if (ctrl && event.shiftKey && event.code === 'KeyC' && term.hasSelection()) {
            const selection = term.getSelection();
            if (selection && navigator.clipboard?.writeText) {
                navigator.clipboard.writeText(selection).catch(() => { /* ignore */ });
            }
            return false;
        }

        if (ctrl && event.shiftKey && event.code === 'KeyV') {
            if (navigator.clipboard?.readText) {
                navigator.clipboard.readText().then(text => {
                    if (text) {
                        post({ type: 'input', data: text });
                    }
                }).catch(() => { /* ignore */ });
            }
            return false;
        }

        return true;
    });

    if (window.chrome?.webview) {
        window.chrome.webview.addEventListener('message', event => {
            const { type, data } = event.data ?? {};
            if (type === 'output' && typeof data === 'string') {
                term.write(data);
            } else if (type === 'clear') {
                term.clear();
            } else if (type === 'reset') {
                term.reset();
                notifyReady();
            } else if (type === 'focus') {
                term.focus();
            }
        });
    }

    registerPresetButtons();
    notifyReady();
    term.focus();
})();
