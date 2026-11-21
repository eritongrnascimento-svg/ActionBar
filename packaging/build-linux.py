"""Package ActionBar for Linux x86-64 using its downloaded wheels."""
import argparse
import gzip
import io
from pathlib import Path
import re
import shutil
import stat
import tarfile
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent.parent
PACKAGING = ROOT / "packaging"
VERSION = re.search(r"<Version>([^<]+)</Version>", (ROOT / "BarraInteligente.csproj").read_text()).group(1)
DEPS = ("python3 (>= 3.10), xdg-utils, libnss3, libnspr4, libdbus-1-3, libglib2.0-0t64 | libglib2.0-0, "
        "libxkbcommon0, libxkbcommon-x11-0, libxcb-cursor0, libxcb-xinerama0, libxcb-icccm4, "
        "libxcb-keysyms1, libxcb-render-util0, libxcb-shape0, libxcb-xfixes0, libxcb-xkb1, "
        "libegl1, libgl1, libgbm1, libxcomposite1, libxdamage1, libxrandr2, libxtst6, "
        "libasound2t64 | libasound2, libfontconfig1, libfreetype6, libxcb-glx0, libpulse0")


def entry(name, size, mode=0o644):
    info = tarfile.TarInfo(name)
    info.size, info.mode = size, mode
    return info


def add(archive, name, data, mode=0o644):
    archive.addfile(entry(name, len(data), mode), io.BytesIO(data))


def read(path):
    return path.read_bytes().replace(b"\r\n", b"\n")


def build_data(target, wheels):
    seen, directories = {}, set()

    def directory(archive, name):
        if name in directories or name == ".":
            return
        directory(archive, name.rpartition("/")[0] or ".")
        info = entry(name, 0, 0o755)
        info.type = tarfile.DIRTYPE
        archive.addfile(info)
        directories.add(name)

    def local(archive, path, name, mode=0o644):
        directory(archive, name.rpartition("/")[0])
        add(archive, name, path.read_bytes() if path.suffix == ".png" else read(path), mode)

    with gzip.GzipFile(str(target), "wb", compresslevel=4, mtime=0) as data, tarfile.open(fileobj=data, mode="w|") as archive:
        home = "usr/lib/actionbar"
        for child in sorted((ROOT / "linux").glob("*.py")):
            local(archive, child, f"{home}/{child.name}")
        for child in sorted((ROOT / "Assets/Terminal").iterdir()):
            if child.is_file():
                local(archive, child, f"{home}/terminal-assets/{child.name}")
        local(archive, PACKAGING / "actionbar-launch", "usr/bin/actionbar", 0o755)
        local(archive, PACKAGING / "actionbar.desktop", "usr/share/applications/actionbar.desktop")
        local(archive, PACKAGING / "actionbar.png", "usr/share/icons/hicolor/256x256/apps/actionbar.png")
        local(archive, ROOT / "linux/README.md", "usr/share/doc/actionbar/README.md")
        for wheel in sorted(wheels):
            with zipfile.ZipFile(wheel) as package:
                for member in package.infolist():
                    if member.is_dir():
                        continue
                    name = f"{home}/runtime/{member.filename}"
                    if name in seen:
                        if seen[name] != member.CRC:
                            raise ValueError(f"Conflicting wheel entry: {name}")
                        continue
                    seen[name] = member.CRC
                    directory(archive, name.rpartition("/")[0])
                    executable = member.external_attr >> 16 & stat.S_IXUSR or member.filename.endswith("/QtWebEngineProcess")
                    with package.open(member) as file:
                        archive.addfile(entry(name, member.file_size, 0o755 if executable else 0o644), file)


def member(destination, name, source):
    length = source.stat().st_size
    header = f"{name}/".ljust(16) + "0".ljust(12) + "0".ljust(6) * 2 + "100644".ljust(8) + str(length).ljust(10) + "\x60\n"
    destination.write(header.encode("ascii"))
    with source.open("rb") as file:
        shutil.copyfileobj(file, destination)
    if length % 2:
        destination.write(b"\n")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--wheels", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    wheels = list(args.wheels.glob("*.whl"))
    expected = {"pyside6", "pyside6_addons", "pyside6_essentials", "shiboken6", "dbus_next"}
    if {path.name.split("-")[0].lower() for path in wheels} != expected or any(
            not ("x86_64" in path.name or "none-any" in path.name) for path in wheels):
        parser.error("provide the five pinned x86-64 or universal wheels")
    args.out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=ROOT.parent) as folder:
        temporary = Path(folder)
        data_tar = temporary / "data.tar.gz"
        build_data(data_tar, wheels)
        control = (
            f"Package: actionbar\nVersion: {VERSION}\nArchitecture: amd64\n"
            "Maintainer: ActionBar Project <actions@users.noreply.github.com>\n"
            "Section: utils\nPriority: optional\n"
            f"Depends: {DEPS}\nRecommends: tesseract-ocr, tesseract-ocr-por, tesseract-ocr-eng, wmctrl\n"
            "Homepage: https://github.com/eritongrnascimento-svg/ActionBar\n"
            "Description: barra de pesquisa e ações de desktop\n Terminal, OCR, mídia, pastas e lembretes.\n"
        ).encode()
        control_tar = temporary / "control.tar.gz"
        with tarfile.open(control_tar, "w:gz") as archive:
            add(archive, "./control", control)
        binary = temporary / "debian-binary"
        binary.write_bytes(b"2.0\n")
        deb = args.out / f"actionbar_{VERSION}_amd64.deb"
        with deb.open("wb") as package:
            package.write(b"!<arch>\n")
            for item in (binary, control_tar, data_tar):
                member(package, item.name, item)
        run = args.out / f"ActionBar-{VERSION}-Linux-x64.run"
        with run.open("wb") as launcher, data_tar.open("rb") as source:
            launcher.write(read(PACKAGING / "linux.run.sh").replace(b"@VERSION@", VERSION.encode()).rstrip(b"\n") + b"\n")
            shutil.copyfileobj(source, launcher)
        run.chmod(0o755)
        for path in (deb, run):
            print(f"{path.name}: {path.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()

