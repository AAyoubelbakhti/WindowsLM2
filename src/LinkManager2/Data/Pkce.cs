using System.Security.Cryptography;
using System.Text;

namespace LinkManager2.Data;

/// <summary>
/// Generates PKCE verifier/challenge pairs for the web-assisted OAuth flow.
/// </summary>
internal static class Pkce
{
    /// <summary>
    /// Creates a fresh PKCE pair. The verifier is 64 random bytes encoded as
    /// base64url without padding; the challenge is the base64url-encoded
    /// SHA-256 of the ASCII verifier (method S256).
    /// </summary>
    public static (string Verifier, string Challenge) Create()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(64));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    /// <summary>
    /// Encodes bytes as base64url: standard base64 with '+' -> '-', '/' -> '_'
    /// and no '=' padding.
    /// </summary>
    private static string Base64Url(byte[] bytes) =>
        System.Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
