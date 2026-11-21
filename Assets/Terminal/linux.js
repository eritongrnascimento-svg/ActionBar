"use strict";
const host = document.getElementById("terminal");
new QWebChannel(qt.webChannelTransport, channel => {
    const bridge = channel.objects.terminalBridge;
    const terminal = new Terminal({
        cursorBlink: true,
        fontFamily: "monospace",
        fontSize: 13,
        theme: {background: "#11151d", foreground: "#dce4f2", cursor: "#a7f3d0"},
        scrollback: 4000,
    });
    const fit = new FitAddon.FitAddon();
    terminal.loadAddon(fit);
    terminal.open(host);
    terminal.onData(text => bridge.input(text));
    terminal.onResize(({cols, rows}) => bridge.resize(cols, rows));
    bridge.output.connect(text => terminal.write(text));
    bridge.reset.connect(() => terminal.reset());
    new ResizeObserver(() => fit.fit()).observe(host);
    fit.fit();
    bridge.connected(terminal.cols, terminal.rows);
});

