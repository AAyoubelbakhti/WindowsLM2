namespace LinkManager2;

/// <summary>Stands in for the app's resources, which need the Windows App SDK: its own texts are the base ones.</summary>
internal static partial class Strings
{
    private static partial string? Shipped(string key) => Base.GetValueOrDefault(key);
}
