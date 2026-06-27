using System.Text.Json;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace LinkManager2.Data;

[Table("items")]
public class Item : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("category_id")]
    public string? CategoryId { get; set; }

    [Column("title")]
    public string Title { get; set; } = string.Empty;

    [Column("value")]
    public string Value { get; set; } = string.Empty;

    [Column("type")]
    public string Type { get; set; } = "url";

    [Column("device_id")]
    public string? DeviceId { get; set; }

    [Column("platform")]
    public string? Platform { get; set; }

    [Column("description")]
    public string? Description { get; set; }

    [Column("favicon_url")]
    public string? FaviconUrl { get; set; }

    [Column("site_name")]
    public string? SiteName { get; set; }

    [Column("usage_count")]
    public int UsageCount { get; set; }

    [Column("is_favorite")]
    public bool IsFavorite { get; set; }

    [Column("archived_at")]
    public DateTime? ArchivedAt { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }

    [Column("updated_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime UpdatedAt { get; set; }
}

[Table("categories")]
public class Category : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("tags")]
public class Tag : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("item_tags")]
public class ItemTag : BaseModel
{
    [Column("item_id")]
    public string ItemId { get; set; } = string.Empty;

    [Column("tag_id")]
    public string TagId { get; set; } = string.Empty;
}

[Table("item_history")]
public class ItemHistoryEntry : BaseModel
{
    [PrimaryKey("id", false)]
    public long Id { get; set; }

    [Column("item_id")]
    public string? ItemId { get; set; }

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("change_type")]
    public string ChangeType { get; set; } = string.Empty;

    [Column("changed_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime ChangedAt { get; set; }
}

[Table("share_links")]
public class ShareLink : BaseModel
{

    [PrimaryKey("token", true)]
    public string Token { get; set; } = string.Empty;

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("item_id")]
    public string ItemId { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }

    [Column("view_count")]
    public int ViewCount { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("share_collections")]
public class ShareCollection : BaseModel
{
    [PrimaryKey("token", true)]
    public string Token { get; set; } = string.Empty;

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("expires_at")]
    public DateTime? ExpiresAt { get; set; }
}

[Table("share_collection_items")]
public class ShareCollectionItem : BaseModel
{
    [Column("collection_token")]
    public string CollectionToken { get; set; } = string.Empty;

    [Column("item_id")]
    public string ItemId { get; set; } = string.Empty;

    [Column("position")]
    public int Position { get; set; }
}

[Table("profiles")]
public class Profile : BaseModel
{
    [PrimaryKey("id", false)]
    public string Id { get; set; } = string.Empty;

    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Column("email")]
    public string? Email { get; set; }
}

[Table("devices")]
public class Device : BaseModel
{

    [PrimaryKey("id", true)]
    public string Id { get; set; } = string.Empty;

    [Column("user_id")]
    public string UserId { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("platform")]
    public string Platform { get; set; } = "windows";

    [Column("last_seen_at")]
    public DateTime? LastSeenAt { get; set; }

    [Column("created_at", ignoreOnInsert: true, ignoreOnUpdate: true)]
    public DateTime CreatedAt { get; set; }
}

[Table("settings")]
public class UserSettings : BaseModel
{
    [PrimaryKey("user_id", false)]
    public string UserId { get; set; } = string.Empty;

    [Column("data")]
    public JsonElement Data { get; set; }

    public bool HistoryEnabled =>
        !(Data.ValueKind == JsonValueKind.Object
          && Data.TryGetProperty("history_enabled", out var v)
          && v.ValueKind == JsonValueKind.False);

    public bool ShareCategories =>
        Data.ValueKind == JsonValueKind.Object
        && Data.TryGetProperty("share_categories", out var v)
        && v.ValueKind == JsonValueKind.True;

    public bool ShareTags =>
        Data.ValueKind == JsonValueKind.Object
        && Data.TryGetProperty("share_tags", out var v)
        && v.ValueKind == JsonValueKind.True;

    public bool IsActionVisible(string key, bool defaultValue = true)
    {
        if (Data.ValueKind != JsonValueKind.Object) return defaultValue;
        if (!Data.TryGetProperty("actions_visible", out var actions)) return defaultValue;
        if (actions.ValueKind != JsonValueKind.Object) return defaultValue;
        if (!actions.TryGetProperty(key, out var flag)) return defaultValue;
        return flag.ValueKind != JsonValueKind.False;
    }
}
