import json
import os
from urllib.parse import urlencode
from urllib.request import Request, urlopen

from PySide6.QtCore import QRect, Qt
from PySide6.QtGui import QPainter, QColor
from PySide6.QtWidgets import QDialog, QRubberBand, QWidget


class RegionPicker(QDialog):
    """Select a region on a frozen image of the monitor under X11."""
    def __init__(self, image, screen):
        super().__init__(None, Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint)
        self.image = image
        self.selection = QRect()
        self.origin = None
        self.setGeometry(screen.geometry())
        self.setCursor(Qt.CursorShape.CrossCursor)
        self.rubber = QRubberBand(QRubberBand.Shape.Rectangle, self)

    def paintEvent(self, event):
        painter = QPainter(self)
        painter.drawPixmap(self.rect(), self.image)
        painter.fillRect(self.rect(), QColor(0, 0, 0, 65))
        painter.fillRect(QRect(12, 12, 272, 30), QColor(18, 18, 26, 210))
        painter.setPen(Qt.GlobalColor.white)
        painter.drawText(QRect(20, 12, 260, 30), Qt.AlignmentFlag.AlignVCenter, "Arraste para selecionar · Esc cancela")

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton:
            self.origin = event.position().toPoint()
            self.rubber.setGeometry(QRect(self.origin, self.origin))
            self.rubber.show()

    def mouseMoveEvent(self, event):
        if self.origin is not None:
            self.rubber.setGeometry(QRect(self.origin, event.position().toPoint()).normalized().intersected(self.rect()))

    def mouseReleaseEvent(self, event):
        if self.origin is not None:
            self.selection = self.rubber.geometry()
            if self.selection.width() > 3 and self.selection.height() > 3:
                self.accept()
            else:
                self.rubber.hide()
                self.origin = None

    def cropped(self):
        ratio_x = self.image.width() / self.width()
        ratio_y = self.image.height() / self.height()
        region = self.selection
        return self.image.copy(round(region.x() * ratio_x), round(region.y() * ratio_y),
                               round(region.width() * ratio_x), round(region.height() * ratio_y))


def translate(text, language):
    key = os.environ.get("ACTIONBAR_TRANSLATOR_KEY")
    if not key:
        raise ValueError("Defina ACTIONBAR_TRANSLATOR_KEY para traduzir.")
    headers = {"Ocp-Apim-Subscription-Key": key, "Content-Type": "application/json"}
    region = os.environ.get("ACTIONBAR_TRANSLATOR_REGION")
    if region:
        headers["Ocp-Apim-Subscription-Region"] = region
    url = "https://api.cognitive.microsofttranslator.com/translate?" + urlencode({"api-version": "3.0", "to": language})
    with urlopen(Request(url, data=json.dumps([{"Text": text}]).encode(), headers=headers), timeout=20) as response:
        return json.load(response)[0]["translations"][0]["text"]

