"use strict";
const host = document.getElementById("terminal");
const bridge = window.chrome.webview;
const terminal = new Terminal({
    cursorBlink: true,
    fontFamily: '"Cascadia Mono", Consolas, monospace',
    fontSize: 13,
    scrollback: 4000,
    theme: { background: "#11151d", foreground: "#dce4f2", cursor: "#a7f3d0", selectionBackground: "#33455e" }
});
const fit = new FitAddon.FitAddon();
terminal.loadAddon(fit);
terminal.open(host);

const send = (type, detail = {}) => bridge.postMessage({type, ...detail});
terminal.onData(data => {
    for (let start = 0; start < data.length;) {
        let end = Math.min(data.length, start + 4000);
        if (end < data.length && /[\uD800-\uDBFF]/.test(data[end - 1])) end--;
        send("input", {text: data.slice(start, end)});
        start = end;
    }
});
terminal.onResize(({cols, rows}) => send("resize", {cols, rows}));
terminal.attachCustomKeyEventHandler(event => {
    if (event.type === "keydown" && event.ctrlKey && !event.shiftKey && event.key.toLowerCase() === "c" && terminal.hasSelection()) {
        send("copy", {text: terminal.getSelection()});
        event.preventDefault();
        return false;
    }
    return true;
});
bridge.addEventListener("message", ({data}) => {
    if (data.type === "output") terminal.write(data.text);
    else if (data.type === "focus") terminal.focus();
    else if (data.type === "reset") terminal.reset();
});
new ResizeObserver(() => fit.fit()).observe(host);
fit.fit();
send("ready", {cols: terminal.cols, rows: terminal.rows});

