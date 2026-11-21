"""Request a user-approved screenshot on desktops with a screenshot portal."""
import asyncio
from threading import Thread
from urllib.parse import unquote, urlparse
from uuid import uuid4

from PySide6.QtCore import QObject, Signal

SERVICE = "org.freedesktop.portal.Desktop"


async def screenshot():
    from dbus_next import BusType, Message, MessageType, Variant
    from dbus_next.aio import MessageBus

    bus = await MessageBus(bus_type=BusType.SESSION).connect()
    token = "actionbar_" + uuid4().hex
    sender_id = bus.unique_name[1:].replace(".", "_")
    path = f"/org/freedesktop/portal/desktop/request/{sender_id}/{token}"
    completed = asyncio.get_running_loop().create_future()

    def message(event):
        if (event.message_type == MessageType.SIGNAL and event.path == path
                and event.interface == "org.freedesktop.portal.Request" and event.member == "Response"
                and not completed.done()):
            completed.set_result(event.body)

    try:
        bus.add_message_handler(message)
        match = await bus.call(Message(
            destination="org.freedesktop.DBus", path="/org/freedesktop/DBus",
            interface="org.freedesktop.DBus", member="AddMatch", signature="s",
            body=[f"type='signal',sender='{SERVICE}',interface='org.freedesktop.portal.Request',path='{path}'"]))
        if match.message_type == MessageType.ERROR:
            raise RuntimeError("Não foi possível escutar a resposta do portal.")

        response = await bus.call(Message(
            destination=SERVICE, path="/org/freedesktop/portal/desktop",
            interface="org.freedesktop.portal.Screenshot", member="Screenshot",
            signature="sa{sv}", body=["", {
                "handle_token": Variant("s", token), "interactive": Variant("b", True)
            }]))
        if response.message_type == MessageType.ERROR:
            raise RuntimeError("O ambiente gráfico não disponibilizou o portal de captura.")

        status, details = await completed
        if status:
            return ""
        uri = urlparse(details["uri"].value)
        if uri.scheme != "file" or uri.netloc not in ("", "localhost"):
            raise RuntimeError("O portal não forneceu um arquivo local.")
        return unquote(uri.path)
    finally:
        bus.disconnect()


class ScreenPortal(QObject):
    finished = Signal(str, str)

    def capture(self):
        def run():
            try:
                path = asyncio.run(asyncio.wait_for(screenshot(), timeout=90))
                self.finished.emit(path, "")
            except Exception as error:
                self.finished.emit("", str(error) or "A captura não terminou.")

        Thread(target=run, daemon=True).start()

