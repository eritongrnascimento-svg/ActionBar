using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;
using Forms = System.Windows.Forms;

namespace BarraInteligente;

internal sealed partial class BarPanels : IDisposable
{
    internal static readonly (string Label, string Key)[] Modes =
    {
        ("YouTube","youtube"), ("Internet","internet"), ("Música","musica"),
        ("Terminal","terminal"), ("Notificações","notificacoes"), ("OCR","ocr"), ("Lembretes","lembretes"), ("Pastas","pastas"), ("Atualizações","atualizacoes")
    };

    private readonly Window window = new() { Title = "ActionBar", Width = 650, Height = 510 };
    private readonly TabControl tabs = new();
    private readonly Dictionary<string, TabItem> pages = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly MediaPlayer music = new();
    private readonly string alarmPath;
    private readonly TextBox youtube = Field(), address = Field();
    private readonly TextBox ocrText = Output(), inbox = Output();
    private readonly ListBox videoResults = new(), alarms = new();
    private readonly TextBlock videoStatus = Label(), webStatus = Label(), musicStatus = Label(), alarmStatus = Label();
    private readonly CancellationTokenSource lifetime = new();
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly List<Reminder> reminders;
    private WebView2? video, internet;
    private string? videoLink;
    private CancellationTokenSource? lookup;
    private bool disposed;
    private int panelInvocation;


    private sealed record Video(string Id, string Title)
    {
        public override string ToString() => Title;
    }
    private sealed record Reminder(Guid Id, DateTimeOffset Due, string Text, bool Sticky)
    {
        public override string ToString() => $"{Due.LocalDateTime:g} · {Text}";
    }

    internal BarPanels(string folder)
    {
        alarmPath = Path.Combine(folder, "lembretes.json");
        reminders = LoadReminders();
        window.Content = tabs;
        window.Icon = Brand.WindowIcon;
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Escape || terminalView?.IsKeyboardFocusWithin == true) return;
            args.Handled = true;
            Hide();
        };
        window.Closing += (_, e) => { if (!disposed) { e.Cancel = true; Hide(); } };
        BuildVideo();
        BuildInternet();
        BuildMusic();
        BuildTerminal();
        BuildNotifications();
        BuildOcr();
        BuildReminders();
        BuildFolders();
        BuildUpdates();
        timer.Tick += (_, _) => CheckReminders();
        timer.Start();
    }

    internal void Hide()
    {
        panelInvocation++;
        lookup?.Cancel();
        window.Hide();
        if (video?.CoreWebView2 != null && videoLink != null) video.CoreWebView2.Navigate("about:blank");
        videoLink = null;
        if (video != null) video.Visibility = Visibility.Collapsed;
    }

    internal void Show(string mode, string query, Window anchor)
    {
        panelInvocation++;
        tabs.SelectedItem = pages[mode];
        if (!window.IsVisible)
        {
            var dpi = VisualTreeHelper.GetDpi(anchor);
            var work = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
            window.Width = Math.Min(650, work.Width / dpi.DpiScaleX);
            window.Height = Math.Min(510, work.Height / dpi.DpiScaleY);
            window.Left = Math.Clamp(anchor.Left, work.Left / dpi.DpiScaleX, work.Right / dpi.DpiScaleX - window.Width);
            window.Top = Math.Clamp(anchor.Top + 115, work.Top / dpi.DpiScaleY, work.Bottom / dpi.DpiScaleY - window.Height);
            window.Topmost = anchor.Topmost;
            window.Show();
        }
        window.Activate();
        switch (mode)
        {
            case "youtube":
                youtube.Text = query;
                youtube.Focus();
                if (query.Length > 0) Run(videoStatus, SearchVideo);
                else
                {
                    videoResults.ItemsSource = null;
                    videoResults.Visibility = Visibility.Collapsed;
                    videoStatus.Text = "";
                }
                break;
            case "internet":
                if (query.Length == 0 && internet?.CoreWebView2 != null) internet.CoreWebView2.Navigate("about:blank");
                address.Text = query;
                address.Focus();
                if (query.Length > 0) Run(webStatus, Navigate);
                break;
            case "terminal":
                pendingCommand = query.Length == 0 ? null : query;
                Run(terminalStatus, async () => { await EnsureTerminal(); FocusTerminal(); });
                break;
            case "ocr":
                ScheduleCaptureChoice();
                break;
            case "pastas":
                if (query == "\0wallpapers") Run(folderStatus, ShowWallpapers);
                else Run(folderStatus, () => Browse(query));
                break;
        }
    }

    private static TextBox Field() => new() { Margin = new Thickness(4), MinWidth = 150 };
    private static TextBox Output() => new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private static TextBlock Label(string text = "") => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };

    private static Button Button(string title, Action action)
    {
        var control = new Button { Content = title, Margin = new Thickness(4), Padding = new Thickness(9, 4, 9, 4) };
        control.Click += (_, _) => action();
        return control;
    }

    private DockPanel Page(string key)
    {
        var panel = new DockPanel { Margin = new Thickness(10) };
        var page = new TabItem { Header = Modes.First(m => m.Key == key).Label, Content = panel };
        pages.Add(key, page);
        tabs.Items.Add(page);
        return panel;
    }

    private static void Top(DockPanel panel, UIElement item) { DockPanel.SetDock(item, Dock.Top); panel.Children.Add(item); }
    private static WrapPanel Row(params UIElement[] items)
    {
        var row = new WrapPanel();
        foreach (var item in items) row.Children.Add(item);
        return row;
    }

    private static async void Run(TextBlock status, Func<Task> action)
    {
        status.Text = "";
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception error) { status.Text = error.Message; }
    }

    private static void OnEnter(TextBox field, Action action)
    {
        field.KeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Enter) return;
            args.Handled = true;
            action();
        };
    }

    private void BuildVideo()
    {
        var panel = Page("youtube");
        Action search = () => Run(videoStatus, SearchVideo);
        OnEnter(youtube, search);
        System.Windows.Automation.AutomationProperties.SetName(youtube, "Termo ou link do YouTube");
        youtube.Width = 355;
        Top(panel, Label("Termo de pesquisa ou link do YouTube"));
        Top(panel, Row(youtube, Button("Abrir ou pesquisar", search), Button("Copiar link", () =>
        {
            if (videoLink == null) return;
            Run(videoStatus, () => { Clipboard.SetText(videoLink); videoStatus.Text = "Link copiado."; return Task.CompletedTask; });
        })));
        Top(panel, videoStatus);
        videoResults.MaxHeight = 100;
        videoResults.Visibility = Visibility.Collapsed;
        videoResults.SelectionChanged += (_, _) =>
        {
            if (videoResults.SelectedItem is Video item) Run(videoStatus, () => PlayVideo(item.Id));
        };
        Top(panel, videoResults);
    }

    private async Task SearchVideo()
    {
        lookup?.Cancel();
        lookup?.Dispose();
        lookup = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = lookup.Token;
        var query = youtube.Text.Trim();
        if (query.Length == 0) return;
        videoResults.ItemsSource = null;
        videoResults.Visibility = Visibility.Collapsed;
        if (VideoAddress.TryParse(query, out var id))
        {
            await PlayVideo(id);
            return;
        }
        if (query.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException("Cole o endereço de um vídeo, Short ou transmissão do YouTube.");
        var apiKey = Environment.GetEnvironmentVariable("ACTIONBAR_YOUTUBE_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            videoStatus.Text = "Configure ACTIONBAR_YOUTUBE_API_KEY para pesquisar ou cole o link de um vídeo.";
            return;
        }
        videoStatus.Text = "Pesquisando…";
        var url = "https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&maxResults=10&q=" +
            Uri.EscapeDataString(query) + "&key=" + Uri.EscapeDataString(apiKey);
        using var response = await Http.GetAsync(url, token);
        if (!response.IsSuccessStatusCode) { videoStatus.Text = $"YouTube respondeu com HTTP {(int)response.StatusCode}. Confira a chave e a cota."; return; }
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        token.ThrowIfCancellationRequested();
        videoResults.ItemsSource = data.RootElement.GetProperty("items").EnumerateArray().Select(item =>
            new Video(item.GetProperty("id").GetProperty("videoId").GetString()!,
            System.Net.WebUtility.HtmlDecode(item.GetProperty("snippet").GetProperty("title").GetString()) ?? "(Sem título)")).ToArray();
        videoResults.Visibility = videoResults.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        videoStatus.Text = videoResults.Items.Count == 0 ? "Nenhum vídeo encontrado." : "Selecione um vídeo.";
    }

    private async Task PlayVideo(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{11}\z"))
            throw new ArgumentException("Link de vídeo inválido.");
        var invocation = panelInvocation;
        var browser = video ??= AddWebView("youtube");
        await browser.EnsureCoreWebView2Async();
        if (disposed || invocation != panelInvocation) return;
        browser.Visibility = Visibility.Visible;
        videoLink = "https://www.youtube.com/watch?v=" + id;
        videoStatus.Text = "Carregando vídeo…";
        browser.CoreWebView2.Navigate(VideoOrigin + PlayerPath + id);
    }

    // Use the application's manifest identity as the HTTPS origin for desktop embeds.
    private const string VideoOrigin = "https://actionbar";
    private const string PlayerPath = "/player/";
    private static readonly System.Text.RegularExpressions.Regex EmbeddedId =
        new(@"\A[A-Za-z0-9_-]{11}\z", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static void ConfigureEmbeddedVideo(CoreWebView2 browser)
    {
        browser.AddWebResourceRequestedFilter(VideoOrigin + PlayerPath + "*", CoreWebView2WebResourceContext.Document);
        browser.AddWebResourceRequestedFilter("https://www.youtube.com/embed/*", CoreWebView2WebResourceContext.Document);
        browser.WebResourceRequested += (_, args) =>
        {
            if (args.Request.Uri.StartsWith("https://www.youtube.com/embed/", StringComparison.Ordinal))
            {
                args.Request.Headers.SetHeader("Referer", VideoOrigin + "/");
                return;
            }
            var id = args.Request.Uri[(VideoOrigin.Length + PlayerPath.Length)..];
            if (!EmbeddedId.IsMatch(id))
            {
                args.Response = browser.Environment.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            var player = "https://www.youtube.com/embed/" + id + "?autoplay=1&amp;origin=" + Uri.EscapeDataString(VideoOrigin);
            var html = "<!doctype html><html><head><meta charset='utf-8'><title>YouTube</title>" +
                "<style>html,body,iframe{width:100%;height:100%;margin:0;border:0;background:#111;display:block}</style></head>" +
                "<body><iframe title='YouTube' referrerpolicy='strict-origin-when-cross-origin' allow='autoplay; encrypted-media; picture-in-picture; fullscreen' allowfullscreen src='" +
                player + "'></iframe></body></html>";
            args.Response = browser.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)),
                200, "OK", "Content-Type: text/html; charset=utf-8\r\nReferrer-Policy: strict-origin-when-cross-origin\r\n" +
                "Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; frame-src https://www.youtube.com");
        };
    }

    private WebView2 AddWebView(string page)
    {
        var browser = new WebView2();
        browser.CoreWebView2InitializationCompleted += (_, e) =>
        {
            if (!e.IsSuccess) return;
            browser.CoreWebView2.Settings.IsWebMessageEnabled = false;
            if (page == "youtube") ConfigureEmbeddedVideo(browser.CoreWebView2);
            browser.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            browser.CoreWebView2.NavigationStarting += (_, args) =>
            {
                if (args.Uri == "about:blank") return;
                if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                    args.Cancel = true;
            };
            browser.CoreWebView2.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (page == "internet" && Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                    browser.Source = uri;
            };
            browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess && page == "youtube" && videoStatus.Text == "Carregando vídeo…") videoStatus.Text = "";
                if (!args.IsSuccess && args.WebErrorStatus.ToString() != "OperationCanceled")
                    (page == "youtube" ? videoStatus : webStatus).Text = "Falha ao carregar: " + args.WebErrorStatus;
            };
        };
        ((DockPanel)pages[page].Content).Children.Add(browser);
        return browser;
    }

    private void BuildInternet()
    {
        var panel = Page("internet");
        Action navigate = () => Run(webStatus, Navigate);
        OnEnter(address, navigate);
        Top(panel, Label("Endereço, pesquisa ou webmail"));
        Top(panel, Row(address, Button("Abrir", navigate), Button("Voltar", () => { if (internet?.CanGoBack == true) internet.GoBack(); })));
        Top(panel, webStatus);
    }

    private async Task Navigate()
    {
        var value = address.Text.Trim();
        if (value.Length == 0) return;
        var candidate = value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || !uri.Host.Contains('.') && !uri.IsLoopback)
            uri = new Uri("https://www.google.com/search?q=" + Uri.EscapeDataString(value));
        if (uri.Scheme is not ("http" or "https")) throw new ArgumentException("Use um endereço HTTP ou HTTPS.");
        var invocation = panelInvocation;
        var browser = internet ??= AddWebView("internet");
        await browser.EnsureCoreWebView2Async();
        if (!disposed && invocation == panelInvocation) browser.Source = uri;
    }

    private void BuildNotifications()
    {
        var panel = Page("notificacoes");
        var status = Label();
        var phone = Field();
        phone.ToolTip = "WhatsApp: número com código do país";
        Top(panel, Row(Button("Ler notificações", () => Run(status, ReadNotifications)), Button("Central do Windows", () =>
        {
            window.Hide();
            WindowActions.Chord(0x5B, OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) ? (byte)0x4E : (byte)0x41);
        })));
        Top(panel, Label("Nova mensagem · informe o número com código do país para WhatsApp"));
        Top(panel, Row(phone, Button("WhatsApp", () =>
        {
            var digits = new string(phone.Text.Where(c => c is >= '0' and <= '9').ToArray());
            if (digits.Length is < 7 or > 15) { status.Text = "Número inválido."; return; }
            Run(status, () => { Commands.Open("https://wa.me/" + digits); return Task.CompletedTask; });
        }), Button("Telegram", () => Run(status, () => OpenExternal("tg://"))),
            Button("Novo e-mail", () => Run(status, () => OpenExternal("mailto:")))));
        Top(panel, Row(Button("Gmail", () => OpenWebmail("https://mail.google.com/")),
            Button("Outlook", () => OpenWebmail("https://outlook.live.com/mail/")),
            Button("Outro webmail", () => tabs.SelectedItem = pages["internet"])));
        Top(panel, status);
        panel.Children.Add(inbox);
    }

    private static Task OpenExternal(string uri)
    {
        Commands.Open(uri);
        return Task.CompletedTask;
    }

    private void OpenWebmail(string uri)
    {
        tabs.SelectedItem = pages["internet"];
        address.Text = uri;
        Run(webStatus, Navigate);
    }

    private async Task ReadNotifications()
    {
        UserNotificationListener listener;
        UserNotificationListenerAccessStatus access;
        try
        {
            listener = UserNotificationListener.Current;
            access = await listener.RequestAccessAsync();
        }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            inbox.Text = "O Windows recusou a leitura. Use a Central do Windows ou uma instalação com a capacidade userNotificationListener.";
            return;
        }
        if (access != UserNotificationListenerAccessStatus.Allowed)
        {
            inbox.Text = "Acesso indisponível. O Windows pode exigir a versão empacotada com a capacidade userNotificationListener.";
            return;
        }
        var items = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        inbox.Text = string.Join("\n\n", items.OrderByDescending(item => item.CreationTime).Select(item =>
        {
            var body = item.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric)?.GetTextElements();
            return $"{item.CreationTime.LocalDateTime:g} · {item.AppInfo.DisplayInfo.DisplayName}\n" +
                string.Join("\n", body?.Select(line => line.Text) ?? Enumerable.Empty<string>());
        }));
        if (inbox.Text.Length == 0) inbox.Text = "Nenhuma notificação.";
    }

    private void BuildReminders()
    {
        var panel = Page("lembretes");
        var date = new DatePicker { SelectedDate = DateTime.Today, Margin = new Thickness(4) };
        var time = Field();
        time.Text = DateTime.Now.AddMinutes(5).ToString("HH:mm");
        var text = Field();
        var sticky = new CheckBox { Content = "Nota adesiva", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        Top(panel, Label("Data, hora e texto do lembrete"));
        Top(panel, Row(date, time, text, sticky));
        Top(panel, Row(Button("Agendar", () => Run(alarmStatus, () =>
        {
            if (date.SelectedDate is not DateTime day || !TimeOnly.TryParseExact(time.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock) || string.IsNullOrWhiteSpace(text.Text))
                throw new ArgumentException("Informe uma data, uma hora HH:mm e o texto.");
            var due = new DateTimeOffset(day.Date.Add(clock.ToTimeSpan()));
            if (due <= DateTimeOffset.Now) throw new ArgumentException("Selecione um horário futuro.");
            SaveReminders(reminders.Append(new Reminder(Guid.NewGuid(), due, text.Text.Trim(), sticky.IsChecked == true)));
            text.Clear();
            return Task.CompletedTask;
        })), Button("Excluir", () => Run(alarmStatus, () =>
        {
            if (alarms.SelectedItem is Reminder item) SaveReminders(reminders.Where(r => r.Id != item.Id));
            return Task.CompletedTask;
        }))));
        Top(panel, alarmStatus);
        panel.Children.Add(alarms);
        RefreshAlarms();
    }

    private List<Reminder> LoadReminders()
    {
        try { return File.Exists(alarmPath) ? JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(alarmPath)) ?? new() : new(); }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            alarmStatus.Text = "Falha ao ler os lembretes. Verifique " + alarmPath;
            return new();
        }
    }

    private void SaveReminders(IEnumerable<Reminder> updated)
    {
        var snapshot = updated.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(alarmPath)!);
        File.WriteAllText(alarmPath + ".tmp", JsonSerializer.Serialize(snapshot));
        File.Move(alarmPath + ".tmp", alarmPath, overwrite: true);
        reminders.Clear();
        reminders.AddRange(snapshot);
        RefreshAlarms();
    }

    private void RefreshAlarms() => alarms.ItemsSource = reminders.OrderBy(r => r.Due).ToArray();

    private void CheckReminders()
    {
        var due = reminders.Where(r => r.Due <= DateTimeOffset.Now).ToArray();
        if (due.Length == 0) return;
        try { SaveReminders(reminders.Except(due)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { alarmStatus.Text = error.Message; return; }
        System.Media.SystemSounds.Exclamation.Play();
        foreach (var reminder in due)
        {
            if (!reminder.Sticky)
            {
                var tray = new Forms.NotifyIcon { Icon = Brand.TrayIcon(), Visible = true };
                tray.ShowBalloonTip(5000, "ActionBar · Lembrete", reminder.Text, Forms.ToolTipIcon.Info);
                var cleanup = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                cleanup.Tick += (_, _) => { cleanup.Stop(); tray.Dispose(); };
                cleanup.Start();
                continue;
            }
            var note = new Window { Title = "ActionBar · Nota", Width = 330, SizeToContent = SizeToContent.Height,
                MaxHeight = 400, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen };
            note.Content = new ScrollViewer { Padding = new Thickness(16), Content = Label(reminder.Text) };
            note.Show();
        }
    }

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        lifetime.Cancel();
        lookup?.Dispose();
        lifetime.Dispose();
        sessionVersion++;
        session?.Dispose();
        terminalView?.Dispose();
        musicClock?.Stop();
        music.Close();
        video?.Dispose();
        internet?.Dispose();
        window.Close();
    }
}

