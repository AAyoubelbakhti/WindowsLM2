using LinkManager2.Data;
using Microsoft.Windows.ApplicationModel.Resources;

namespace LinkManager2;

internal static partial class Strings
{
    private static readonly Lazy<ResourceLoader?> Loader = new(CreateLoader);

    private static partial string? Shipped(string key)
    {
        try
        {
            return Loader.Value?.GetString(UidKeys.ResourcePath(key));
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"missing string '{key}'", ex);
            return null;
        }
    }

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
