using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace BarraInteligente;

internal sealed partial class BarPanels
{
    private enum Repeat { Off, Queue, Track }
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".wav", ".m4a", ".aac", ".wma", ".flac", ".ogg", ".opus", ".aif", ".aiff" };
    private readonly ObservableCollection<Track> tracks = new();
    private readonly HashSet<string> queuedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ListBox queue = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TextBlock trackTitle = new() { FontSize = 23, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock trackDetails = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock trackCount = new();
    private readonly Image coverImage = new() { Stretch = Stretch.UniformToFill };
    private DispatcherTimer? musicClock;
    private Track? currentTrack;
    private Button playPause = null!;
    private Repeat repeat;
    private bool updatingPosition, playing;
    private int trackVersion;

    private sealed class Track(string path) : System.ComponentModel.INotifyPropertyChanged
    {
        internal string Path { get; } = path;
        public string Title { get; private set; } = System.IO.Path.GetFileNameWithoutExtension(path);
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        internal void SetTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title) || title == Title) return;
            Title = title;
            PropertyChanged?.Invoke(this, new(nameof(Title)));
        }
    }

    private static Brush Paint(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private void BuildMusic()
    {
        var page = Page("musica");
        var muted = Paint("#AAB4CF");
        var layout = new Grid { Margin = new Thickness(4) };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(layout);

        var now = new DockPanel();
        var identity = new StackPanel { Margin = new Thickness(0, 9, 0, 0) };
        trackTitle.Text = "Escolha uma faixa";
        trackTitle.Foreground = Brushes.White;
        trackDetails.Foreground = muted;
        identity.Children.Add(trackTitle);
        identity.Children.Add(trackDetails);
        DockPanel.SetDock(identity, Dock.Bottom);
        now.Children.Add(identity);
        var album = new Grid { ClipToBounds = true };
        album.Children.Add(new TextBlock { Text = "♫", FontSize = 76, Foreground = Paint("#E1D9FF"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        album.Children.Add(coverImage);
        var cover = new Border { CornerRadius = new CornerRadius(13), ClipToBounds = true,
            Background = new LinearGradientBrush(Color.FromRgb(106, 76, 167), Color.FromRgb(29, 41, 79), 38), Child = album };
        now.Children.Add(cover);
        var main = new Border { Padding = new Thickness(13), Margin = new Thickness(0, 0, 10, 9), CornerRadius = new CornerRadius(16),
            Background = Paint("#1B2132"), Child = now };
        layout.Children.Add(main);

        var playlist = new DockPanel();
        var addFolder = Button("Adicionar pasta…", () => Run(musicStatus, AddFolder));
        var addFiles = Button("Adicionar arquivos…", AddFiles);
        Top(playlist, Row(addFolder, addFiles));
        trackCount.Text = "Fila · 0 faixas";
        trackCount.Margin = new Thickness(4, 6, 0, 6);
        trackCount.Foreground = muted;
        Top(playlist, trackCount);
        queue.ItemsSource = tracks;
        queue.DisplayMemberPath = nameof(Track.Title);
        queue.Foreground = Brushes.White;
        queue.MouseDoubleClick += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject hit && ItemsControl.ContainerFromElement(queue, hit) is ListBoxItem { Content: Track track })
                PlayTrack(track);
        };
        queue.KeyDown += (_, args) =>
        {
            if (queue.SelectedItem is not Track track) return;
            if (args.Key == Key.Enter) { args.Handled = true; PlayTrack(track); }
            if (args.Key == Key.Delete) { args.Handled = true; RemoveTrack(track); }
        };
        var queueMenu = new ContextMenu();
        var remove = new MenuItem { Header = "Remover da fila" };
        remove.Click += (_, _) => { if (queue.SelectedItem is Track track) RemoveTrack(track); };
        queueMenu.Items.Add(remove);
        queue.ContextMenu = queueMenu;
        playlist.Children.Add(queue);
        var queueCard = new Border { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 9), CornerRadius = new CornerRadius(16),
            Background = Paint("#191F2C"), Child = playlist };
        Grid.SetColumn(queueCard, 1);
        layout.Children.Add(queueCard);

        var controls = new StackPanel();
        var position = new Slider { IsEnabled = false, IsMoveToPointEnabled = true, ToolTip = "Posição na faixa" };
        position.ValueChanged += (_, _) => { if (!updatingPosition && position.IsEnabled) music.Position = TimeSpan.FromSeconds(position.Value); };
        var elapsed = new TextBlock { Text = "00:00", Foreground = muted };
        var duration = new TextBlock { Text = "00:00", Foreground = muted, HorizontalAlignment = HorizontalAlignment.Right };
        var times = new Grid { Margin = new Thickness(2, 0, 2, 3) };
        times.Children.Add(elapsed);
        times.Children.Add(duration);
        controls.Children.Add(position);
        controls.Children.Add(times);

        playPause = Button("▶  Tocar", ToggleMusic);
        playPause.FontWeight = FontWeights.SemiBold;
        playPause.Background = Paint("#C4AFF9");
        playPause.Foreground = Paint("#211335");
        var previous = Button("⏮", PreviousTrack);
        previous.ToolTip = "Faixa anterior";
        System.Windows.Automation.AutomationProperties.SetName(previous, "Faixa anterior");
        var next = Button("⏭", () => Advance(manual: true));
        next.ToolTip = "Próxima faixa";
        System.Windows.Automation.AutomationProperties.SetName(next, "Próxima faixa");
        var stop = Button("■  Parar", StopMusic);
        var repeatControl = new ComboBox { MinWidth = 119, Margin = new Thickness(6, 4, 4, 4),
            ItemsSource = new[] { "Sem repetição", "Repetir fila", "Repetir faixa" }, SelectedIndex = 0 };
        System.Windows.Automation.AutomationProperties.SetName(repeatControl, "Repetição");
        repeatControl.SelectionChanged += (_, _) => repeat = (Repeat)Math.Max(0, repeatControl.SelectedIndex);
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = .5, Width = 75, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(volume, "Volume");
        music.Volume = volume.Value;
        volume.ValueChanged += (_, _) => music.Volume = volume.Value;
        controls.Children.Add(Row(previous, playPause, next, stop, repeatControl,
            new TextBlock { Text = "Volume", Foreground = muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 5, 0) }, volume));
        musicStatus.Foreground = muted;
        controls.Children.Add(musicStatus);
        var footer = new Border { Padding = new Thickness(11, 8, 11, 3), CornerRadius = new CornerRadius(16),
            Background = Paint("#191F2C"), Child = controls };
        Grid.SetRow(footer, 1);
        Grid.SetColumnSpan(footer, 2);
        layout.Children.Add(footer);

        music.MediaEnded += (_, _) => Advance(manual: false);
        music.MediaFailed += (_, args) => { musicStatus.Text = "Falha ao reproduzir: " + args.ErrorException.Message; SetPlaying(false); };
        musicClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        musicClock.Tick += (_, _) =>
        {
            if (!window.IsVisible || tabs.SelectedItem != pages["musica"]) return;
            var total = music.NaturalDuration.HasTimeSpan ? music.NaturalDuration.TimeSpan : TimeSpan.Zero;
            updatingPosition = true;
            position.IsEnabled = total > TimeSpan.Zero;
            position.Maximum = Math.Max(1, total.TotalSeconds);
            position.Value = music.Position.TotalSeconds;
            updatingPosition = false;
            elapsed.Text = MusicTime(music.Position);
            duration.Text = MusicTime(total);
        };
        musicClock.Start();
    }

    private static string MusicTime(TimeSpan time) => $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
    private void SetPlaying(bool value)
    {
        playing = value;
        playPause.Content = value ? "Ⅱ  Pausar" : "▶  Tocar";
    }

    private void AddFiles()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Multiselect = true,
            Filter = "Arquivos de áudio|" + string.Join(";", AudioExtensions.Select(extension => "*" + extension)) };
        if (picker.ShowDialog(window) == true) QueueTracks(picker.FileNames);
    }

    private async Task AddFolder()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Adicionar pasta de músicas" };
        if (picker.ShowDialog(window) != true) return;
        musicStatus.Text = "Buscando músicas na pasta e nas subpastas…";
        var paths = await Task.Run(() => Directory.EnumerateFiles(picker.FolderName, "*",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                    AttributesToSkip = System.IO.FileAttributes.ReparsePoint | System.IO.FileAttributes.System })
            .Where(path => AudioExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToArray());
        if (!disposed) QueueTracks(paths);
    }

    private void QueueTracks(IEnumerable<string> paths)
    {
        int added = 0;
        var firstAdd = tracks.Count == 0;
        foreach (var path in paths)
        {
            if (!queuedPaths.Add(path)) continue;
            tracks.Add(new Track(path));
            added++;
        }
        trackCount.Text = $"Fila · {tracks.Count} faixas";
        if (firstAdd && tracks.Count > 0) queue.SelectedIndex = 0;
        musicStatus.Text = added > 0 ? $"{added} faixa(s) adicionada(s)." : "Nenhuma faixa nova encontrada.";
    }

    private void RemoveTrack(Track track)
    {
        if (currentTrack == track)
        {
            StopMusic();
            music.Close();
            currentTrack = null;
            trackVersion++;
            trackTitle.Text = "Escolha uma faixa";
            trackDetails.Text = "";
            coverImage.Source = null;
        }
        queuedPaths.Remove(track.Path);
        tracks.Remove(track);
        trackCount.Text = $"Fila · {tracks.Count} faixas";
    }

    private void ToggleMusic()
    {
        if (currentTrack == null)
        {
            if ((queue.SelectedItem as Track ?? tracks.FirstOrDefault()) is Track track) PlayTrack(track);
            return;
        }
        if (playing) music.Pause(); else music.Play();
        SetPlaying(!playing);
    }

    private void StopMusic()
    {
        if (currentTrack != null) music.Stop();
        SetPlaying(false);
    }

    private void PreviousTrack()
    {
        if (currentTrack != null && music.Position > TimeSpan.FromSeconds(3))
        {
            music.Position = TimeSpan.Zero;
            return;
        }
        var index = currentTrack == null ? -1 : tracks.IndexOf(currentTrack);
        if (index > 0) PlayTrack(tracks[index - 1]);
        else if (repeat == Repeat.Queue && tracks.Count > 0) PlayTrack(tracks[^1]);
        else if (currentTrack != null) music.Position = TimeSpan.Zero;
    }

    private void Advance(bool manual)
    {
        if (currentTrack != null && !manual && repeat == Repeat.Track)
        {
            music.Position = TimeSpan.Zero;
            music.Play();
            return;
        }
        var index = currentTrack == null ? -1 : tracks.IndexOf(currentTrack);
        if (index + 1 < tracks.Count) PlayTrack(tracks[index + 1]);
        else if (repeat == Repeat.Queue && tracks.Count > 0) PlayTrack(tracks[0]);
        else if (!manual) StopMusic();
    }

    private void PlayTrack(Track track)
    {
        var version = ++trackVersion;
        currentTrack = track;
        queue.SelectedItem = track;
        queue.ScrollIntoView(track);
        trackTitle.Text = track.Title;
        trackDetails.Text = "";
        coverImage.Source = null;
        musicStatus.Text = "";
        music.Open(new Uri(track.Path));
        music.Play();
        SetPlaying(true);
        _ = LoadTrackDetails(track, version);
    }

    private async Task LoadTrackDetails(Track track, int version)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(track.Path);
            var metadata = await file.Properties.GetMusicPropertiesAsync();
            if (version != trackVersion || disposed) return;
            track.SetTitle(metadata.Title);
            trackTitle.Text = track.Title;
            trackDetails.Text = string.Join("  ·  ", new[] { metadata.Artist, metadata.Album }.Where(value => !string.IsNullOrWhiteSpace(value)));
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.MusicView, 320);
            if (version != trackVersion || disposed) return;
            if (thumbnail?.Type == ThumbnailType.Image)
            {
                using var input = thumbnail.AsStreamForRead();
                coverImage.Source = DecodeCover(input);
            }
            else if (Path.GetDirectoryName(track.Path) is string folder)
            {
                var fallback = new[] { "cover.jpg", "folder.jpg", "cover.png", "album.jpg" }
                    .Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
                if (fallback != null)
                {
                    using var input = File.OpenRead(fallback);
                    coverImage.Source = DecodeCover(input);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException
            or System.Runtime.InteropServices.COMException) { }
    }

    private static BitmapImage DecodeCover(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.DecodePixelWidth = 520;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        return image;
    }
}

