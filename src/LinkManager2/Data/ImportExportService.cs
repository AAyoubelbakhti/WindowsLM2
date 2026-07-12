using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace LinkManager2.Data;

public readonly record struct ImportResult(int Inserted, int Skipped);

public static class ImportExportService
{

    public static async Task<ImportResult> ImportFileAsync(AppState state, string path)
    {
        var parsed = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? ParseJsonAuto(await File.ReadAllTextAsync(path))
            : LegacyImporter.ParseSqlite(path);
        return await BulkInsertAsync(state, parsed);
    }

    /// <summary>
    /// Routes JSON text to the right parser: the exporter format (items/categories arrays)
    /// or the legacy title-to-url dictionary. Throws when neither format is recognized.
    /// </summary>
    private static LegacyImporter.ParsedData ParseJsonAuto(string text)
    {
        bool isExporterFormat;
        using (var doc = JsonDocument.Parse(text))
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("Formato JSON no reconocido.");
            isExporterFormat = root.TryGetProperty("items", out _) || root.TryGetProperty("categories", out _);
        }

        if (isExporterFormat) return LegacyImporter.ParseLinkManagerJson(text);

        var legacy = LegacyImporter.ParseJson(text);
        if (legacy.Items.Count == 0 && legacy.Categories.Count == 0)
            throw new FormatException("Formato JSON no reconocido.");
        return legacy;
    }

    private static async Task<ImportResult> BulkInsertAsync(AppState state, LegacyImporter.ParsedData parsed)
    {
        var existingTitles = new HashSet<string>(state.Items.Select(i => i.Title), StringComparer.OrdinalIgnoreCase);
        var existingCats = state.Categories.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var wantedCats = new HashSet<string>(parsed.Categories, StringComparer.OrdinalIgnoreCase);
        foreach (var p in parsed.Items)
            if (!string.IsNullOrWhiteSpace(p.Category)) wantedCats.Add(p.Category!);
        foreach (var name in wantedCats)
        {
            if (existingCats.ContainsKey(name)) continue;
            try
            {
                var created = await state.Repo.AddCategoryAsync(name);
                existingCats[created.Name] = created.Id;
            }
            catch (Exception ex) { Diagnostics.Log($"import create category '{name}'", ex); }
        }

        var inserted = 0; var skipped = 0;
        foreach (var p in parsed.Items)
        {
            if (existingTitles.Contains(p.Title)) { skipped++; continue; }
            var catId = p.Category is not null && existingCats.TryGetValue(p.Category, out var id) ? id : null;
            try
            {
                await state.Repo.AddAsync(p.Title, p.Value, p.Type, catId);
                inserted++;
                existingTitles.Add(p.Title);
            }
            catch (Exception ex) { Diagnostics.Log($"import insert '{p.Title}'", ex); skipped++; }
        }
        return new ImportResult(inserted, skipped);
    }

    public static Task ExportJsonAsync(AppState state, string path) =>
        Exporter.ExportJsonAsync(state.Items, state.Categories, state.Auth.UserEmail, path);
}
