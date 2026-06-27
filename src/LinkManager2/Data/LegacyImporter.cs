using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LinkManager2.Data;

public static class LegacyImporter
{
    public sealed record ParsedItem(string Title, string Value, string Type, string? Category);
    public sealed record ParsedData(IReadOnlyList<string> Categories, IReadOnlyList<ParsedItem> Items);

    public static ParsedData ParseJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ParsedItem>();

        foreach (var prop in root.EnumerateObject())
        {
            if (prop.NameEquals("__user_defined_categories__"))
            {
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in prop.Value.EnumerateArray())
                    {
                        if (c.ValueKind == JsonValueKind.String) categories.Add(c.GetString()!);
                    }
                }
                continue;
            }

            string? value = null;
            string type = "url";
            string? category = null;

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                value = prop.Value.GetString();
            }
            else if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                if (prop.Value.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                {
                    value = u.GetString(); type = "url";
                }
                else if (prop.Value.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String)
                {
                    value = p.GetString(); type = "path";
                }
                if (prop.Value.TryGetProperty("categories", out var c))
                {
                    if (c.ValueKind == JsonValueKind.Array && c.GetArrayLength() > 0)
                    {
                        category = c[0].GetString();
                    }
                    else if (c.ValueKind == JsonValueKind.String)
                    {
                        category = c.GetString();
                    }
                }
            }

            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(prop.Name)) continue;
            if (!string.IsNullOrEmpty(category)) categories.Add(category);
            items.Add(new ParsedItem(prop.Name, value, type, category));
        }

        return new ParsedData(categories.ToList(), items);
    }

    public static ParsedData ParseLinkManagerJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var categories = new List<string>();
        var items = new List<ParsedItem>();

        if (root.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cats.EnumerateArray())
                if (c.ValueKind == JsonValueKind.String) categories.Add(c.GetString()!);
        }

        if (root.TryGetProperty("items", out var its) && its.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in its.EnumerateArray())
            {
                var title = i.TryGetProperty("title", out var t) ? t.GetString() : null;
                var value = i.TryGetProperty("value", out var v) ? v.GetString() : null;
                var type = i.TryGetProperty("type", out var ty) && ty.GetString() == "path" ? "path" : "url";
                var category = i.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(value))
                    items.Add(new ParsedItem(title, value, type, category));
            }
        }

        return new ParsedData(categories, items);
    }

    public static ParsedData ParseSqlite(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();

        var cats = new Dictionary<long, string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM categories";
            using var r = cmd.ExecuteReader();
            while (r.Read()) cats[r.GetInt64(0)] = r.GetString(1);
        }

        var items = new List<ParsedItem>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT title, value, type, category_id FROM items";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var title = r.GetString(0);
                var value = r.GetString(1);
                var type = r.IsDBNull(2) ? "url" : (r.GetString(2) == "path" ? "path" : "url");
                string? category = null;
                if (!r.IsDBNull(3))
                {
                    var catId = r.GetInt64(3);
                    if (cats.TryGetValue(catId, out var name)) category = name;
                }
                items.Add(new ParsedItem(title, value, type, category));
            }
        }

        return new ParsedData(cats.Values.Distinct().ToList(), items);
    }
}
