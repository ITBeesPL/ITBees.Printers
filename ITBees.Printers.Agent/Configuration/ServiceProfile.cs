using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ITBees.Printers.Agent.Configuration;

/// <summary>
/// One web application (service) this agent is connected to. Every service has its own agent
/// listener address and its own token - the agent can serve any number of them at once.
/// </summary>
public class ServiceProfile
{
    /// <summary>Address the agent was started with - also where the login page lives.</summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>Agent listener of the service (dedicated port), as announced during the login.</summary>
    public string HubUrl { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;
    public Guid AgentGuid { get; set; }

    /// <summary>The agent token, encrypted with DPAPI for the current Windows user.</summary>
    public string ProtectedToken { get; set; } = string.Empty;

    public DateTime ConnectedUtc { get; set; }

    [JsonIgnore]
    public bool HasToken => !string.IsNullOrEmpty(ProtectedToken);

    public void SetToken(string token)
    {
        ProtectedToken = Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
    }

    public string? GetToken()
    {
        if (!HasToken)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Copied from another user / machine - useless here; the user has to log in again.
            return null;
        }
    }

    public void ClearToken()
    {
        ProtectedToken = string.Empty;
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(ServiceName) ? SiteUrl : ServiceName;
}
