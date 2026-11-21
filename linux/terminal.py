"""Local interactive terminal using a PTY and the bundled xterm.js."""
import codecs
import json
import os
from pathlib import Path
import sys

from PySide6.QtCore import QObject, QProcess, Signal, Slot, QUrl
from PySide6.QtWebChannel import QWebChannel
from PySide6.QtWebEngineCore import QWebEnginePage
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWidgets import QLabel


class TerminalPage(QWebEnginePage):
    def __init__(self, url, parent):
        super().__init__(parent)
        self.url = url

    def acceptNavigationRequest(self, url, kind, is_main_frame):
        return is_main_frame and url == self.url


class Terminal(QObject):
    output = Signal(str)
    reset = Signal()

    def __init__(self, app, layout):
        super().__init__(app)
        self.process = QProcess(self)
        self.decoder = codecs.getincrementaldecoder("utf-8")("replace")
        self.size = (80, 24)
        self.ready = False
        self.status = QLabel("Digite diretamente no terminal · Ctrl+C interrompe o comando")
        app.row(layout, app.button("Nova sessão", self.restart), app.button("Interromper", lambda: self.input("\x03")))
        layout.addWidget(self.status)
        self.view = QWebEngineView()
        paths = [Path(__file__).parent / "terminal-assets", Path(__file__).parent.parent / "Assets/Terminal"]
        assets = next((path for path in paths if (path / "xterm.js").exists()), paths[0])
        url = QUrl.fromLocalFile(str(assets / "linux.html"))
        self.view.setPage(TerminalPage(url, self.view))
        channel = QWebChannel(self.view.page())
        channel.registerObject("terminalBridge", self)
        self.view.page().setWebChannel(channel)
        self.view.load(url)
        self.view.loadFinished.connect(lambda ok: self.status.setText("Falha ao carregar os arquivos do terminal.") if not ok else None)
        layout.addWidget(self.view, 1)
        self.process.readyReadStandardOutput.connect(self.read)
        self.process.readyReadStandardError.connect(lambda: self.status.setText(
            bytes(self.process.readAllStandardError()).decode(errors="replace").strip()))
        self.process.finished.connect(self.finished)

    def restart(self):
        self.restarting = True
        if self.process.state() == QProcess.ProcessState.NotRunning:
            self.start()
        else:
            self.send({"close": True})

    def start(self):
        self.restarting = False
        self.decoder.reset()
        self.reset.emit()
        self.process.start(sys.executable, [str(Path(__file__).with_name("terminal_pty.py")), *map(str, self.size)])

    @Slot(int, int)
    def connected(self, columns, rows):
        if self.ready:
            return
        self.ready = True
        self.size = (columns, rows)
        self.start()

    @Slot(str)
    def input(self, text):
        if len(text) <= 65536:
            self.send({"input": text})

    @Slot(int, int)
    def resize(self, columns, rows):
        self.size = (columns, rows)
        self.send({"resize": list(self.size)})

    def send(self, message):
        if self.process.state() == QProcess.ProcessState.Running:
            self.process.write((json.dumps(message) + "\n").encode())

    def read(self):
        chunk = bytes(self.process.readAllStandardOutput())
        self.output.emit(self.decoder.decode(chunk))

    def finished(self):
        self.read()
        if getattr(self, "restarting", False):
            self.start()
        else:
            self.output.emit("\r\nSessão encerrada. Use Nova sessão para continuar.\r\n")

