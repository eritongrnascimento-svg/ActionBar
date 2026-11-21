import ast
from concurrent.futures import ThreadPoolExecutor
import math
import os
from pathlib import Path
import re
import shutil
import subprocess

from PySide6.QtCore import QObject, QProcess, QStorageInfo, Qt, QUrl, Signal
from PySide6.QtGui import QDesktopServices, QPixmap
from PySide6.QtWidgets import (QApplication, QCheckBox, QHBoxLayout, QLabel, QLineEdit,
    QListWidget, QListWidgetItem, QMessageBox, QProgressBar, QVBoxLayout, QWidget)

from platform_actions import package_query

PICTURES = {".jpg", ".jpeg", ".png", ".bmp", ".webp"}
WORKERS = ThreadPoolExecutor(max_workers=2, thread_name_prefix="actionbar")


def calculator(text):
    text = text.replace("^", "**").replace(",", ".").strip()
    percent = re.fullmatch(r"(\d+(?:\.\d+)?)\s*%\s+de\s+(\d+(?:\.\d+)?)", text, re.I)
    if percent:
        return f"{float(percent[1]) * float(percent[2]) / 100:.12g}"
    if len(text) > 150 or re.fullmatch(r"[0-9.]+", text):
        return None

    def visit(node):
        if isinstance(node, ast.Constant) and type(node.value) in (int, float):
            return float(node.value)
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
            return visit(node.operand) * (-1 if isinstance(node.op, ast.USub) else 1)
        if isinstance(node, ast.BinOp):
            a, b = visit(node.left), visit(node.right)
            if isinstance(node.op, ast.Add): return a + b
            if isinstance(node.op, ast.Sub): return a - b
            if isinstance(node.op, ast.Mult): return a * b
            if isinstance(node.op, ast.Div): return a / b
            if isinstance(node.op, ast.Mod): return a % b
            if isinstance(node.op, ast.Pow) and abs(b) < 1024: return a ** b
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and len(node.args) == 1 and not node.keywords:
            functions = {"sqrt": math.sqrt, "sin": math.sin, "cos": math.cos, "abs": abs,
                         "log": math.log10, "ln": math.log}
            if node.func.id in functions:
                return functions[node.func.id](visit(node.args[0]))
        raise ValueError

    try:
        return f"{visit(ast.parse(text, mode='eval').body):.12g}"
    except (ValueError, TypeError, SyntaxError, OverflowError, ZeroDivisionError):
        return None


class WorkerSignals(QObject):
    finished = Signal(object, object, object)


class DesktopPanel:
    def build_desktop(self):
        self.workers = WorkerSignals(self)
        self.workers.finished.connect(self.worker_finished, Qt.ConnectionType.QueuedConnection)
        self.folder = Path.home()
        self.build_folders()
        self.build_updates()

    @staticmethod
    def worker_finished(result, callback, status):
        if isinstance(result, BaseException):
            status.setText(str(result))
        else:
            callback(result)

    def run_worker(self, task, callback, status):
        signal = self.workers.finished

        def completed(future):
            try:
                answer = future.result()
            except Exception as error:
                answer = error
            try:
                signal.emit(answer, callback, status)
            except RuntimeError:
                pass

        WORKERS.submit(task).add_done_callback(completed)

    def build_folders(self):
        layout = self.tab("Pastas")
        self.folder_page = layout.parentWidget()
        self.folder_path = QLineEdit(str(self.folder))
        self.folder_status = QLabel()
        self.folder_files = QListWidget()
        self.folder_preview = QLabel(alignment=Qt.AlignmentFlag.AlignCenter)
        self.use_wallpaper = self.button("Aplicar wallpaper", self.apply_wallpaper)
        self.use_wallpaper.setEnabled(False)
        self.row(layout, self.button("Subir", lambda: self.open_folder(self.folder.parent)),
                 self.folder_path, self.button("Ir", lambda: self.open_folder(self.folder_path.text())))
        self.row(layout, self.button("Início", lambda: self.open_folder(self.initial_folder())),
                 self.button("Galeria de wallpapers", self.wallpaper_gallery), self.use_wallpaper)
        self.mount_row = QHBoxLayout()
        layout.addLayout(self.mount_row)
        self.refresh_mounts()
        layout.addWidget(self.folder_status)
        body = QHBoxLayout()
        body.addWidget(self.folder_files, 3)
        body.addWidget(self.folder_preview, 2)
        layout.addLayout(body, 1)
        self.folder_path.returnPressed.connect(lambda: self.open_folder(self.folder_path.text()))
        self.folder_files.itemActivated.connect(self.open_folder_entry)
        self.folder_files.currentItemChanged.connect(lambda item, _: self.preview_folder_entry(item))

    @staticmethod
    def mounted_paths():
        return sorted({volume.rootPath() for volume in QStorageInfo.mountedVolumes()
                       if volume.isValid() and volume.isReady() and
                       (volume.rootPath() == "/" or bytes(volume.device()).startswith(b"/dev/"))})

    def initial_folder(self):
        path = self.preferences.value("browse_home", str(Path.home()), str)
        candidate = Path(path).expanduser()
        return candidate if candidate.is_dir() else Path.home()

    def refresh_mounts(self):
        while self.mount_row.count():
            item = self.mount_row.takeAt(0)
            item.widget().deleteLater()
        all_mounts = self.preferences.value("browse_all_mounts", True, bool)
        selected = self.preferences.value("browse_mounts", [], list)
        for path in self.mounted_paths():
            if all_mounts or path in selected:
                self.mount_row.addWidget(self.button(path, lambda root=path: self.open_folder(root)))

    def open_folder(self, path):
        path = Path(path).expanduser().absolute()
        self.setCurrentWidget(self.folder_page)
        self.folder_request = request = object()
        self.folder_status.setText("Carregando…")

        def entries():
            return [(item, item.is_dir()) for item in path.iterdir() if not item.name.startswith(".")]

        def loaded(items):
            if self.folder_request is request:
                self.folder = path
                self.folder_path.setText(str(path))
                self.show_folder_entries(sorted(items, key=lambda item: (not item[1], item[0].name.casefold())))

        self.run_worker(entries, loaded, self.folder_status)

    def wallpaper_gallery(self):
        self.setCurrentWidget(self.folder_page)
        self.folder_request = request = object()
        self.folder_status.setText("Procurando papéis de parede…")

        def scan():
            pictures = subprocess.run(["xdg-user-dir", "PICTURES"], capture_output=True, text=True).stdout.strip() if shutil.which("xdg-user-dir") else ""
            roots = [Path(pictures) if pictures else Path.home() / "Pictures", Path("/usr/share/backgrounds"), Path("/usr/share/wallpapers")]
            found = []
            for root in roots:
                if not root.is_dir(): continue
                added = 0
                for path in root.rglob("*"):
                    if path.suffix.lower() in PICTURES and path.is_file():
                        found.append((path, False))
                        added += 1
                        if added == 60: break
            return found

        def loaded(items):
            if self.folder_request is request:
                self.show_folder_entries(items)
                self.folder_files.setCurrentRow(0)

        self.run_worker(scan, loaded, self.folder_status)

    def show_folder_entries(self, items):
        self.folder_files.clear()
        for path, is_folder in items:
            item = QListWidgetItem(("▸ " if is_folder else "") + path.name)
            item.setData(Qt.ItemDataRole.UserRole, (str(path), is_folder))
            self.folder_files.addItem(item)
        self.folder_status.setText(f"{len(items)} itens")

    def open_folder_entry(self, item):
        name, folder = item.data(Qt.ItemDataRole.UserRole)
        if folder:
            self.open_folder(name)
        else:
            QDesktopServices.openUrl(QUrl.fromLocalFile(name))

    def preview_folder_entry(self, item):
        self.folder_preview.clear()
        name = item.data(Qt.ItemDataRole.UserRole)[0] if item else ""
        pixmap = QPixmap(name) if Path(name).suffix.lower() in PICTURES else QPixmap()
        self.use_wallpaper.setEnabled(not pixmap.isNull())
        if not pixmap.isNull():
            self.folder_preview.setPixmap(pixmap.scaled(235, 235, Qt.AspectRatioMode.KeepAspectRatio,
                                                       Qt.TransformationMode.SmoothTransformation))

    def apply_wallpaper(self):
        item = self.folder_files.currentItem()
        if item is None:
            return
        file = item.data(Qt.ItemDataRole.UserRole)[0]
        uri = Path(file).as_uri()
        desktop = os.environ.get("XDG_CURRENT_DESKTOP", "").casefold()
        if "cinnamon" in desktop and shutil.which("gsettings"):
            args = ["gsettings", "set", "org.cinnamon.desktop.background", "picture-uri", uri]
        elif "gnome" in desktop and shutil.which("gsettings"):
            args = ["gsettings", "set", "org.gnome.desktop.background", "picture-uri", uri]
            if "prefer-dark" in subprocess.run(["gsettings", "get", "org.gnome.desktop.interface", "color-scheme"],
                                               capture_output=True, text=True).stdout:
                args[3] = "picture-uri-dark"
        elif ("kde" in desktop or "plasma" in desktop) and shutil.which("plasma-apply-wallpaperimage"):
            args = ["plasma-apply-wallpaperimage", file]
        else:
            self.folder_status.setText("O ambiente atual não disponibilizou uma ação de wallpaper.")
            return

        def set_background():
            result = subprocess.run(args, capture_output=True, text=True, timeout=20)
            if result.returncode:
                raise RuntimeError(result.stderr.strip() or "Falha ao aplicar o wallpaper.")
            return "Papel de parede aplicado."

        self.run_worker(set_background, self.folder_status.setText, self.folder_status)

    def build_updates(self):
        layout = self.tab("Atualizações")
        self.updates_page = layout.parentWidget()
        self.updates_status = QLabel("Consultar o gerenciador de pacotes desta distribuição.")
        self.updates_progress = QProgressBar()
        self.updates_progress.hide()
        self.updates_output = self.output()
        self.updates_button = self.button("Verificar atualizações", self.check_updates)
        layout.addWidget(self.updates_button)
        layout.addWidget(self.updates_status)
        layout.addWidget(self.updates_progress)
        layout.addWidget(self.updates_output, 1)
        self.updates_job = QProcess(self)
        self.updates_job.setProcessChannelMode(QProcess.ProcessChannelMode.MergedChannels)
        self.updates_job.readyReadStandardOutput.connect(self.read_update_output)
        self.updates_job.finished.connect(self.end_updates)
        self.updates_job.errorOccurred.connect(lambda error: self.end_updates(-1)
            if error == QProcess.ProcessError.FailedToStart else None)

    def check_updates(self):
        action = package_query()
        if not action:
            self.updates_status.setText("Gerenciador de pacotes não encontrado.")
            return
        self.updates_provider, arguments = action
        self.updates_button.setEnabled(False)
        self.updates_progress.setRange(0, 0)
        self.updates_progress.show()
        self.updates_output.clear()
        self.updates_job.start(self.updates_provider, arguments)

    def read_update_output(self):
        self.updates_output.insertPlainText(bytes(self.updates_job.readAllStandardOutput()).decode(errors="replace"))

    def end_updates(self, code):
        self.read_update_output()
        self.updates_button.setEnabled(True)
        self.updates_progress.setRange(0, 100)
        success = code == 0 or (self.updates_provider in ("dnf", "dnf5") and code == 100) or (
            self.updates_provider == "pacman" and code == 1 and not self.updates_output.toPlainText().strip())
        self.updates_progress.setValue(100 if success else 0)
        self.updates_status.setText("Consulta concluída." if success else f"Falha na consulta · código {code}.")

