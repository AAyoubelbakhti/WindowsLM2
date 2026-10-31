using System.Globalization;
using LinkManager2.Data;
using Microsoft.Windows.ApplicationModel.Resources;

namespace LinkManager2;

/// <summary>
/// User-facing texts from Strings/{language}/Resources.resw. A missing key returns the key itself
/// and is logged, so a typo shows on screen instead of closing the app.
/// </summary>
internal static class Strings
{
    private static readonly Lazy<ResourceLoader?> Loader = new(CreateLoader);

    public static string Get(string key)
    {
        try
        {
            return Loader.Value?.GetString(key) ?? key;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"missing string '{key}'", ex);
            return key;
        }
    }

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static ResourceLoader? CreateLoader()
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception ex)
        {
            Diagnostics.Log("strings loader", ex);
            return null;
        }
    }
}
