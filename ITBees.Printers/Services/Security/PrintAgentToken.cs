using System.Security.Cryptography;
using System.Text;

namespace ITBees.Printers.Services.Security;

/// <summary>
/// Agent token: "{agent guid, 32 hex digits}.{random secret}". The guid finds the agent row,
/// the secret is compared against the stored SHA-256 - so a leaked database does not leak
/// working tokens. The secret is 256 bits of randomness, which makes a plain (unsalted, fast)
/// hash sufficient.
/// </summary>
public static class PrintAgentToken
{
    private const int SecretBytes = 32;

    public static string Create(Guid agentGuid, out string secretHash)
    {
        var secret = ToBase64Url(RandomNumberGenerator.GetBytes(SecretBytes));
        secretHash = Hash(secret);
        return $"{agentGuid:N}.{secret}";
    }

    public static bool TryParse(string? token, out Guid agentGuid, out string secret)
    {
        agentGuid = Guid.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var separator = token.IndexOf('.');
        if (separator <= 0 || separator == token.Length - 1)
        {
            return false;
        }

        if (!Guid.TryParseExact(token[..separator], "N", out agentGuid))
        {
            return false;
        }

        secret = token[(separator + 1)..];
        return true;
    }

    public static string Hash(string secret)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    public static bool Matches(string secret, string? expectedHash)
    {
        if (string.IsNullOrEmpty(expectedHash))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(secret)),
            Encoding.ASCII.GetBytes(expectedHash.ToUpperInvariant()));
    }

    /// <summary>Random one-time value safe to put in a URL (registration codes).</summary>
    public static string CreateOneTimeCode()
    {
        return ToBase64Url(RandomNumberGenerator.GetBytes(SecretBytes));
    }

    private static string ToBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
