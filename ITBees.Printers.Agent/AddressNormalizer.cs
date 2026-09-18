using System.Net;

namespace ITBees.Printers.Agent;

/// <summary>
/// People type "admin.example.com", not "https://admin.example.com/" - and a service may
/// announce its agent address just as loosely. Every address the agent is given goes through
/// here first.
/// </summary>
public static class AddressNormalizer
{
    /// <summary>
    /// Completes a missing scheme - https, or http for this computer (localhost / loopback),
    /// where nobody runs TLS - and drops the trailing slash. False when what is left is still
    /// not an http(s) address.
    /// </summary>
    public static bool TryNormalize(string? input, out string url)
    {
        url = string.Empty;
        var value = (input ?? string.Empty).Trim().Trim('"', '\'').Trim();
        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = (IsThisComputer(HostOf(value)) ? "http://" : "https://") + value.TrimStart('/');
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        url = uri.AbsoluteUri.TrimEnd('/');
        return true;
    }

    /// <summary>"admin.example.com:8443/panel" -> "admin.example.com".</summary>
    private static string HostOf(string addressWithoutScheme)
    {
        var authority = addressWithoutScheme.TrimStart('/').Split('/', '?', '#')[0];
        if (authority.StartsWith('['))
        {
            // IPv6 literal: "[::1]:5190"
            var end = authority.IndexOf(']');
            return end > 0 ? authority[1..end] : authority;
        }

        var colon = authority.LastIndexOf(':');
        return colon > 0 ? authority[..colon] : authority;
    }

    private static bool IsThisComputer(string host)
    {
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
    }
}
