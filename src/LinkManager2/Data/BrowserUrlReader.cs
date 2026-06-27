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

            var bar = FindAddressBar(root);
            if (bar is null) return null;

            if (!bar.TryGetCurrentPattern(ValuePattern.Pattern, out var p)) return null;
            var raw = ((ValuePattern)p).Current.Value;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var url = NormalizeBrowserBarValue(raw);
            if (string.IsNullOrEmpty(url)) return null;

            return new Capture(url, CleanWindowTitle(windowTitle));
        }
        catch
        {

            return null;
        }
    }

    private static AutomationElement? FindAddressBar(AutomationElement root)
    {

        var edits = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

        AutomationElement? best = null;
        var bestTop = int.MaxValue;
        foreach (AutomationElement edit in edits)
        {
            var name = edit.Current.Name ?? string.Empty;
            foreach (var frag in AddressBarFragments)
            {
                if (name.Contains(frag, StringComparison.OrdinalIgnoreCase))
                    return edit;
            }

            var top = (int)edit.Current.BoundingRectangle.Top;
            if (top < bestTop) { bestTop = top; best = edit; }
        }
        return best;
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
