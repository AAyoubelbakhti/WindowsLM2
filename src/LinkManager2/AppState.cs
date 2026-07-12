using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using LinkManager2.Data;

namespace LinkManager2;

public sealed class AppState
{
    public AuthService Auth { get; }
    public ItemsRepository Repo { get; }
    public LocalCache Cache { get; }

    public IReadOnlyList<Item> Items => _items;
    public IReadOnlyList<Category> Categories => _categories;
    public IReadOnlyList<Tag> Tags => _tags;
    public UserSettings? Settings { get; private set; }

    private List<Item> _items = new();
    private List<Category> _categories = new();
    private List<Tag> _tags = new();
    private Dictionary<string, HashSet<string>> _itemTags = new();

    public bool ItemHasTag(string itemId, string tagId) =>
        _itemTags.TryGetValue(itemId, out var set) && set.Contains(tagId);

    private AppState()
    {
        Auth = new AuthService();
        Repo = new ItemsRepository();
        Cache = new LocalCache();
    }

    private void SyncCacheUser() => Cache.SetUser(Auth.CurrentUser?.Id);

    public static (string Url, string Key) LoadConfig()
    {
        var defaults = ReadFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        var local = ReadFile(Path.Combine(AppContext.BaseDirectory, "appsettings.Local.json"));
        var url = FirstNonEmpty(
            Environment.GetEnvironmentVariable("LM_SUPABASE_URL"),
            local.Url, defaults.Url);
        var key = FirstNonEmpty(
            Environment.GetEnvironmentVariable("LM_SUPABASE_ANON_KEY"),
            local.Key, defaults.Key);
        return (url, key);
    }

    public static AppState Create() => new();

    public Task WaitReadyAsync() => SupabaseClientHolder.Ready;

    public async Task ReloadAllAsync()
    {
        var itemsTask = Repo.ListAsync();
        var catsTask = Repo.ListCategoriesAsync();
        var tagsTask = Repo.ListTagsAsync();
        var itemTagsTask = Repo.ListAllItemTagsAsync();
        var settingsTask = Repo.GetSettingsAsync();
        await Task.WhenAll(itemsTask, catsTask, tagsTask, itemTagsTask, settingsTask);
        _items = (await itemsTask).ToList();
        _categories = (await catsTask).ToList();
        _tags = (await tagsTask).ToList();
        _itemTags = BuildItemTagMap(await itemTagsTask);
        Settings = await settingsTask;
        SyncCacheUser();
        Cache.ReplaceItems(_items, _categories);

        _ = RegisterDeviceOnceAsync();
    }

    private bool _deviceRegistered;

    private async Task RegisterDeviceOnceAsync()
    {
        if (_deviceRegistered) return;
        _deviceRegistered = true;
        try
        {
            var name = Environment.MachineName;
            if (string.IsNullOrWhiteSpace(name)) name = "Windows";
            await Repo.RegisterDeviceAsync(LocalPreferences.GetOrCreateDeviceId(), name);
        }
        catch
        {
            _deviceRegistered = false;
        }
    }

    private static Dictionary<string, HashSet<string>> BuildItemTagMap(IReadOnlyList<ItemTag> rows)
    {
        var map = new Dictionary<string, HashSet<string>>();
        foreach (var r in rows)
        {
            if (!map.TryGetValue(r.ItemId, out var set))
                map[r.ItemId] = set = new HashSet<string>();
            set.Add(r.TagId);
        }
        return map;
    }

    public async Task ReloadItemsAsync()
    {
        _items = (await Repo.ListAsync()).ToList();
        SyncCacheUser();
        Cache.ReplaceItems(_items, _categories);
    }

    public enum AddResult { Added, QueuedOffline }

    /// <summary>Outcome of a mutating item operation: applied online or queued for later flush.</summary>
    public enum OpResult { Done, QueuedOffline }

    public async Task<AddResult> AddItemAsync(string title, string value, string type, string? categoryId,
        IReadOnlyList<string>? tagIds = null, string? description = null)
    {
        try
        {
            var item = await Repo.AddAsync(title, value, type, categoryId, description);
            if (tagIds is { Count: > 0 }) await Repo.SetItemTagsAsync(item.Id, tagIds);
            await ReloadItemsAsync();
            return AddResult.Added;
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            SyncCacheUser();
            Cache.EnqueuePending("add",
                JsonSerializer.Serialize(new PendingAdd(title, value, type, categoryId, tagIds?.ToList(), description)));
            return AddResult.QueuedOffline;
        }
    }

    /// <summary>Hard-deletes an item; queues the delete offline if the network is unavailable. Idempotent on replay.</summary>
    public async Task<OpResult> DeleteItemAsync(string id)
    {
        try
        {
            await Repo.DeleteAsync(id);
            await ReloadItemsAsync();
            return OpResult.Done;
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            SyncCacheUser();
            Cache.EnqueuePending("delete", JsonSerializer.Serialize(new PendingDelete(id)));
            return OpResult.QueuedOffline;
        }
    }

    /// <summary>Archives or unarchives an item; queues offline when the network is unavailable. Idempotent on replay.</summary>
    public async Task<OpResult> ArchiveItemAsync(string id, bool archived)
    {
        try
        {
            await Repo.ArchiveAsync(id, archived);
            await ReloadItemsAsync();
            return OpResult.Done;
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            SyncCacheUser();
            Cache.EnqueuePending("archive", JsonSerializer.Serialize(new PendingArchive(id, archived)));
            return OpResult.QueuedOffline;
        }
    }

    /// <summary>Sets an item's favorite flag; queues offline when the network is unavailable. Idempotent on replay.</summary>
    public async Task<OpResult> SetFavoriteAsync(string id, bool isFavorite)
    {
        try
        {
            await Repo.ToggleFavoriteAsync(id, isFavorite);
            await ReloadItemsAsync();
            return OpResult.Done;
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            SyncCacheUser();
            Cache.EnqueuePending("favorite", JsonSerializer.Serialize(new PendingFavorite(id, isFavorite)));
            return OpResult.QueuedOffline;
        }
    }

    /// <summary>
    /// Updates an item's core fields; queues offline when the network is unavailable. Replay is a
    /// full field overwrite (last-write-wins, matching the project sync policy): a value edited
    /// elsewhere while offline is overwritten on flush rather than three-way merged.
    /// </summary>
    public async Task<OpResult> UpdateItemAsync(string id, string title, string value, string type,
        string? categoryId, string? description)
    {
        try
        {
            await Repo.UpdateAsync(id, title, value, type, categoryId, description);
            await ReloadItemsAsync();
            return OpResult.Done;
        }
        catch (Exception ex) when (IsRetryable(ex))
        {
            SyncCacheUser();
            Cache.EnqueuePending("update",
                JsonSerializer.Serialize(new PendingUpdate(id, title, value, type, categoryId, description)));
            return OpResult.QueuedOffline;
        }
    }

    private readonly SemaphoreSlim _flushGate = new(1, 1);

    public async Task FlushPendingAsync()
    {
        if (!await _flushGate.WaitAsync(0)) return;
        try
        {
            SyncCacheUser();
            var pending = Cache.ReadPending();
            if (pending.Count == 0) return;
            var flushedCount = 0;
            var droppedCount = 0;
            var brokeOffline = false;
            foreach (var (id, op, payload) in pending)
            {
                try
                {
                    await ReplayPendingAsync(op, payload);
                    Cache.DeletePending(id);
                    flushedCount++;
                }
                catch (Exception ex) when (IsRetryable(ex))
                {
                    brokeOffline = true;
                    break;
                }
                catch (Exception ex)
                {
                    Diagnostics.Log($"flush-pending drop op {id}", ex);
                    Cache.DeletePending(id);
                    droppedCount++;
                }
            }

            if (droppedCount > 0)
            {
                Notifications.Show("Cambios pendientes descartados",
                    droppedCount == 1
                        ? "1 cambio pendiente fue rechazado por el servidor y se descartó."
                        : $"{droppedCount} cambios pendientes fueron rechazados por el servidor y se descartaron.");
            }
            if (flushedCount > 0 && !brokeOffline)
            {
                await ReloadItemsAsync();
                Notifications.Show("Cola sincronizada",
                    flushedCount == 1
                        ? "Se aplicó 1 cambio que estaba pendiente."
                        : $"Se aplicaron {flushedCount} cambios que estaban pendientes.");
            }
        }
        finally { _flushGate.Release(); }
    }

    /// <summary>
    /// Re-applies one queued operation against the server. Unknown op types are logged and treated
    /// as non-retryable (dropped by the caller) so a corrupt or future entry can't wedge the queue.
    /// </summary>
    private async Task ReplayPendingAsync(string op, string payload)
    {
        switch (op)
        {
            case "add":
            {
                var p = JsonSerializer.Deserialize<PendingAdd>(payload)!;
                var added = await Repo.AddAsync(p.Title, p.Value, p.Type, p.CategoryId, p.Description);
                if (p.TagIds is { Count: > 0 }) await Repo.SetItemTagsAsync(added.Id, p.TagIds);
                break;
            }
            case "delete":
            {
                var p = JsonSerializer.Deserialize<PendingDelete>(payload)!;
                await Repo.DeleteAsync(p.Id);
                break;
            }
            case "archive":
            {
                var p = JsonSerializer.Deserialize<PendingArchive>(payload)!;
                await Repo.ArchiveAsync(p.Id, p.Archived);
                break;
            }
            case "favorite":
            {
                var p = JsonSerializer.Deserialize<PendingFavorite>(payload)!;
                await Repo.ToggleFavoriteAsync(p.Id, p.IsFavorite);
                break;
            }
            case "update":
            {
                var p = JsonSerializer.Deserialize<PendingUpdate>(payload)!;
                await Repo.UpdateAsync(p.Id, p.Title, p.Value, p.Type, p.CategoryId, p.Description);
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown pending op '{op}'.");
        }
    }

    private static bool IsRetryable(Exception? ex)
    {
        for (var cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur is HttpRequestException or TaskCanceledException or SocketException or IOException)
                return true;
            if (cur is Supabase.Postgrest.Exceptions.PostgrestException pex && IsRetryableStatus(pex.StatusCode))
                return true;
        }
        return false;
    }

    private static bool IsRetryableStatus(int code) => code switch
    {
        0 => true,
        401 => true,
        408 => true,
        429 => true,
        >= 500 and < 600 => true,
        _ => false,
    };

    private sealed record PendingAdd(string Title, string Value, string Type, string? CategoryId, List<string>? TagIds = null, string? Description = null);
    private sealed record PendingDelete(string Id);
    private sealed record PendingArchive(string Id, bool Archived);
    private sealed record PendingFavorite(string Id, bool IsFavorite);
    private sealed record PendingUpdate(string Id, string Title, string Value, string Type, string? CategoryId, string? Description);

    public async Task ReloadTagsAsync()
    {

        var tagsTask = Repo.ListTagsAsync();
        var itemTagsTask = Repo.ListAllItemTagsAsync();
        await Task.WhenAll(tagsTask, itemTagsTask);
        _tags = (await tagsTask).ToList();
        _itemTags = BuildItemTagMap(await itemTagsTask);
    }

    public async Task ReloadSettingsAsync()
    {
        Settings = await Repo.GetSettingsAsync();
    }

    public void LoadCachedItems()
    {
        SyncCacheUser();
        var cached = Cache.LoadItems();
        if (cached.Count == 0) return;
        _items = cached.Select(c => new Item
        {
            Id = c.Id,
            Title = c.Title,
            Value = c.Value,
            Type = c.Type,
            CategoryId = c.CategoryId,
        }).ToList();
        _categories = cached
            .Where(c => c.CategoryId is not null && c.CategoryName is not null)
            .GroupBy(c => c.CategoryId!)
            .Select(g => new Category { Id = g.Key, Name = g.First().CategoryName! })
            .ToList();
    }

    public void ClearLocalCacheForCurrentUser()
    {
        SyncCacheUser();
        try { Cache.ClearUser(); }
        catch (Exception ex) { Diagnostics.Log("cache clear on sign-out", ex); }
    }

    public void Clear()
    {
        _items.Clear();
        _categories.Clear();
        _tags.Clear();
        _itemTags.Clear();
        Settings = null;
    }

    /// <summary>Reads Supabase config from a settings file, returning empties on any read/parse
    /// failure so the caller falls back to other sources (and ultimately a visible bootstrap error).</summary>
    private static (string Url, string Key) ReadFile(string path)
    {
        if (!File.Exists(path)) return ("", "");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("Supabase", out var sb)) return ("", "");
            var u = sb.TryGetProperty("Url", out var x) ? x.GetString() ?? "" : "";
            var k = sb.TryGetProperty("AnonKey", out var y) ? y.GetString() ?? "" : "";
            return (u, k);
        }
        catch { return ("", ""); }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v;
        return string.Empty;
    }
}
