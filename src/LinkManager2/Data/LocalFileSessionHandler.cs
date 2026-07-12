using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Supabase.Gotrue;
using Supabase.Gotrue.Interfaces;

namespace LinkManager2.Data;

internal sealed class LocalFileSessionHandler : IGotrueSessionPersistence<Session>
{
    private static string Dir
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LinkManager");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string EncryptedPath => Path.Combine(Dir, "session.dat");
    private static string LegacyPlainPath => Path.Combine(Dir, "session.json");

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LinkManager2.session.v1");

    public void SaveSession(Session session)
    {
        try
        {
            var json = JsonConvert.SerializeObject(session);
            var cipher = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(json), Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(EncryptedPath, cipher);
            DeleteLegacyPlain();
        }
        catch (Exception ex) { Diagnostics.Log("session save", ex); }
    }

    /// <summary>Best-effort deletion of the persisted session; a failed delete is ignored so sign-out
    /// and recovery paths never throw.</summary>
    public void DestroySession()
    {
        try { if (File.Exists(EncryptedPath)) File.Delete(EncryptedPath); } catch { }
        DeleteLegacyPlain();
    }

    public Session? LoadSession()
    {
        var session = LoadEncrypted();
        if (session != null) return session;
        return MigrateLegacyPlain();
    }

    private static Session? LoadEncrypted()
    {
        try
        {
            if (!File.Exists(EncryptedPath)) return null;
            var plain = ProtectedData.Unprotect(
                File.ReadAllBytes(EncryptedPath), Entropy, DataProtectionScope.CurrentUser);
            return JsonConvert.DeserializeObject<Session>(Encoding.UTF8.GetString(plain));
        }
        catch { return null; }
    }

    private Session? MigrateLegacyPlain()
    {
        try
        {
            if (!File.Exists(LegacyPlainPath)) return null;
            var session = JsonConvert.DeserializeObject<Session>(File.ReadAllText(LegacyPlainPath));
            if (session != null) SaveSession(session);
            else DeleteLegacyPlain();
            return session;
        }
        catch { return null; }
    }

    private static void DeleteLegacyPlain()
    {
        try { if (File.Exists(LegacyPlainPath)) File.Delete(LegacyPlainPath); } catch { }
    }
}
