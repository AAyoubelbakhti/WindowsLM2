using Microsoft.Data.Sqlite;

namespace LinkManager2.Data;

/// <summary>
/// Local SQLite mirror of items and an offline pending-ops queue, segregated per user.
/// Every row carries the owning user id so a second account on the same machine never
/// sees the first account's cache and never inherits its queued writes.
/// </summary>
public sealed class LocalCache : IDisposable
{
    private readonly SqliteConnection _conn;

    private string _userId = string.Empty;

    public LocalCache()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LinkManager");
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "cache.db");

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        EnsureSchema();
    }

    public void SetUser(string? userId) => _userId = userId ?? string.Empty;

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS items_cache (
                id TEXT PRIMARY KEY,
                user_id TEXT NOT NULL DEFAULT '',
                title TEXT NOT NULL,
                value TEXT NOT NULL,
                type TEXT NOT NULL,
                category_id TEXT,
                category_name TEXT,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS pending_ops (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id TEXT NOT NULL DEFAULT '',
                op TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
        """;
        cmd.ExecuteNonQuery();
        MigrateAddUserIdColumn("items_cache");
        MigrateAddUserIdColumn("pending_ops");
    }

    private void MigrateAddUserIdColumn(string table)
    {
        if (ColumnExists(table, "user_id")) return;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN user_id TEXT NOT NULL DEFAULT ''";
        cmd.ExecuteNonQuery();
    }

    private bool ColumnExists(string table, string column)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public void ReplaceItems(IEnumerable<Item> items, IEnumerable<Category> categories)
    {
        var cats = categories.ToDictionary(c => c.Id, c => c.Name);
        using var tx = _conn.BeginTransaction();
        using (var del = _conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM items_cache WHERE user_id = $uid";
            del.Parameters.AddWithValue("$uid", _userId);
            del.ExecuteNonQuery();
        }
        foreach (var i in items)
        {
            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT OR REPLACE INTO items_cache
                  (id, user_id, title, value, type, category_id, category_name, updated_at)
                VALUES ($id, $uid, $title, $value, $type, $cat_id, $cat_name, $updated)
            """;
            ins.Parameters.AddWithValue("$id", i.Id);
            ins.Parameters.AddWithValue("$uid", _userId);
            ins.Parameters.AddWithValue("$title", i.Title);
            ins.Parameters.AddWithValue("$value", i.Value);
            ins.Parameters.AddWithValue("$type", i.Type);
            ins.Parameters.AddWithValue("$cat_id", (object?)i.CategoryId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$cat_name", i.CategoryId is not null && cats.TryGetValue(i.CategoryId, out var n) ? n : DBNull.Value);
            ins.Parameters.AddWithValue("$updated", i.UpdatedAt.ToString("O"));
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<CachedItem> LoadItems()
    {
        var result = new List<CachedItem>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, value, type, category_id, category_name FROM items_cache WHERE user_id = $uid ORDER BY title COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$uid", _userId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new CachedItem(
                Id: r.GetString(0),
                Title: r.GetString(1),
                Value: r.GetString(2),
                Type: r.GetString(3),
                CategoryId: r.IsDBNull(4) ? null : r.GetString(4),
                CategoryName: r.IsDBNull(5) ? null : r.GetString(5)));
        }
        return result;
    }

    public void EnqueuePending(string op, string payloadJson)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "INSERT INTO pending_ops (user_id, op, payload) VALUES ($uid, $op, $p)";
        cmd.Parameters.AddWithValue("$uid", _userId);
        cmd.Parameters.AddWithValue("$op", op);
        cmd.Parameters.AddWithValue("$p", payloadJson);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<(long Id, string Op, string Payload)> ReadPending()
    {
        var list = new List<(long, string, string)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, op, payload FROM pending_ops WHERE user_id = $uid ORDER BY id";
        cmd.Parameters.AddWithValue("$uid", _userId);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
        return list;
    }

    public void DeletePending(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_ops WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void ClearUser()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM items_cache WHERE user_id = $uid; DELETE FROM pending_ops WHERE user_id = $uid;";
        cmd.Parameters.AddWithValue("$uid", _userId);
        cmd.ExecuteNonQuery();
    }

    public void Dispose() => _conn.Dispose();
}

public sealed record CachedItem(
    string Id,
    string Title,
    string Value,
    string Type,
    string? CategoryId,
    string? CategoryName);
