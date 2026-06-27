using System.Text.RegularExpressions;

namespace LinkManager2.Data;

public static partial class Validation
{
    [GeneratedRegex(
        @"^(https?://|ftp://|file://|www\.)|^(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public static (bool ok, string type) DetectType(string value)
    {
        var v = value.Trim().Trim('"');
        if (string.IsNullOrEmpty(v)) return (false, "invalid");
        if (UrlRegex().IsMatch(v)) return (true, "url");
        if (File.Exists(v) || Directory.Exists(v) || v.Contains('\\')) return (true, "path");
        return (false, "invalid");
    }

    public static string NormalizeUrl(string value)
    {
        var v = value.Trim().Trim('"');
        if (v.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) return "https://" + v;
        if (!v.Contains("://") && UrlRegex().IsMatch(v)) return "https://" + v;
        return v;
    }
}
