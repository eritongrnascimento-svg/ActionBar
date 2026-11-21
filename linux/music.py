from pathlib import Path

from PySide6.QtCore import Qt, QUrl
from PySide6.QtGui import QImage, QPixmap
from PySide6.QtMultimedia import QAudioOutput, QMediaMetaData, QMediaPlayer
from PySide6.QtWidgets import (QComboBox, QFileDialog, QFrame, QHBoxLayout, QLabel,
                               QListWidget, QSlider, QVBoxLayout)

EXTENSIONS = {".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".wma", ".opus", ".aiff"}
FILTER = "Áudio (" + " ".join("*" + extension for extension in sorted(EXTENSIONS)) + ")"


class MusicPanel:
    def build_music(self):
        layout = self.tab("Música")
        self.music = QMediaPlayer(self)
        self.audio = QAudioOutput(self)
        self.audio.setVolume(.5)
        self.music.setAudioOutput(self.audio)
        self.tracks = []
        self.selected_track = -1
        self.music_note = QLabel()
        self.music_note.setWordWrap(True)

        left = QFrame(objectName="musicCard")
        left.setStyleSheet("#musicCard { background:#242441; border-radius:15px; }")
        details = QVBoxLayout(left)
        self.album_cover = QLabel("♫", alignment=Qt.AlignmentFlag.AlignCenter)
        self.album_cover.setStyleSheet("color:#eee5ff; font-size:64px; background:#473475; border-radius:9px;")
        self.album_cover.setMinimumHeight(165)
        details.addWidget(self.album_cover, 1)
        self.song_title = QLabel("Escolha uma faixa")
        self.song_title.setStyleSheet("font-size:21px; font-weight:600; color:white")
        self.song_album = QLabel()
        self.song_album.setStyleSheet("color:#c9c2df")
        details.addWidget(self.song_title)
        details.addWidget(self.song_album)

        right = QVBoxLayout()
        self.music_queue = QListWidget()
        self.music_queue.itemActivated.connect(lambda item: self.play_track(self.music_queue.row(item)))
        right.addWidget(self.button("Adicionar pasta…", self.add_music_folder))
        right.addWidget(self.button("Adicionar arquivos…", self.open_audio))
        right.addWidget(self.music_queue, 1)
        body = QHBoxLayout()
        body.addWidget(left, 3)
        body.addLayout(right, 2)
        layout.addLayout(body, 1)

        self.music_position = QSlider(Qt.Orientation.Horizontal)
        self.music_position.setAccessibleName("Posição da faixa")
        self.music_position.sliderMoved.connect(self.music.setPosition)
        self.music.durationChanged.connect(lambda value: self.music_position.setRange(0, min(value, 2147483647)))
        self.music.positionChanged.connect(self.update_music_position)
        layout.addWidget(self.music_position)
        self.music_times = QLabel("00:00 / 00:00")
        layout.addWidget(self.music_times)
        self.play_button = self.button("▶ Tocar", self.toggle_music)
        self.music.playbackStateChanged.connect(lambda state: self.play_button.setText(
            "Ⅱ Pausar" if state == QMediaPlayer.PlaybackState.PlayingState else "▶ Tocar"))
        self.repeat_music = QComboBox()
        self.repeat_music.addItems(["Sem repetição", "Repetir fila", "Repetir faixa"])
        volume = QSlider(Qt.Orientation.Horizontal)
        volume.setRange(0, 100)
        volume.setValue(50)
        volume.setMaximumWidth(95)
        volume.setAccessibleName("Volume")
        volume.valueChanged.connect(lambda value: self.audio.setVolume(value / 100))
        self.row(layout, self.button("⏮ Anterior", lambda: self.move_music(-1)), self.play_button,
                 self.button("Próxima ⏭", lambda: self.move_music(1)), self.button("■ Parar", self.music.stop),
                 self.repeat_music, QLabel("Volume"), volume)
        layout.addWidget(self.music_note)
        self.music.metaDataChanged.connect(self.show_track_metadata)
        self.music.errorOccurred.connect(lambda _, message: self.music_note.setText(message))
        self.music.mediaStatusChanged.connect(self.music_status)

    def update_music_position(self, value):
        def formatted(ms):
            seconds = ms // 1000
            return f"{seconds // 60:02d}:{seconds % 60:02d}"
        if not self.music_position.isSliderDown():
            self.music_position.setValue(value)
        self.music_times.setText(f"{formatted(value)} / {formatted(self.music.duration())}")

    def add_tracks(self, paths):
        existing = set(self.tracks)
        for path in paths:
            path = Path(path)
            if path.suffix.lower() in EXTENSIONS and path not in existing:
                self.tracks.append(path)
                existing.add(path)
                self.music_queue.addItem(path.stem)
        if self.music_queue.currentRow() < 0:
            self.music_queue.setCurrentRow(0)
        self.music_note.setText(f"{len(self.tracks)} faixas na fila")

    def open_audio(self):
        files, _ = QFileDialog.getOpenFileNames(self, "Adicionar áudio", "", FILTER)
        self.add_tracks(files)

    def add_music_folder(self):
        folder = QFileDialog.getExistingDirectory(self, "Adicionar pasta de músicas")
        if folder:
            self.run_worker(lambda: sorted(Path(folder).rglob("*")), self.add_tracks, self.music_note)

    def toggle_music(self):
        if self.selected_track < 0:
            if self.tracks:
                self.play_track(max(0, self.music_queue.currentRow()))
        elif self.music.playbackState() == QMediaPlayer.PlaybackState.PlayingState:
            self.music.pause()
        else:
            self.music.play()

    def play_track(self, index):
        if not 0 <= index < len(self.tracks):
            return
        path = self.tracks[index]
        self.selected_track = index
        self.music_queue.setCurrentRow(index)
        self.song_title.setText(path.stem)
        self.song_album.clear()
        self.album_cover.clear()
        self.album_cover.setText("♫")
        self.music.setSource(QUrl.fromLocalFile(str(path)))
        self.music.play()

    def move_music(self, step, automatic=False):
        next_track = self.selected_track + step
        if self.tracks and self.repeat_music.currentIndex() == 1:
            next_track %= len(self.tracks)
        if 0 <= next_track < len(self.tracks):
            self.play_track(next_track)
        elif automatic:
            self.music.stop()

    def music_status(self, state):
        if state != QMediaPlayer.MediaStatus.EndOfMedia:
            return
        if self.repeat_music.currentIndex() == 2:
            self.music.setPosition(0)
            self.music.play()
        else:
            self.move_music(1, automatic=True)

    def show_track_metadata(self):
        if not 0 <= self.selected_track < len(self.tracks):
            return
        metadata = self.music.metaData()
        keys = QMediaMetaData.Key
        title = metadata.stringValue(keys.Title)
        album = metadata.stringValue(keys.AlbumTitle)
        artist = metadata.stringValue(keys.ContributingArtist)
        if title:
            self.song_title.setText(title)
            self.music_queue.item(self.selected_track).setText(title)
        self.song_album.setText(" · ".join(part for part in [artist, album] if part))
        artwork = metadata.value(keys.CoverArtImage) or metadata.value(keys.ThumbnailImage)
        if not isinstance(artwork, QImage) or artwork.isNull():
            artwork = QImage()
            for name in ("cover.jpg", "cover.png", "folder.jpg"):
                if artwork.load(str(self.tracks[self.selected_track].parent / name)):
                    break
        if not artwork.isNull():
            self.album_cover.setPixmap(QPixmap.fromImage(artwork.scaled(240, 200,
                Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)))

