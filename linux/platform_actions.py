"""Linux actions selected from the tools and desktop running in this session."""
import configparser
import os
from pathlib import Path
import shutil

from PySide6.QtCore import QProcess


def available(title, *command):
    return (title, lambda: QProcess.startDetached(command[0], list(command[1:]))) if shutil.which(command[0]) else None


def desktop_entries():
    roots = [os.environ.get("XDG_DATA_HOME", str(Path.home() / ".local/share"))]
    roots.extend(os.environ.get("XDG_DATA_DIRS", "/usr/local/share:/usr/share").split(":"))
    roots.extend([str(Path.home() / ".local/share/flatpak/exports/share"),
                  "/var/lib/flatpak/exports/share", "/var/lib/snapd/desktop"])
    if not shutil.which("gio"):
        return []
    entries, seen = [], set()
    current = set(os.environ.get("XDG_CURRENT_DESKTOP", "").split(":"))
    for root in dict.fromkeys(roots):
        applications = Path(root) / "applications"
        for file in applications.rglob("*.desktop"):
            desktop_id = str(file.relative_to(applications)).replace("/", "-")
            if desktop_id in seen:
                continue
            seen.add(desktop_id)
            config = configparser.ConfigParser(interpolation=None, strict=False)
            try:
                config.read(file, encoding="utf-8")
                entry = config["Desktop Entry"]
                if entry.get("Type") != "Application" or any(
                        entry.getboolean(key, fallback=False) for key in ("Hidden", "NoDisplay")):
                    continue
                only = set(entry.get("OnlyShowIn", "").rstrip(";").split(";")) - {""}
                excluded = set(entry.get("NotShowIn", "").rstrip(";").split(";"))
                if only and not only.intersection(current) or excluded.intersection(current):
                    continue
                try_exec = entry.get("TryExec")
                if try_exec and not shutil.which(try_exec):
                    continue
                name = entry.get("Name[pt_BR]") or entry.get("Name[pt]") or entry["Name"]
            except (OSError, UnicodeError, KeyError, ValueError, configparser.Error):
                continue
            entries.append((name, lambda path=str(file): QProcess.startDetached("gio", ["launch", path])))
    return sorted(entries, key=lambda item: item[0].casefold())


def system_actions():
    desktops = os.environ.get("XDG_CURRENT_DESKTOP", "").casefold().split(":")
    presets = [
        ("Bloquear sessão", "loginctl", "lock-session"),
        ("Suspender computador", "systemctl", "suspend"),
        ("Reproduzir ou pausar mídia", "playerctl", "play-pause"),
        ("Próxima faixa", "playerctl", "next"),
        ("Faixa anterior", "playerctl", "previous"),
        ("Alternar mudo do áudio", "wpctl", "set-mute", "@DEFAULT_AUDIO_SINK@", "toggle"),
        ("Abrir arquivos pessoais", "xdg-open", str(Path.home())),
    ]
    if "kde" in desktops or "plasma" in desktops:
        presets.extend([
            ("Configurações do Plasma", "systemsettings"),
            ("Arquivos · Dolphin", "dolphin"),
            ("Monitor do sistema · Plasma", "plasma-systemmonitor"),
            ("Captura de tela · Spectacle", "spectacle"),
        ])
    if "gnome" in desktops:
        presets.extend([
            ("Configurações do GNOME", "gnome-control-center"),
            ("Configurações de tela · GNOME", "gnome-control-center", "display"),
            ("Configurações de rede · GNOME", "gnome-control-center", "network"),
            ("Configurações de notificações · GNOME", "gnome-control-center", "notifications"),
            ("Arquivos · Nautilus", "nautilus"),
        ])
    if "cinnamon" in desktops or "x-cinnamon" in desktops:
        presets.extend([
            ("Configurações do Cinnamon", "cinnamon-settings"),
            ("Configurações de tela · Cinnamon", "cinnamon-settings", "display"),
            ("Configurações de rede · Cinnamon", "cinnamon-settings", "network"),
            ("Configurações de notificações · Cinnamon", "cinnamon-settings", "notifications"),
            ("Arquivos · Nemo", "nemo"),
        ])
    return [item for preset in presets if (item := available(*preset)) is not None]


def package_query():
    for name, arguments in (
        ("apt", ["list", "--upgradable"]),
        ("pacman", ["-Qu"]),
        ("dnf5", ["check-upgrade"]),
        ("dnf", ["check-update"]),
        ("zypper", ["--no-refresh", "list-updates"]),
    ):
        if shutil.which(name):
            return name, arguments
    return None

