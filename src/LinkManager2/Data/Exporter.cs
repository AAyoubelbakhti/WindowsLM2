using System.Text.Json;

namespace LinkManager2.Data;

public static class Exporter
{
    public static async Task ExportJsonAsync(
        IReadOnlyList<Item> items,
        IReadOnlyList<Category> categories,
        string? userEmail,
        string path,
        CancellationToken ct = default)
    {
        var payload = new
        {
            format = "linkmanager-export",
            version = 1,
            exported_at = DateTime.UtcNow.ToString("O"),
            user_email = userEmail,
            categories = categories.Select(c => c.Name).ToArray(),
            items = items.Select(i => new
            {
                title = i.Title,
                value = i.Value,
                type = i.Type,
                description = i.Description,
                category = categories.FirstOrDefault(c => c.Id == i.CategoryId)?.Name,
                created_at = i.CreatedAt.ToString("O"),
            }).ToArray(),
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct);
    }
}
