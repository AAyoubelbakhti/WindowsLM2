using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Supabase.Postgrest;
using static Supabase.Postgrest.Constants;

namespace LinkManager2.Data;

public sealed class ItemsRepository
{
    private readonly Supabase.Client _client = SupabaseClientHolder.Client;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<IReadOnlyList<Item>> ListAsync(CancellationToken ct = default)
    {
        var response = await _client
            .From<Item>()
            .Filter("archived_at", Operator.Is, "null")
            .Order("title", Ordering.Ascending)
            .Get(ct);
        return response.Models;
    }

    public async Task<IReadOnlyList<Item>> ListArchivedAsync(CancellationToken ct = default)
    {
        var response = await _client
            .From<Item>()
            .Not("archived_at", Operator.Is, "null")
            .Order("title", Ordering.Ascending)
            .Get(ct);
        return response.Models;
    }

    public async Task<Item> AddAsync(string title, string value, string type, string? categoryId,
        string? description = null, CancellationToken ct = default)
    {
        var userId = _client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión activa.");
        var item = new Item
        {
            UserId = userId,
            Title = title.Trim(),
            Value = value.Trim(),
            Type = type,
            CategoryId = categoryId,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Platform = "windows",
        };
        var response = await _client.From<Item>().Insert(item, cancellationToken: ct);
        if (response.Models.Count == 0)
            throw new InvalidOperationException("El servidor no devolvió el item creado (revisa RLS).");
        return response.Models.First();
    }

    public async Task<Item> UpdateAsync(string id, string title, string value, string type, string? categoryId,
        string? description = null, CancellationToken ct = default)
    {
        var response = await _client
            .From<Item>()
            .Filter("id", Operator.Equals, id)
            .Set(x => x.Title!, title.Trim())
            .Set(x => x.Value!, value.Trim())
            .Set(x => x.Type!, type)
            .Set(x => x.CategoryId!, categoryId)
            .Set(x => x.Description!, string.IsNullOrWhiteSpace(description) ? null : description.Trim())
            .Update(cancellationToken: ct);
        if (response.Models.Count == 0)
            throw new InvalidOperationException("Este elemento ya no existe (puede haberse borrado en otro dispositivo).");
        return response.Models.First();
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await _client
            .From<Item>()
            .Filter("id", Operator.Equals, id)
            .Delete(cancellationToken: ct);
    }

    /// <summary>
    /// Re-inserts a hard-deleted item keeping its original id (undo support). Uses a raw
    /// REST insert because the Postgrest model never serializes the primary key on insert.
    /// Tags and history of the original row are not recovered (cascaded on delete).
    /// </summary>
    public async Task RestoreAsync(Item item, CancellationToken ct = default)
    {
        var supaUrl = SupabaseClientHolder.SupabaseUrl;
        var supaKey = SupabaseClientHolder.AnonKey;
        var session = _client.Auth.CurrentSession?.AccessToken
            ?? throw new InvalidOperationException("Sin sesión activa.");

        var payload = new Dictionary<string, object?>
        {
            ["id"] = item.Id,
            ["user_id"] = item.UserId,
            ["category_id"] = item.CategoryId,
            ["title"] = item.Title,
            ["value"] = item.Value,
            ["type"] = item.Type,
            ["device_id"] = item.DeviceId,
            ["platform"] = item.Platform,
            ["description"] = item.Description,
            ["favicon_url"] = item.FaviconUrl,
            ["site_name"] = item.SiteName,
            ["usage_count"] = item.UsageCount,
            ["is_favorite"] = item.IsFavorite,
            ["link_status"] = item.LinkStatus,
            ["link_http_code"] = item.LinkHttpCode,
        };
        if (item.CreatedAt.Year > 2000)
            payload["created_at"] = DateTime.SpecifyKind(item.CreatedAt, DateTimeKind.Utc).ToString("o");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{supaUrl}/rest/v1/items")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.TryAddWithoutValidation("apikey", supaKey);
        req.Headers.Authorization = new("Bearer", session);
        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }

    public async Task ArchiveAsync(string id, bool archived, CancellationToken ct = default)
    {
        await _client
            .From<Item>()
            .Filter("id", Operator.Equals, id)
            .Set(x => x.ArchivedAt!, archived
                ? DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
                : (DateTime?)null)
            .Update(cancellationToken: ct);
    }

    public async Task<Item> ToggleFavoriteAsync(string id, bool isFavorite, CancellationToken ct = default)
    {
        var response = await _client
            .From<Item>()
            .Filter("id", Operator.Equals, id)
            .Set(x => x.IsFavorite, isFavorite)
            .Update(cancellationToken: ct);
        if (response.Models.Count == 0)
            throw new InvalidOperationException("Este elemento ya no existe (puede haberse borrado en otro dispositivo).");
        return response.Models.First();
    }

    public Task BumpUsageAsync(string itemId, CancellationToken ct = default) =>
        _client.Rpc("bump_usage", new Dictionary<string, object> { ["item_id"] = itemId });

    public async Task<IReadOnlyList<Category>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var response = await _client
            .From<Category>()
            .Order("name", Ordering.Ascending)
            .Get(ct);
        return response.Models;
    }

    public async Task<Category> AddCategoryAsync(string name, CancellationToken ct = default)
    {
        var userId = _client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión activa.");
        var cat = new Category { Name = name.Trim(), UserId = userId };
        var response = await _client.From<Category>().Insert(cat, cancellationToken: ct);
        if (response.Models.Count == 0)
            throw new InvalidOperationException("El servidor no devolvió la categoría creada (revisa RLS).");
        return response.Models.First();
    }

    public async Task RenameCategoryAsync(string id, string newName, CancellationToken ct = default)
    {
        await _client.From<Category>()
            .Filter("id", Operator.Equals, id)
            .Set(x => x.Name, newName.Trim())
            .Update(cancellationToken: ct);
    }

    public async Task DeleteCategoryAsync(string id, CancellationToken ct = default)
    {
        await _client.From<Category>()
            .Filter("id", Operator.Equals, id)
            .Delete(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<Tag>> ListTagsAsync(CancellationToken ct = default)
    {
        var response = await _client
            .From<Tag>()
            .Order("name", Ordering.Ascending)
            .Get(ct);
        return response.Models;
    }

    public async Task<Tag> AddTagAsync(string name, CancellationToken ct = default)
    {
        var userId = _client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión activa.");
        var tag = new Tag { Name = name.Trim(), UserId = userId };
        var response = await _client.From<Tag>().Insert(tag, cancellationToken: ct);
        if (response.Models.Count == 0)
            throw new InvalidOperationException("El servidor no devolvió la etiqueta creada (revisa RLS).");
        return response.Models.First();
    }

    public async Task RenameTagAsync(string id, string newName, CancellationToken ct = default)
    {
        await _client.From<Tag>()
            .Filter("id", Operator.Equals, id)
            .Set(x => x.Name, newName.Trim())
            .Update(cancellationToken: ct);
    }

    public async Task DeleteTagAsync(string id, CancellationToken ct = default)
    {
        await _client.From<Tag>()
            .Filter("id", Operator.Equals, id)
            .Delete(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<string>> GetItemTagIdsAsync(string itemId, CancellationToken ct = default)
    {
        var response = await _client
            .From<ItemTag>()
            .Filter("item_id", Operator.Equals, itemId)
            .Get(ct);
        return response.Models.Select(t => t.TagId).ToList();
    }

    public async Task<IReadOnlyList<ItemTag>> ListAllItemTagsAsync(CancellationToken ct = default)
    {
        var response = await _client.From<ItemTag>().Get(ct);
        return response.Models;
    }

    public async Task SetItemTagsAsync(string itemId, IReadOnlyCollection<string> tagIds, CancellationToken ct = default)
    {
        var previous = await GetItemTagIdsAsync(itemId, ct);

        await _client
            .From<ItemTag>()
            .Filter("item_id", Operator.Equals, itemId)
            .Delete(cancellationToken: ct);

        if (tagIds.Count == 0) return;

        try
        {
            var rows = tagIds.Select(tid => new ItemTag { ItemId = itemId, TagId = tid }).ToList();
            await _client.From<ItemTag>().Insert(rows, cancellationToken: ct);
        }
        catch
        {
            if (previous.Count > 0)
            {
                try
                {
                    var restore = previous.Select(tid => new ItemTag { ItemId = itemId, TagId = tid }).ToList();
                    await _client.From<ItemTag>().Insert(restore, cancellationToken: CancellationToken.None);
                }
                catch (Exception ex) { Diagnostics.Log("set-item-tags rollback failed", ex); }
            }
            throw;
        }
    }

    public async Task<IReadOnlyList<ItemHistoryEntry>> ListHistoryAsync(string itemId, CancellationToken ct = default)
    {
        var response = await _client
            .From<ItemHistoryEntry>()
            .Filter("item_id", Operator.Equals, itemId)
            .Order("changed_at", Ordering.Descending)
            .Limit(100)
            .Get(ct);
        return response.Models;
    }

    private static readonly char[] _shareAlphabet = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();

    private static string GenerateShareToken(int length)
    {
        int limit = 256 / _shareAlphabet.Length * _shareAlphabet.Length;
        var sb = new StringBuilder(length);
        while (sb.Length < length)
        {
            foreach (var b in RandomNumberGenerator.GetBytes(length - sb.Length))
                if (b < limit) sb.Append(_shareAlphabet[b % _shareAlphabet.Length]);
        }
        return sb.ToString();
    }

    public async Task<ShareLink> CreateShareLinkAsync(string itemId, int? expiresInDays, CancellationToken ct = default)
    {
        var userId = _client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión activa.");
        var token = GenerateShareToken(12);

        var link = new ShareLink
        {
            Token = token,
            UserId = userId,
            ItemId = itemId,
            ExpiresAt = expiresInDays.HasValue
                ? DateTime.SpecifyKind(DateTime.UtcNow.AddDays(expiresInDays.Value), DateTimeKind.Utc)
                : null,
        };
        var response = await _client.From<ShareLink>().Insert(link, cancellationToken: ct);
        if (response.Models.Count == 0)
            throw new InvalidOperationException("El servidor no devolvió el enlace creado (revisa RLS / migración 0005).");
        return response.Models.First();
    }

    public async Task<string> CreateShareCollectionAsync(string name, IReadOnlyList<string> itemIds, int? expiresInDays, CancellationToken ct = default)
    {
        if (itemIds.Count == 0) throw new InvalidOperationException("Selecciona al menos un enlace.");
        if (itemIds.Count > 200) throw new InvalidOperationException("Máximo 200 enlaces por colección.");
        var userId = _client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión activa.");
        var token = GenerateShareToken(12);
        var finalName = name.Trim();
        if (finalName.Length > 120) finalName = finalName[..120];
        if (finalName.Length == 0) finalName = "Colección de enlaces";

        var collection = new ShareCollection
        {
            Token = token,
            UserId = userId,
            Name = finalName,
            ExpiresAt = expiresInDays.HasValue
                ? DateTime.SpecifyKind(DateTime.UtcNow.AddDays(expiresInDays.Value), DateTimeKind.Utc)
                : null,
        };
        await _client.From<ShareCollection>().Insert(collection, cancellationToken: ct);

        try
        {
            var rows = itemIds.Select((id, i) => new ShareCollectionItem
            {
                CollectionToken = token,
                ItemId = id,
                Position = i,
            }).ToList();
            await _client.From<ShareCollectionItem>().Insert(rows, cancellationToken: ct);
        }
        catch
        {
            try { await _client.From<ShareCollection>().Filter("token", Operator.Equals, token).Delete(cancellationToken: ct); }
            catch (Exception ex) { Diagnostics.Log("share-collection rollback failed", ex); }
            throw;
        }
        return token;
    }

    public async Task<IReadOnlyList<ShareLink>> ListShareLinksAsync(CancellationToken ct = default)
    {
        var response = await _client
            .From<ShareLink>()
            .Order("created_at", Ordering.Descending)
            .Get(ct);
        return response.Models;
    }

    public async Task RevokeShareLinkAsync(string token, CancellationToken ct = default)
    {
        await _client
            .From<ShareLink>()
            .Filter("token", Operator.Equals, token)
            .Delete(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ShareCollection>> ListShareCollectionsAsync(CancellationToken ct = default)
    {
        var response = await _client
            .From<ShareCollection>()
            .Get(ct);
        return response.Models;
    }

    public async Task RevokeShareCollectionAsync(string token, CancellationToken ct = default)
    {
        await _client
            .From<ShareCollectionItem>()
            .Filter("collection_token", Operator.Equals, token)
            .Delete(cancellationToken: ct);
        await _client
            .From<ShareCollection>()
            .Filter("token", Operator.Equals, token)
            .Delete(cancellationToken: ct);
    }

    public async Task RegisterDeviceAsync(string deviceId, string name, CancellationToken ct = default)
    {
        var userId = _client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión activa.");
        var device = new Device
        {
            Id = deviceId,
            UserId = userId,
            Name = name,
            Platform = "windows",
            LastSeenAt = DateTime.UtcNow,
        };
        await _client.From<Device>().Upsert(device, cancellationToken: ct);
    }

    public async Task EnrichMetadataAsync(Item item, bool skipDescription = false, CancellationToken ct = default)
    {
        if (item.Type != ItemTypes.Url) return;
        var token = _client.Auth.CurrentSession?.AccessToken;
        if (string.IsNullOrEmpty(token)) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://lm.aelbak.dev/api/metadata")
            {
                Content = JsonContent.Create(new { url = item.Value }),
            };
            req.Headers.Authorization = new("Bearer", token);
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            string? Get(string k) =>
                root.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var description = skipDescription ? null : Get("description");
            var favicon = Get("favicon_url");
            var siteName = Get("site_name");

            if (favicon is not null && !(favicon.StartsWith("http://") || favicon.StartsWith("https://")))
                favicon = null;
            if (description is null && favicon is null && siteName is null) return;

            var q = _client.From<Item>().Filter("id", Operator.Equals, item.Id);
            if (description is not null) q = q.Set(x => x.Description!, description);
            if (favicon is not null) q = q.Set(x => x.FaviconUrl!, favicon);
            if (siteName is not null) q = q.Set(x => x.SiteName!, siteName);
            await q.Update(cancellationToken: ct);
        }
        catch {  }
    }

    public async Task<Profile?> GetProfileAsync(CancellationToken ct = default)
    {
        var userId = SupabaseClientHolder.Client.Auth.CurrentUser?.Id;
        if (userId is null) return null;
        var response = await _client
            .From<Profile>()
            .Filter("id", Operator.Equals, userId)
            .Get(ct);
        return response.Models.FirstOrDefault();
    }

    public async Task UpdateDisplayNameAsync(string displayName, CancellationToken ct = default)
    {
        var userId = SupabaseClientHolder.Client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión");
        await _client
            .From<Profile>()
            .Filter("id", Operator.Equals, userId)
            .Set(x => x.DisplayName!, displayName.Trim())
            .Update(cancellationToken: ct);
    }

    public async Task<UserSettings?> GetSettingsAsync(CancellationToken ct = default)
    {
        var userId = SupabaseClientHolder.Client.Auth.CurrentUser?.Id;
        if (userId is null) return null;
        var response = await _client
            .From<UserSettings>()
            .Filter("user_id", Operator.Equals, userId)
            .Get(ct);
        return response.Models.FirstOrDefault();
    }

    /// <summary>
    /// Read-merge-upsert of the per-user settings JSON. Concurrency note: the fresh read
    /// happens immediately before the upsert to keep the race window minimal, but two
    /// clients patching at once are still last-writer-wins for the whole document; a truly
    /// atomic per-key merge would need a server-side RPC (jsonb merge) in the database.
    /// </summary>
    public async Task SaveSettingsPatchAsync(IDictionary<string, object?> patch, CancellationToken ct = default)
    {
        var current = await GetSettingsAsync(ct);
        var merged = new Dictionary<string, JsonElement>();
        if (current?.Data.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in current.Data.EnumerateObject())
                merged[prop.Name] = prop.Value.Clone();
        }
        foreach (var (k, v) in patch)
        {
            var serialized = JsonSerializer.SerializeToElement(v);
            merged[k] = serialized;
        }

        var json = JsonSerializer.Serialize(merged);
        var userId = SupabaseClientHolder.Client.Auth.CurrentUser?.Id
            ?? throw new InvalidOperationException("Sin sesión");

        var supaUrl = SupabaseClientHolder.SupabaseUrl;
        var supaKey = SupabaseClientHolder.AnonKey;
        var session = _client.Auth.CurrentSession?.AccessToken
            ?? throw new InvalidOperationException("Sin token");

        var payload = new { user_id = userId, data = JsonDocument.Parse(json).RootElement };
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{supaUrl}/rest/v1/settings?on_conflict=user_id")
        {
            Content = JsonContent.Create(payload),
        };
        req.Headers.TryAddWithoutValidation("apikey", supaKey);
        req.Headers.Authorization = new("Bearer", session);
        req.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=representation");

        var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
    }
}
