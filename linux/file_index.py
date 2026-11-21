"""Optional persistent index of the user's file names and paths."""
import os
from pathlib import Path
import sqlite3
import unicodedata


def normalize(text):
    return "".join(letter for letter in unicodedata.normalize("NFKD", text.casefold()) if not unicodedata.combining(letter))


def build(database, root):
    database.parent.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect(database, timeout=10) as connection:
        connection.execute("BEGIN IMMEDIATE")
        connection.execute("DROP TABLE IF EXISTS files")
        connection.execute("CREATE TABLE files(path TEXT PRIMARY KEY, name TEXT)")
        rows = []
        total = 0
        for folder, subdirectories, files in os.walk(root, followlinks=False):
            subdirectories[:] = [name for name in subdirectories if not name.startswith(".")]
            for name in [*subdirectories, *files]:
                if not name.startswith("."):
                    rows.append((os.path.join(folder, name), normalize(name)))
            if len(rows) >= 4096:
                connection.executemany("INSERT OR IGNORE INTO files VALUES(?, ?)", rows)
                total += len(rows)
                rows = []
            if total >= 500000:
                break
        connection.executemany("INSERT OR IGNORE INTO files VALUES(?, ?)", rows)
        return total + len(rows)


def find(database, term):
    query = normalize(term).replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")
    with sqlite3.connect(database, timeout=2) as connection:
        return [Path(path) for path, in connection.execute(
            "SELECT path FROM files WHERE name LIKE ? ESCAPE '\\' ORDER BY name != ?, length(path) LIMIT 30",
            (f"%{query}%", normalize(term))) if Path(path).exists()]

