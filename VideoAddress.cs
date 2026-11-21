using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace BarraInteligente;

internal static class VideoAddress
{
    internal static bool TryParse(string input, out string id)
    {
        id = "";
        var value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http")) return false;
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? candidate = null;
        if (url.IdnHost == "youtu.be")
            candidate = parts.FirstOrDefault();
        else if (url.IdnHost is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or "www.youtube-nocookie.com")
        {
            if (parts.Length == 2 && parts[0] is "live" or "shorts" or "embed" or "v") candidate = parts[1];
            else if (url.AbsolutePath == "/watch")
                candidate = url.Query.TrimStart('?').Split('&').FirstOrDefault(part => part.StartsWith("v=", StringComparison.Ordinal))?[2..];
        }
        if (candidate == null || !Regex.IsMatch(candidate, @"\A[A-Za-z0-9_-]{11}\z")) return false;
        id = candidate;
        return true;
    }
}

