using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace LinkManager2.Data;

public static class BrowserUrlReader
{

    private static readonly string[] AddressBarFragments =
    {
        "address and search bar",
        "barra de direcciones",
        "search with",
        "address bar",
        "barra de búsqueda",
    };

    private static readonly string[] AddressBarAutomationIds =
    {
        "urlbar-input",
        "view_1022",
        "omnibox",
        "addressEditBox",
    };

    public sealed record Capture(string Url, string? Title);

    public static Capture? TryCapture(IntPtr hwnd = default)
    {
        try
        {
            if (hwnd == IntPtr.Zero) hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            var root = AutomationElement.FromHandle(hwnd);
            if (root is null) return null;

            var windowTitle = root.Current.Name;

            var found = FindAddressBar(root);
            if (found is null) return null;
            var (bar, trusted) = found.Value;

            if (!bar.TryGetCurrentPattern(ValuePattern.Pattern, out var p)) return null;
            var raw = ((ValuePattern)p).Current.Value;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            if (!trusted && !LooksLikeUrl(raw)) return null;

            var url = NormalizeBrowserBarValue(raw);
            if (string.IsNullOrEmpty(url)) return null;

            return new Capture(url, CleanWindowTitle(windowTitle));
        }
        catch
        {

            return null;
        }
    }

    private static (AutomationElement Bar, bool Trusted)? FindAddressBar(AutomationElement root)
    {
        var edits = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

        AutomationElement? best = null;
        var bestTop = int.MaxValue;
        foreach (AutomationElement edit in edits)
        {
            var automationId = edit.Current.AutomationId ?? string.Empty;
            foreach (var id in AddressBarAutomationIds)
            {
                if (automationId.Equals(id, StringComparison.OrdinalIgnoreCase))
                    return (edit, true);
            }

            var name = edit.Current.Name ?? string.Empty;
            foreach (var frag in AddressBarFragments)
            {
                if (name.Contains(frag, StringComparison.OrdinalIgnoreCase))
                    return (edit, true);
            }

            var top = (int)edit.Current.BoundingRectangle.Top;
            if (top < bestTop) { bestTop = top; best = edit; }
        }
        return best is null ? null : (best, false);
    }

    private static bool LooksLikeUrl(string value)
    {
        var v = value.Trim();
        if (v.Length == 0 || v.Contains(' ')) return false;
        if (Uri.TryCreate(v, UriKind.Absolute, out var abs)
            && (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            return true;
        return v.Contains('.');
    }

    private static string NormalizeBrowserBarValue(string value)
    {
        var v = value.Trim();
        if (v.Length == 0) return string.Empty;
        if (v.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) ||
            v.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            return v;

        return "https://" + v;
    }

    private static string? CleanWindowTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        foreach (var sep in new[] { " - ", " — ", " – " })
        {
            var idx = title.LastIndexOf(sep, StringComparison.Ordinal);
            if (idx > 0) return title[..idx].Trim();
        }
        return title.Trim();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
