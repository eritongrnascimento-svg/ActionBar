"""Window management over Extended Window Manager Hints on X11."""
import json
import os
from pathlib import Path
import shutil
import subprocess

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QApplication, QLabel, QLineEdit, QListWidget, QListWidgetItem


def available():
    return bool(os.environ.get("DISPLAY") and os.environ.get("XDG_SESSION_TYPE") != "wayland"
                and not os.environ.get("WAYLAND_DISPLAY") and shutil.which("wmctrl"))


def wmctrl(*args):
    return subprocess.run(["wmctrl", *args], check=True, capture_output=True, text=True, timeout=6).stdout


def current_windows():
    windows = []
    for line in wmctrl("-lGx").splitlines():
        columns = line.split(None, 8)
        if len(columns) == 9 and columns[1] != "-1" and "actionbar" not in columns[6].casefold() and columns[8] != "ActionBar":
            windows.append((columns[0], columns[6], columns[8], tuple(map(int, columns[2:6]))))
    return windows


class WorkspacesPanel:
    def build_workspaces(self, data):
        layout = self.tab("Janelas")
        self.workspace_data = data / "workspaces.json"
        self.window_list = QListWidget()
        self.group_list = QListWidget()
        self.group_title = QLineEdit(placeholderText="Nome do grupo")
        self.workspace_status = QLabel()
        self.workspace_status.setWordWrap(True)
        self.row(layout, self.button("Listar janelas", self.refresh_windows),
                 self.button("Focar", self.focus_window), self.button("Encaixar à esquerda", lambda: self.tile_window(False)),
                 self.button("Encaixar à direita", lambda: self.tile_window(True)))
        layout.addWidget(self.window_list, 1)
        self.row(layout, self.group_title, self.button("Salvar layout atual", self.save_layout),
                 self.button("Restaurar layout", self.restore_layout))
        layout.addWidget(self.group_list, 1)
        layout.addWidget(self.workspace_status)
        self.window_list.itemActivated.connect(lambda _: self.focus_window())
        self.group_list.itemActivated.connect(lambda _: self.restore_layout())
        try:
            self.groups = json.loads(self.workspace_data.read_text(encoding="utf-8")) if self.workspace_data.is_file() else {}
        except (OSError, ValueError):
            self.groups = {}
        self.group_list.addItems(sorted(self.groups))
        if not available():
            self.workspace_status.setText("Encaixe e grupos requerem wmctrl numa sessão X11. O Wayland restringe o controle de janelas de outros aplicativos.")

    def workspace_action(self, task, on_result=lambda _: None):
        if available():
            self.run_worker(task, on_result, self.workspace_status)
        else:
            self.workspace_status.setText("A ação requer wmctrl em uma sessão X11.")

    def refresh_windows(self):
        def show(windows):
            self.window_list.clear()
            for address, program, title, _ in windows:
                item = QListWidgetItem(f"{title} · {program}")
                item.setData(Qt.ItemDataRole.UserRole, address)
                self.window_list.addItem(item)
            self.workspace_status.setText(f"{len(windows)} janelas")
        self.workspace_action(current_windows, show)

    def focus_window(self):
        item = self.window_list.currentItem()
        if item:
            address = item.data(Qt.ItemDataRole.UserRole)
            self.workspace_action(lambda: wmctrl("-ia", address))

    def tile_window(self, right):
        item = self.window_list.currentItem()
        if not item:
            return
        address = item.data(Qt.ItemDataRole.UserRole)
        screen = (self.screen() or QApplication.primaryScreen()).availableGeometry()
        width = screen.width() // 2
        x = screen.x() + (width if right else 0)
        area = f"0,{x},{screen.y()},{width},{screen.height()}"
        self.workspace_action(lambda: self.place(address, area))

    @staticmethod
    def place(address, area):
        wmctrl("-ir", address, "-b", "remove,maximized_vert,maximized_horz")
        wmctrl("-ir", address, "-e", area)

    def save_layout(self):
        name = self.group_title.text().strip()
        if not name:
            self.workspace_status.setText("Informe o nome do grupo.")
            return

        def save(windows):
            updated = dict(self.groups, **{name: [(program, title, bounds) for _, program, title, bounds in windows]})
            try:
                self.workspace_data.parent.mkdir(parents=True, exist_ok=True)
                temporary = self.workspace_data.with_suffix(".tmp")
                temporary.write_text(json.dumps(updated, ensure_ascii=False), encoding="utf-8")
                temporary.replace(self.workspace_data)
            except OSError as error:
                self.workspace_status.setText(str(error))
                return
            self.groups = updated
            self.group_list.clear()
            self.group_list.addItems(sorted(self.groups))
            self.workspace_status.setText(f"Layout salvo · {name}")
        self.workspace_action(current_windows, save)

    def restore_layout(self):
        group = self.group_list.currentItem()
        if not group:
            return
        desired = self.groups[group.text()]

        def restore():
            windows = current_windows()
            restored = 0
            for program, title, bounds in desired:
                match = next((window for window in windows if window[1:3] == (program, title)), None)
                if match:
                    windows.remove(match)
                    self.place(match[0], "0," + ",".join(map(str, bounds)))
                    restored += 1
            return restored

        self.workspace_action(restore, lambda count: self.workspace_status.setText(f"{count} janelas restauradas."))

