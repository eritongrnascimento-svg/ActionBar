using System;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Forms = System.Windows.Forms;

namespace BarraInteligente;

internal sealed partial class BarPanels
{
    private readonly TextBox translation = Output();
    private readonly ComboBox targetLanguage = new() { MinWidth = 115, SelectedIndex = 0, DisplayMemberPath = "Label", SelectedValuePath = "Code" };
    private readonly CheckBox autoTranslate = new() { Content = "Traduzir após o OCR (online)", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock ocrStatus = Label("Selecione uma região ou leia o monitor do cursor.");
    private readonly Button capture = new() { Content = "Capturar…", Margin = new Thickness(4) };
    private readonly Button translateButton = new() { Content = "Traduzir online", Margin = new Thickness(4) };
    private bool captureChoiceScheduled;
    private sealed record LanguageChoice(string Label, string Code);

    private void BuildOcr()
    {
        var panel = Page("ocr");
        targetLanguage.ItemsSource = new[]
        {
            new LanguageChoice("Português", "pt"), new LanguageChoice("Inglês", "en"),
            new LanguageChoice("Espanhol", "es"), new LanguageChoice("Francês", "fr"), new LanguageChoice("Alemão", "de")
        };
        targetLanguage.SelectedIndex = 0;
        capture.Click += (_, _) => RequestCaptureMode();
        tabs.SelectionChanged += (_, args) =>
        {
            if (args.Source == tabs && tabs.SelectedItem == pages["ocr"]) ScheduleCaptureChoice();
        };
        translateButton.Click += (_, _) => Run(ocrStatus, TranslateOcr);
        Top(panel, Row(capture, Button("Copiar original", () => CopyText(ocrText))));
        Top(panel, Row(targetLanguage, translateButton, autoTranslate, Button("Copiar tradução", () => CopyText(translation))));
        Top(panel, ocrStatus);
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var original = new DockPanel { Margin = new Thickness(4) };
        var translated = new DockPanel { Margin = new Thickness(4) };
        Top(original, Label("Texto reconhecido"));
        Top(translated, Label("Tradução"));
        original.Children.Add(ocrText);
        translated.Children.Add(translation);
        columns.Children.Add(original);
        Grid.SetColumn(translated, 1);
        columns.Children.Add(translated);
        panel.Children.Add(columns);
    }

    private void ScheduleCaptureChoice()
    {
        if (captureChoiceScheduled) return;
        captureChoiceScheduled = true;
        window.Dispatcher.BeginInvoke(() =>
        {
            captureChoiceScheduled = false;
            if (!disposed && tabs.SelectedItem == pages["ocr"]) RequestCaptureMode();
        });
    }

    private void RequestCaptureMode()
    {
        if (!capture.IsEnabled || !window.IsVisible) return;
        bool? selected = null;
        var chooser = new Window { Title = "OCR · O que você quer ler?", Owner = window,
            Width = 390, SizeToContent = SizeToContent.Height, ShowInTaskbar = false,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new Thickness(14) };
        body.Children.Add(Label("Escolha a área que será reconhecida como texto."));
        var whole = Button("Tela inteira", () => { selected = false; chooser.Close(); });
        var area = Button("Selecionar área", () => { selected = true; chooser.Close(); });
        var cancel = Button("Cancelar", chooser.Close);
        cancel.IsCancel = true;
        body.Children.Add(Row(whole, area, cancel));
        chooser.Content = body;
        chooser.Loaded += (_, _) => area.Focus();
        chooser.ShowDialog();
        if (selected is bool selectArea) Run(ocrStatus, () => CaptureOcr(selectArea));
    }

    private static void CopyText(TextBox text)
    {
        if (text.Text.Length > 0) Clipboard.SetText(text.Text);
    }

    private async Task CaptureOcr(bool selectArea)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException("Instale um idioma de OCR no Windows.");
        var bounds = Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds;
        capture.IsEnabled = translateButton.IsEnabled = false;
        var invocation = panelInvocation;
        window.Hide();
        try
        {
            await Task.Delay(250, lifetime.Token);
            if (invocation != panelInvocation) return;
            using var screenshot = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(screenshot))
                graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            var area = selectArea ? ScreenSelection.Select(screenshot, bounds) : new System.Drawing.Rectangle(System.Drawing.Point.Empty, bounds.Size);
            if (area == null) return;
            using var cropped = screenshot.Clone(area.Value, PixelFormat.Format32bppArgb);
            var factor = Math.Min(1, (double)OcrEngine.MaxImageDimension / Math.Max(cropped.Width, cropped.Height));
            using var resized = new System.Drawing.Bitmap(cropped, Math.Max(1, (int)(cropped.Width * factor)), Math.Max(1, (int)(cropped.Height * factor)));
            using var memory = new MemoryStream();
            resized.Save(memory, ImageFormat.Png);
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(memory.ToArray());
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            var recognized = await engine.RecognizeAsync(bitmap);
            if (disposed || invocation != panelInvocation) return;
            ocrText.Text = string.Join(Environment.NewLine, recognized.Lines.Select(line => line.Text));
            translation.Clear();
            ocrStatus.Text = string.IsNullOrWhiteSpace(ocrText.Text) ? "Nenhum texto reconhecido nesta área." : "";
            if (autoTranslate.IsChecked == true && ocrText.Text.Length > 0)
            {
                window.Show();
                await TranslateOcr();
            }
        }
        finally
        {
            if (!disposed)
            {
                capture.IsEnabled = translateButton.IsEnabled = true;
                if (invocation == panelInvocation) { window.Show(); window.Activate(); }
            }
        }
    }

    private async Task TranslateOcr()
    {
        if (string.IsNullOrWhiteSpace(ocrText.Text)) { ocrStatus.Text = "Capture texto antes de traduzir."; return; }
        var key = Environment.GetEnvironmentVariable("ACTIONBAR_TRANSLATOR_KEY");
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Configure ACTIONBAR_TRANSLATOR_KEY e, para recursos regionais, ACTIONBAR_TRANSLATOR_REGION.");
        if (ocrText.Text.Length > 50000) throw new InvalidOperationException("A tradução aceita até 50 mil caracteres por captura.");
        var language = (string?)targetLanguage.SelectedValue ?? "pt";
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.cognitive.microsofttranslator.com/translate?api-version=3.0&to=" + language)
        {
            Content = JsonContent.Create(new[] { new { Text = ocrText.Text } })
        };
        message.Headers.Add("Ocp-Apim-Subscription-Key", key);
        var region = Environment.GetEnvironmentVariable("ACTIONBAR_TRANSLATOR_REGION");
        if (!string.IsNullOrWhiteSpace(region)) message.Headers.Add("Ocp-Apim-Subscription-Region", region);
        var invocation = panelInvocation;
        capture.IsEnabled = translateButton.IsEnabled = false;
        ocrStatus.Text = "Traduzindo…";
        try
        {
            using var response = await Http.SendAsync(message, lifetime.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"A tradução retornou HTTP {(int)response.StatusCode}.");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
            if (disposed || invocation != panelInvocation) return;
            translation.Text = json.RootElement[0].GetProperty("translations")[0].GetProperty("text").GetString() ?? "";
            ocrStatus.Text = "Tradução concluída.";
        }
        finally
        {
            if (!disposed) capture.IsEnabled = translateButton.IsEnabled = true;
        }
    }
}

