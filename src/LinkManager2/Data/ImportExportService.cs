using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace LinkManager2.Data;

public readonly record struct ImportResult(int Inserted, int Skipped);

public static class ImportExportService
{

    public static async Task<ImportResult> ImportFileAsync(AppState state, string path)
    {
        var parsed = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? LegacyImporter.ParseLinkManagerJson(await File.ReadAllTextAsync(path))
            : LegacyImporter.ParseSqlite(path);
        return await BulkInsertAsync(state, parsed);
    }

    private static async Task<ImportResult> BulkInsertAsync(AppState state, LegacyImporter.ParsedData parsed)
    {
        var existingTitles = new HashSet<string>(state.Items.Select(i => i.Title), StringComparer.OrdinalIgnoreCase);
        var existingCats = state.Categories.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
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
