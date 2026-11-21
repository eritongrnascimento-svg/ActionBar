"""Standalone PTY relay. Runs outside the multithreaded Qt process."""
import errno
import fcntl
import json
import os
import pty
import selectors
import signal
import struct
import sys
import termios


def run(columns, rows):
    child, master = pty.fork()
    if child == 0:
        shell = os.environ.get("SHELL") or "/bin/bash"
        os.chdir(os.path.expanduser("~"))
        os.environ["TERM"] = "xterm-256color"
        os.execv(shell, [shell, "-i"])

    def resize(width, height):
        fcntl.ioctl(master, termios.TIOCSWINSZ, struct.pack("HHHH", max(4, min(height, 200)), max(20, min(width, 400)), 0, 0))

    def write_all(fd, data):
        while data:
            data = data[os.write(fd, data):]

    resize(columns, rows)
    pending = b""
    with selectors.DefaultSelector() as watcher:
        watcher.register(master, selectors.EVENT_READ)
        watcher.register(0, selectors.EVENT_READ)
        try:
            while True:
                for key, _ in watcher.select():
                    if key.fd == master:
                        data = os.read(master, 8192)
                        if not data:
                            return
                        write_all(1, data)
                    else:
                        data = os.read(0, 8192)
                        if not data:
                            return
                        pieces = (pending + data).split(b"\n")
                        pending = pieces.pop()
                        for line in pieces:
                            command = json.loads(line)
                            if command.get("close"):
                                return
                            if "input" in command:
                                write_all(master, command["input"].encode())
                            if "resize" in command:
                                resize(*command["resize"])
        except OSError as error:
            if error.errno not in (errno.EIO, errno.EPIPE):
                raise
        finally:
            os.close(master)
            try:
                os.kill(child, signal.SIGHUP)
            except ProcessLookupError:
                pass
            os.waitpid(child, 0)


if __name__ == "__main__":
    run(*map(int, sys.argv[1:3]))

