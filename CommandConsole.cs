using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BarraInteligente;

internal static class CommandConsole
{
    internal static async Task<int> Execute(string command, IProgress<string> output, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        // The package-manager list command can otherwise stop at a source agreement prompt.
        if (Regex.IsMatch(command, @"\Awinget(?:\.exe)?\s+(?:update|upgrade)\z", RegexOptions.IgnoreCase))
            command += " --disable-interactivity";
        var script = "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n" +
            "$OutputEncoding = [Console]::OutputEncoding\n$global:LASTEXITCODE = 0\n" +
            command + "\nif (!$?) { if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }; exit 1 }\nexit $LASTEXITCODE";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-OutputFormat", "Text", "-EncodedCommand", encoded })
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o PowerShell.");
        process.StandardInput.Close();
        using var reads = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var stdout = Pump(process.StandardOutput, output, reads.Token);
        var stderr = Pump(process.StandardError, output, reads.Token);
        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            // A child started by the command can inherit the pipes after PowerShell exits.
            reads.CancelAfter(TimeSpan.FromSeconds(3));
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            finally
            {
                reads.Cancel();
                try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
            throw;
        }
    }

    private static async Task Pump(StreamReader reader, IProgress<string> output, CancellationToken token)
    {
        var buffer = new char[2048];
        int count, sent = 0;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            if (sent >= 262144) continue;
            sent += count;
            output.Report(Regex.Replace(new string(buffer, 0, count), @"\x1b\[[0-?]*[ -/]*[@-~]|[\x00-\x08\x0B\x0C\x0E-\x1F]", ""));
            if (sent >= 262144) output.Report("\n[Limite de saída atingido.]\n");
        }
    }
}

