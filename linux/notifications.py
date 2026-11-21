"""Read new desktop notices when the session bus allows a monitor connection."""
import asyncio
from threading import Thread

from PySide6.QtCore import QObject, Signal
from dbus_next import BusType, Message, MessageType
from dbus_next.aio import MessageBus


class NotificationMonitor(QObject):
    notice = Signal(str)
    status = Signal(str)

    def __init__(self, parent=None):
        super().__init__(parent)
        self.active = False

    def start(self):
        if self.active:
            return
        self.active = True

        def worker():
            try:
                asyncio.run(self.monitor())
            except Exception:
                self.status.emit("O D-Bus deste desktop não permitiu monitorar notificações. Use também a central nativa.")
            finally:
                self.active = False

        Thread(target=worker, daemon=True).start()

    async def monitor(self):
        bus = await MessageBus(bus_type=BusType.SESSION).connect()
        rule = "type='method_call',interface='org.freedesktop.Notifications',member='Notify'"

        def receive(message):
            if message.interface == "org.freedesktop.Notifications" and message.member == "Notify" and len(message.body) >= 5:
                app, _, _, title, message_body = message.body[:5]
                self.notice.emit(f"{app} · {title}\n{message_body}")
            # A monitor observes calls but must never send a reply on their behalf.
            return message.message_type == MessageType.METHOD_CALL

        bus.add_message_handler(receive)
        try:
            reply = await bus.call(Message(destination="org.freedesktop.DBus", path="/org/freedesktop/DBus",
                interface="org.freedesktop.DBus.Monitoring", member="BecomeMonitor", signature="asu", body=[[rule], 0]))
            if reply.message_type == MessageType.ERROR:
                raise PermissionError(reply.body)
            self.status.emit("Monitorando novas notificações desta sessão.")
            await bus.wait_for_disconnect()
        finally:
            bus.disconnect()

