using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BarraInteligente;

internal sealed partial class BarPanels
{
    private const string TerminalHost = "terminal.actionbar.invalid";
    private const string TerminalPage = "https://" + TerminalHost + "/index.html";
    private readonly TextBlock terminalStatus = Label("PowerShell · digite no terminal. Ctrl+C interrompe o comando.");
    private WebView2? terminalView;
    private Task? terminalLoad;
    private PseudoTerminal? session;
    private bool terminalReady;
    private string? pendingCommand;
    private int sessionVersion;
    private short terminalCols = 78, terminalRows = 22;

    private void BuildTerminal()
    {
        var page = Page("terminal");
        Top(page, Row(Button("Nova sessão", () => Run(terminalStatus, async () =>
        {
            await EnsureTerminal();
            if (terminalReady && !disposed) StartTerminal();
        })), Button("Interromper · Ctrl+C", () => session?.Send("\x03"))));
        Top(page, terminalStatus);
        tabs.SelectionChanged += (_, args) =>
        {
            if (args.Source == tabs && tabs.SelectedItem == pages["terminal"])
                Run(terminalStatus, EnsureTerminal);
        };
    }

    private Task EnsureTerminal() => terminalLoad ??= LoadTerminal();

    private async Task LoadTerminal()
    {
        var panel = (DockPanel)pages["terminal"].Content;
        var view = new WebView2();
        terminalView = view;
        panel.Children.Add(view);
        try
        {
            await view.EnsureCoreWebView2Async();
            if (disposed) return;
            var browser = view.CoreWebView2;
            browser.Settings.AreHostObjectsAllowed = false;
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.AreDefaultScriptDialogsEnabled = false;
            browser.Settings.IsWebMessageEnabled = true;
            browser.SetVirtualHostNameToFolderMapping(TerminalHost, Path.Combine(AppContext.BaseDirectory, "Assets", "Terminal"),
                CoreWebView2HostResourceAccessKind.Deny);
            browser.NavigationStarting += (_, args) => args.Cancel = !string.Equals(args.Uri, TerminalPage, StringComparison.Ordinal);
            browser.FrameNavigationStarting += (_, args) => args.Cancel = true;
            browser.NewWindowRequested += (_, args) => args.Handled = true;
            browser.WebMessageReceived += (_, args) =>
            {
                if (!disposed && args.Source == TerminalPage) ReceiveTerminalMessage(args.WebMessageAsJson);
            };
            browser.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess && !disposed) terminalStatus.Text = "Falha ao carregar o terminal: " + args.WebErrorStatus;
            };
            browser.Navigate(TerminalPage);
        }
        catch
        {
            panel.Children.Remove(view);
            view.Dispose();
            terminalView = null;
            terminalLoad = null;
            throw;
        }
    }

    private void ReceiveTerminalMessage(string json)
    {
        using var document = JsonDocument.Parse(json);
        var message = document.RootElement;
        switch (message.GetProperty("type").GetString())
        {
            case "ready":
                terminalReady = true;
                ReadTerminalSize(message);
                if (session == null) Run(terminalStatus, () => { StartTerminal(); return Task.CompletedTask; });
                break;
            case "resize":
                ReadTerminalSize(message);
                session?.Resize(terminalCols, terminalRows);
                break;
            case "input":
                var text = message.GetProperty("text").GetString();
                if (text is { Length: > 0 and <= 4000 } && session?.Send(text) == false)
                    terminalStatus.Text = "O terminal ainda está processando a entrada. Aguarde e tente novamente.";
                break;
            case "copy":
                var selection = message.GetProperty("text").GetString();
                if (selection is { Length: > 0 and <= 262144 })
                    Run(terminalStatus, () => { Clipboard.SetText(selection); return Task.CompletedTask; });
                break;
        }
    }

    private void ReadTerminalSize(JsonElement message)
    {
        terminalCols = (short)Math.Clamp(message.GetProperty("cols").GetInt32(), 20, 350);
        terminalRows = (short)Math.Clamp(message.GetProperty("rows").GetInt32(), 6, 160);
    }

    private void StartTerminal()
    {
        var version = ++sessionVersion;
        session?.Dispose();
        session = null;
        PostTerminal("reset");
        session = new PseudoTerminal(terminalCols, terminalRows,
            text => window.Dispatcher.Invoke(() =>
            {
                if (!disposed && sessionVersion == version) PostTerminal("output", text);
            }), code => window.Dispatcher.BeginInvoke(() =>
            {
                if (disposed || sessionVersion != version) return;
                session = null;
                PostTerminal("output", $"\r\nSessão encerrada ({code}). Use Nova sessão para reabrir.\r\n");
            }));
        terminalStatus.Text = "PowerShell · digite no terminal. Ctrl+C interrompe o comando.";
        FocusTerminal();
    }

    private void FocusTerminal()
    {
        if (!terminalReady || session == null || tabs.SelectedItem != pages["terminal"]) return;
        terminalView?.Focus();
        PostTerminal("focus");
        if (pendingCommand is not { Length: > 0 } command) return;
        pendingCommand = null;
        // Stage search text as input. Enter remains an explicit action inside the terminal.
        session.Send(new string(command.Where(character => !char.IsControl(character)).Take(4000).ToArray()));
    }

    private void PostTerminal(string type, string text = "") =>
        terminalView?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(new { type, text }));
}

