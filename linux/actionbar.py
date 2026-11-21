#!/usr/bin/env python3
"""ActionBar desktop launcher for Linux."""
import datetime as dt
import html
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
import subprocess
import sqlite3
from urllib.parse import parse_qs, quote, urlparse

from PySide6.QtCore import QDateTime, QProcess, QSettings, QTimer, QUrl, Qt
from PySide6.QtGui import QColor, QDesktopServices, QIcon, QPalette
from PySide6.QtNetwork import QAbstractSocket, QLocalServer, QLocalSocket, QNetworkAccessManager, QNetworkRequest
from PySide6.QtWebEngineCore import QWebEngineHttpRequest, QWebEnginePage, QWebEngineProfile
from PySide6.QtWebEngineWidgets import QWebEngineView
from PySide6.QtWidgets import (QApplication, QCheckBox, QComboBox, QDateTimeEdit, QFileDialog, QHBoxLayout,
    QLabel, QLineEdit, QListWidget, QListWidgetItem, QMenu, QMessageBox, QPushButton, QSlider,
    QStyle, QSystemTrayIcon, QTabWidget, QTextEdit, QVBoxLayout, QWidget)

from platform_actions import desktop_entries, package_query, system_actions
from screen_portal import ScreenPortal
from desktop import DesktopPanel, calculator
import file_index
from music import MusicPanel
from notifications import NotificationMonitor
from ocr_tools import RegionPicker, translate
from terminal import Terminal
from workspaces import WorkspacesPanel

DATA = Path(os.environ.get("XDG_DATA_HOME", Path.home() / ".local/share")) / "actionbar"
REMINDERS = DATA / "reminders.json"


class BrowserPage(QWebEnginePage):
    def acceptNavigationRequest(self, url, kind, is_main_frame):
        return url.scheme() in {"http", "https", "about", "data", "blob"}

    def createWindow(self, kind):
        return self


class ActionBar(QTabWidget, DesktopPanel, MusicPanel, WorkspacesPanel):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("ActionBar")
        self.resize(740, 480)
        self.profile = QWebEngineProfile(self)
        self.network = QNetworkAccessManager(self)
        self.search_reply = None
        self.notes = set()
        self.video_link = ""
        self.actions = system_actions()
        self.entries = desktop_entries()
        self.file_search = None
        self.find_files = QTimer(self, singleShot=True, interval=250)
        self.find_files.timeout.connect(self.search_files)
        self.alarms = []
        self.file_database = DATA / "files.sqlite"
        self.preferences = QSettings("ActionBar", "ActionBar")
        self.recent = self.preferences.value("recent", [], list)
        self.build_launcher()
        self.build_video()
        self.build_internet()
        self.build_music()
        self.build_terminal()
        self.build_notifications()
        self.build_ocr()
        self.build_reminders()
        self.build_desktop()
        self.build_workspaces(DATA)
        self.build_settings()
        self.poll = QTimer(self)
        self.poll.timeout.connect(self.check_alarms)
        self.poll.start(1000)
        icon = QIcon.fromTheme("system-search", self.style().standardIcon(QStyle.StandardPixmap.SP_ComputerIcon))
        self.setWindowIcon(icon)
        self.tray = QSystemTrayIcon(icon, self)
        self.tray.activated.connect(lambda reason: self.showNormal()
                                    if reason == QSystemTrayIcon.ActivationReason.Trigger else None)
        self.tray_menu = QMenu(self)
        self.tray_menu.addAction("Abrir", self.showNormal)
        self.tray_menu.addAction("Sair", QApplication.quit)
        self.tray.setContextMenu(self.tray_menu)
        self.tray.show()

    def toggle(self):
        if self.isVisible() and self.isActiveWindow():
            self.hide()
        else:
            self.showNormal()
            self.raise_()
            self.activateWindow()
            self.setCurrentIndex(0)
            self.query.clear()
            self.query.setFocus()

    def closeEvent(self, event):
        if self.tray.isSystemTrayAvailable():
            event.ignore()
            self.hide()
        else:
            super().closeEvent(event)

    def tab(self, name):
        container = QWidget()
        layout = QVBoxLayout(container)
        self.addTab(container, name)
        return layout

    @staticmethod
    def row(layout, *items):
        row = QHBoxLayout()
        for widget in items:
            row.addWidget(widget)
        layout.addLayout(row)

    @staticmethod
    def button(label, callback):
        control = QPushButton(label)
        control.clicked.connect(callback)
        return control

    @staticmethod
    def output():
        control = QTextEdit()
        control.setReadOnly(True)
        return control

    def browser(self):
        browser = QWebEngineView()
        browser.setPage(BrowserPage(self.profile, browser))
        return browser

    def build_launcher(self):
        layout = self.tab("Busca")
        self.query = QLineEdit(placeholderText="Aplicativos, arquivos e ações · OCR")
        self.results = QListWidget()
        layout.addWidget(self.query)
        layout.addWidget(self.results)
        self.query.textChanged.connect(self.refresh_launcher)
        self.query.returnPressed.connect(lambda: self.activate_result(self.results.currentItem()))
        self.results.itemActivated.connect(self.activate_result)
        self.refresh_launcher()

    def refresh_launcher(self):
        term = self.query.text().strip().casefold()
        self.results.clear()
        actions = [*self.actions, *self.entries]
        if "wallpaper" in term or "papel de parede" in term:
            self.add_result("Trocar wallpaper · Galeria local", self.wallpaper_gallery, False)
        if "atualiz" in term:
            self.add_result("Verificar atualizações", lambda: self.setCurrentWidget(self.updates_page), False)
        if "pastas" in term:
            self.add_result("Navegar em pastas", lambda: self.open_folder(Path.home()), False)
        value = calculator(term)
        if value is not None:
            self.add_result("Calculadora · " + value, lambda: QApplication.clipboard().setText(value), False)
        if "ocr" in term:
            self.add_result("OCR · Ler texto da tela", self.open_ocr, False)
        candidate = Path(self.query.text()).expanduser()
        if term and candidate.is_absolute() and candidate.exists():
            actions.insert(0, (str(candidate), lambda: self.open_path(candidate)))
        for title, action in [(title, action) for title, action in actions if term in title.casefold()][:80]:
            self.add_result(title, action)
        if not term and self.preferences.value("history", False, bool):
            for title in reversed(self.recent[:3]):
                action = next((callback for label, callback in actions if label == title), None)
                if action:
                    self.add_result("Recente · " + title, action, False, first=True)
        self.results.setCurrentRow(0)
        self.find_files.start()
        if self.file_search is not None:
            previous = self.file_search
            self.file_search = None
            previous.kill()

    def search_files(self):
        term = self.query.text().strip()
        if len(term) < 3:
            return
        if self.file_database.exists() and self.preferences.value("file_index", False, bool):
            def found(paths):
                if self.query.text().strip() != term:
                    return
                for path in paths:
                    self.add_result(str(path), lambda selected=path: self.open_path(selected), False)
                if self.results.currentRow() < 0:
                    self.results.setCurrentRow(0)
            self.run_worker(lambda: file_index.find(self.file_database, term), found, self.index_status)
            return
        if not shutil.which("find"):
            return
        pattern = re.sub(r"([*?\[\\])", r"\\\1", term)
        process = QProcess(self)
        self.file_search = process
        process.setStandardErrorFile(QProcess.nullDevice())
        pending = b""
        count = 0

        def read():
            nonlocal pending, count
            data = bytes(process.readAllStandardOutput())
            if process != self.file_search:
                return
            pieces = (pending + data).split(b"\x00")
            pending = pieces.pop()
            for raw in pieces[:max(0, 30 - count)]:
                path = os.fsdecode(raw)
                item = QListWidgetItem(path)
                item.setData(Qt.ItemDataRole.UserRole, lambda filename=path:
                             self.open_path(Path(filename)))
                self.results.addItem(item)
                count += 1
            if count == 30 and process.state() == QProcess.ProcessState.Running:
                process.kill()
            if self.results.currentRow() < 0:
                self.results.setCurrentRow(0)

        def finished():
            read()
            if process == self.file_search:
                self.file_search = None
            process.deleteLater()

        process.readyReadStandardOutput.connect(read)
        process.finished.connect(finished)
        process.errorOccurred.connect(lambda error: finished()
                                      if error == QProcess.ProcessError.FailedToStart else None)
        process.start("find", [str(Path.home()), "-maxdepth", "6", "-name", ".*", "-prune",
                               "-o", "-iname", "*" + pattern + "*", "-print0"])

    def add_result(self, title, action, record=True, first=False):
        item = QListWidgetItem(title)
        item.setData(Qt.ItemDataRole.UserRole, action)
        item.setData(Qt.ItemDataRole.UserRole + 1, record)
        if first:
            self.results.insertItem(0, item)
        else:
            self.results.addItem(item)

    def activate_result(self, item):
        if item:
            if item.data(Qt.ItemDataRole.UserRole + 1) and self.preferences.value("history", False, bool):
                title = item.text()
                self.recent = [title] + [entry for entry in self.recent if entry != title]
                self.preferences.setValue("recent", self.recent[:20])
            item.data(Qt.ItemDataRole.UserRole)()

    def open_path(self, path):
        if path.is_dir():
            self.open_folder(path)
        else:
            QDesktopServices.openUrl(QUrl.fromLocalFile(str(path)))

    def build_settings(self):
        layout = self.tab("Configurações")
        history = QCheckBox("Guardar histórico de itens usados")
        history.setChecked(self.preferences.value("history", False, bool))
        history.toggled.connect(lambda value: self.preferences.setValue("history", value))
        top = QCheckBox("Manter a barra no topo")
        top.setChecked(self.preferences.value("always_on_top", False, bool))
        def set_top(value):
            self.preferences.setValue("always_on_top", value)
            self.setWindowFlag(Qt.WindowType.WindowStaysOnTopHint, value)
            self.show()
        top.toggled.connect(set_top)
        self.setWindowFlag(Qt.WindowType.WindowStaysOnTopHint, top.isChecked())
        layout.addWidget(history)
        layout.addWidget(top)
        indexed = QCheckBox("Usar o índice persistente na busca de arquivos")
        indexed.setChecked(self.preferences.value("file_index", False, bool))
        indexed.toggled.connect(lambda value: self.preferences.setValue("file_index", value))
        self.index_status = QLabel()
        rebuild = self.button("Escolher pasta e reconstruir índice…", lambda: rebuild_index())
        def rebuild_index():
            root = QFileDialog.getExistingDirectory(self, "Pasta para indexar", str(Path.home()))
            if root:
                rebuild.setEnabled(False)
                self.index_status.setText("Indexando nomes e caminhos…")
                def index():
                    try:
                        return file_index.build(self.file_database, Path(root))
                    except (OSError, sqlite3.Error) as error:
                        return error
                def finished(count):
                    rebuild.setEnabled(True)
                    if isinstance(count, Exception):
                        self.index_status.setText(str(count))
                    else:
                        indexed.setChecked(True)
                        self.index_status.setText(f"{count} itens indexados.")
                self.run_worker(lambda: (index(),), lambda result: finished(result[0]), self.index_status)
        layout.addWidget(indexed)
        layout.addWidget(rebuild)
        layout.addWidget(self.index_status)
        layout.addWidget(QLabel("Atalho global · associe o comando actionbar a um atalho do ambiente gráfico."))
        layout.addStretch()

    def build_video(self):
        layout = self.tab("YouTube")
        self.video_query = QLineEdit(placeholderText="Termo de pesquisa ou link do YouTube")
        self.video_query.returnPressed.connect(self.search_video)
        self.video_status = QLabel()
        self.video_results = QListWidget()
        self.video_results.setMaximumHeight(110)
        self.video_results.itemActivated.connect(lambda item: self.play_video(item.data(Qt.ItemDataRole.UserRole)))
        self.video_browser = self.browser()
        self.row(layout, self.video_query, self.button("Pesquisar", self.search_video),
                 self.button("Copiar link", lambda: QApplication.clipboard().setText(self.video_link) if self.video_link else None))
        layout.addWidget(self.video_status)
        layout.addWidget(self.video_results)
        layout.addWidget(self.video_browser, 1)

    def search_video(self):
        if self.search_reply is not None:
            previous = self.search_reply
            self.search_reply = None
            previous.abort()
        term = self.video_query.text().strip()
        if not term:
            return
        parsed = urlparse(term if "://" in term else "https://" + term)
        if parsed.hostname == "youtu.be":
            self.play_video(parsed.path.strip("/"))
            return
        if parsed.hostname in {"youtube.com", "www.youtube.com", "m.youtube.com"}:
            parts = parsed.path.strip("/").split("/")
            video_id = parts[1] if len(parts) == 2 and parts[0] in {"shorts", "live", "embed"} else parse_qs(parsed.query).get("v", [""])[0]
            self.play_video(video_id)
            return
        key = os.environ.get("ACTIONBAR_YOUTUBE_API_KEY", "")
        if not key:
            self.video_status.setText("Configure ACTIONBAR_YOUTUBE_API_KEY ou cole o link de um vídeo.")
            return
        self.video_results.clear()
        self.video_status.setText("Pesquisando…")
        url = f"https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&maxResults=10&q={quote(term)}&key={quote(key)}"
        request = QNetworkRequest(QUrl(url))
        request.setTransferTimeout(20000)
        reply = self.network.get(request)
        self.search_reply = reply
        reply.finished.connect(lambda: self.video_response(reply))

    def video_response(self, reply):
        try:
            if reply != self.search_reply:
                return
            self.search_reply = None
            payload = json.loads(bytes(reply.readAll()))
            if "items" not in payload:
                self.video_status.setText("Pesquisa indisponível. Confira a conexão, a chave e a cota.")
                return
            for result in payload["items"]:
                item = QListWidgetItem(html.unescape(result["snippet"]["title"]))
                item.setData(Qt.ItemDataRole.UserRole, result["id"]["videoId"])
                self.video_results.addItem(item)
            self.video_status.setText("Dê duplo clique em um vídeo." if self.video_results.count() else "Nenhum vídeo encontrado.")
        except (ValueError, KeyError):
            self.video_status.setText("O YouTube não retornou resultados legíveis.")
        finally:
            reply.deleteLater()

    def play_video(self, video_id):
        if not re.fullmatch(r"[\w-]{11}", video_id, re.ASCII):
            self.video_status.setText("Link inválido.")
            return
        self.video_link = f"https://www.youtube.com/watch?v={video_id}"
        navigation = QWebEngineHttpRequest(QUrl(f"https://www.youtube.com/embed/{video_id}?autoplay=1"))
        navigation.setHeader(b"Referer", b"https://actionbar.local/")
        self.video_browser.load(navigation)
        self.video_status.clear()

    def build_internet(self):
        layout = self.tab("Internet")
        self.internet_page = layout.parentWidget()
        self.address = QLineEdit(placeholderText="Endereço, pesquisa ou webmail")
        self.web = self.browser()
        self.web_status = QLabel()
        self.web.loadFinished.connect(lambda ok: self.web_status.setText("" if ok else "Falha ao carregar a página."))
        self.address.returnPressed.connect(self.navigate)
        self.row(layout, self.button("Voltar", self.web.back), self.address, self.button("Abrir", self.navigate))
        layout.addWidget(self.web_status)
        layout.addWidget(self.web, 1)

    def navigate(self):
        value = self.address.text().strip()
        if not value:
            return
        candidate = QUrl.fromUserInput(value)
        if "://" not in value and ("." not in value.split("/")[0] or any(ch.isspace() for ch in value)):
            candidate = QUrl("https://www.google.com/search?q=" + quote(value))
        if candidate.scheme() in {"http", "https"} and candidate.host():
            self.web.load(candidate)
        else:
            self.web_status.setText("Use HTTP ou HTTPS.")

    def build_terminal(self):
        self.terminal = Terminal(self, self.tab("Terminal"))

    def build_notifications(self):
        layout = self.tab("Notificações")
        self.notice_log = self.output()
        self.notice_log.setPlaceholderText("Notificações recebidas nesta sessão.")
        notice_status = QLabel()
        notice_status.setWordWrap(True)
        self.monitor = NotificationMonitor(self)
        self.monitor.notice.connect(lambda text: self.notice_log.insertPlainText(text.strip() + "\n\n"))
        self.monitor.status.connect(notice_status.setText)
        layout.addWidget(self.button("Ler novas notificações do sistema", self.monitor.start))
        layout.addWidget(notice_status)
        phone = QLineEdit(placeholderText="WhatsApp · número com código do país")

        def message():
            number = re.sub(r"[^0-9]", "", phone.text())
            if 7 <= len(number) <= 15:
                QDesktopServices.openUrl(QUrl("https://wa.me/" + number))
            else:
                QMessageBox.information(self, "WhatsApp", "Informe um número com código do país.")

        self.row(layout, phone, self.button("WhatsApp", message),
                 self.button("Telegram", lambda: QDesktopServices.openUrl(QUrl("tg://"))),
                 self.button("Novo e-mail", lambda: QDesktopServices.openUrl(QUrl("mailto:"))),
                 self.button("Outro webmail", lambda: self.setCurrentWidget(self.internet_page)))
        self.row(layout, self.button("Gmail", lambda: self.open_webmail("https://mail.google.com/")),
                 self.button("Outlook", lambda: self.open_webmail("https://outlook.live.com/mail/")))
        layout.addWidget(self.notice_log)

    def open_webmail(self, url):
        self.setCurrentWidget(self.internet_page)
        self.address.setText(url)
        self.navigate()

    def build_ocr(self):
        layout = self.tab("OCR")
        self.ocr_page = layout.parentWidget()
        self.ocr_output = self.output()
        self.portal = ScreenPortal(self)
        self.portal.finished.connect(self.portal_result)
        self.capture = self.button("Capturar…", self.choose_capture)
        self.translate_auto = QCheckBox("Traduzir após OCR")
        self.translate_language = QComboBox()
        for label, code in [("Português", "pt"), ("Inglês", "en"), ("Espanhol", "es"), ("Francês", "fr"), ("Alemão", "de")]:
            self.translate_language.addItem(label, code)
        self.ocr_translation = self.output()
        self.ocr_translation.setPlaceholderText("Tradução")
        self.ocr_output.setPlaceholderText("Texto reconhecido")
        self.ocr_status = QLabel()
        self.ocr_status.setWordWrap(True)
        self.row(layout, self.capture, self.button("Ler imagem", self.read_image), self.button("Copiar texto",
                 lambda: QApplication.clipboard().setText(self.ocr_output.toPlainText())))
        self.row(layout, self.translate_language, self.button("Traduzir online", self.translate_ocr), self.translate_auto)
        layout.addWidget(self.ocr_status)
        self.row(layout, self.ocr_output, self.ocr_translation)

    def open_ocr(self):
        self.setCurrentWidget(self.ocr_page)
        self.choose_capture()

    def choose_capture(self):
        dialog = QMessageBox(self, windowTitle="OCR · O que você quer ler?", text="Escolha a área a reconhecer.")
        whole = dialog.addButton("Tela inteira", QMessageBox.ButtonRole.AcceptRole)
        region = dialog.addButton("Selecionar área", QMessageBox.ButtonRole.AcceptRole)
        dialog.addButton(QMessageBox.StandardButton.Cancel)
        dialog.exec()
        if dialog.clickedButton() in (whole, region):
            self.start_capture(dialog.clickedButton() == region)

    def translate_ocr(self):
        source = self.ocr_output.toPlainText().strip()
        if source:
            self.ocr_status.setText("Traduzindo…")
            language = self.translate_language.currentData()
            def finished(result):
                self.ocr_translation.setPlainText(result)
                self.ocr_status.setText("Tradução concluída.")
            self.run_worker(lambda: translate(source, language), finished, self.ocr_status)

    def read_image(self):
        file, _ = QFileDialog.getOpenFileName(self, "Selecionar imagem", "", "Imagens (*.png *.jpg *.jpeg *.tif *.bmp *.webp)")
        if file:
            self.run_ocr(file, lambda: None)

    def start_capture(self, region=False):
        from PySide6.QtGui import QCursor
        screen = QApplication.screenAt(QCursor.pos()) or QApplication.primaryScreen()
        self.hide()
        self.capture.setEnabled(False)
        if os.environ.get("XDG_SESSION_TYPE") == "wayland" or os.environ.get("WAYLAND_DISPLAY"):
            QTimer.singleShot(300, self.portal.capture)
        else:
            QTimer.singleShot(300, lambda: self.capture_screen(screen, region))

    def portal_result(self, file, error):
        self.showNormal()
        self.capture.setEnabled(True)
        if error:
            self.ocr_output.setPlainText(error + " Use Ler imagem para abrir uma captura existente.")
        elif file:
            self.run_ocr(file, lambda: None)

    def capture_screen(self, screen, region):
        image = screen.grabWindow(0)
        if region and not image.isNull():
            picker = RegionPicker(image, screen)
            if not picker.exec():
                self.showNormal()
                self.capture.setEnabled(True)
                return
            image = picker.cropped()
        self.showNormal()
        self.capture.setEnabled(True)
        if image.isNull():
            self.ocr_output.setPlainText("Este compositor bloqueou a captura direta. Capture pelo ambiente gráfico e use Ler imagem.")
            return
        with tempfile.NamedTemporaryFile(suffix=".png", delete=False) as temporary:
            file = Path(temporary.name)
        if not image.save(str(file)):
            file.unlink(missing_ok=True)
            self.ocr_output.setPlainText("Falha ao salvar a captura.")
            return
        self.run_ocr(str(file), lambda: file.unlink(missing_ok=True))

    def run_ocr(self, file, cleanup):
        if not shutil.which("tesseract"):
            self.ocr_output.setPlainText("Instale o Tesseract e os dados de idioma português e inglês.")
            cleanup()
            return
        self.ocr_output.clear()
        self.ocr_translation.clear()
        self.ocr_status.setText("Reconhecendo texto…")
        def recognize():
            try:
                result = subprocess.run(["tesseract", file, "stdout", "-l", "por+eng"],
                                                     text=True, capture_output=True, timeout=60)
                if result.returncode:
                    raise RuntimeError(result.stderr.strip())
                return result.stdout.strip()
            finally:
                cleanup()
        def complete(text):
            self.ocr_output.setPlainText(text)
            self.ocr_status.setText("Reconhecimento concluído." if text else "Nenhum texto encontrado.")
            if text and self.translate_auto.isChecked():
                self.translate_ocr()
        self.run_worker(recognize, complete, self.ocr_status)

    def build_reminders(self):
        layout = self.tab("Lembretes")
        self.when = QDateTimeEdit(QDateTime.currentDateTime().addSecs(300), calendarPopup=True)
        self.when.setDisplayFormat("dd/MM/yyyy HH:mm")
        self.note = QLineEdit(placeholderText="Texto do lembrete")
        self.sticky = QCheckBox("Nota adesiva", checked=True)
        self.alarm_list = QListWidget()
        self.alarm_status = QLabel()
        self.row(layout, self.when, self.note, self.sticky, self.button("Agendar", self.add_alarm),
                 self.button("Excluir", self.delete_alarm))
        layout.addWidget(self.alarm_status)
        layout.addWidget(self.alarm_list)
        try:
            if REMINDERS.exists():
                self.alarms = sorted((float(at), str(title), bool(sticky))
                                     for at, title, sticky in json.loads(REMINDERS.read_text(encoding="utf-8")))
        except (OSError, ValueError, TypeError):
            self.alarm_status.setText(f"Falha ao ler {REMINDERS}")
        self.refresh_alarms()

    def save_alarms(self, alarms):
        try:
            DATA.mkdir(parents=True, exist_ok=True)
            temporary = REMINDERS.with_suffix(".tmp")
            temporary.write_text(json.dumps(alarms, ensure_ascii=False), encoding="utf-8")
            temporary.replace(REMINDERS)
        except OSError as error:
            self.alarm_status.setText(str(error))
            return False
        self.alarms = alarms
        self.refresh_alarms()
        self.alarm_status.clear()
        return True

    def refresh_alarms(self):
        self.alarm_list.clear()
        for at, title, _ in self.alarms:
            self.alarm_list.addItem(f"{dt.datetime.fromtimestamp(at):%d/%m %H:%M} · {title}")

    def add_alarm(self):
        title = self.note.text().strip()
        at = self.when.dateTime().toSecsSinceEpoch()
        if not title or at <= dt.datetime.now().timestamp():
            self.alarm_status.setText("Informe texto e horário futuro.")
            return
        if self.save_alarms(sorted([*self.alarms, (at, title, self.sticky.isChecked())])):
            self.note.clear()

    def delete_alarm(self):
        row = self.alarm_list.currentRow()
        if row >= 0:
            self.save_alarms(self.alarms[:row] + self.alarms[row + 1:])

    def check_alarms(self):
        now = dt.datetime.now().timestamp()
        due = [item for item in self.alarms if item[0] <= now]
        if not due or not self.save_alarms([item for item in self.alarms if item[0] > now]):
            return
        QApplication.beep()
        for _, title, sticky in due:
            self.notice_log.insertPlainText(f"{dt.datetime.now():%H:%M} · {title}\n")
            if sticky:
                note = QMessageBox(self, windowTitle="ActionBar · Nota", text=title)
                note.setTextFormat(Qt.TextFormat.PlainText)
                note.setWindowFlag(Qt.WindowType.WindowStaysOnTopHint)
                note.setAttribute(Qt.WidgetAttribute.WA_DeleteOnClose)
                self.notes.add(note)
                note.finished.connect(lambda _, item=note: self.notes.discard(item))
                note.show()
            else:
                self.tray.showMessage("ActionBar · Lembrete", title)


def configure_style(app):
    app.setStyle("Fusion")
    palette = app.palette()
    colors = {"Window": "#202332", "Base": "#171a25", "AlternateBase": "#24283b",
              "Button": "#303549", "WindowText": "#f5f6fc", "Text": "#f5f6fc",
              "ButtonText": "#f5f6fc", "PlaceholderText": "#a6acc6", "Highlight": "#7362ba", "HighlightedText": "#ffffff",
              "ToolTipBase": "#303549", "ToolTipText": "#f5f6fc"}
    for name, value in colors.items():
        palette.setColor(getattr(QPalette.ColorRole, name), QColor(value))
    app.setPalette(palette)


if __name__ == "__main__":
    app = QApplication([])
    app.setApplicationName("ActionBar")
    configure_style(app)
    socket_name = str(Path(os.environ.get("XDG_RUNTIME_DIR", tempfile.gettempdir())) / f"actionbar-{os.getuid()}.sock")
    existing = QLocalSocket()
    existing.connectToServer(socket_name)
    if existing.waitForConnected(300):
        existing.write(b"toggle")
        existing.waitForBytesWritten(300)
        raise SystemExit(0)
    server = QLocalServer()
    server.setSocketOptions(QLocalServer.SocketOption.UserAccessOption)
    if not server.listen(socket_name):
        if server.serverError() != QAbstractSocket.SocketError.AddressInUseError:
            raise SystemExit(server.errorString())
        QLocalServer.removeServer(socket_name)
        if not server.listen(socket_name):
            raise SystemExit(server.errorString())
    window = ActionBar()

    def listen():
        client = server.nextPendingConnection()
        client.readyRead.connect(lambda: (client.readAll(), window.toggle(), client.disconnectFromServer()))
        client.disconnected.connect(client.deleteLater)

    server.newConnection.connect(listen)
    window.show()
    raise SystemExit(app.exec())

