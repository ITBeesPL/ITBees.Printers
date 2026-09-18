using ITBees.Printers.Controllers.Models;

namespace ITBees.Printers.Services.Agents;

public interface IPrintAgentRegistrationService
{
    /// <summary>
    /// Issues a one-time registration code bound to the logged-in user, together with the
    /// address the agent is to connect to (see <see cref="PrintersSettings.PublicAgentUrl"/>).
    /// </summary>
    PrintAgentRegistrationVm Create(PrintAgentRegistrationIm? registrationIm, PrintAgentRequestOrigin requestOrigin);
}

/// <summary>
/// Where the pairing request came in, as the application sees it - the last resort for working
/// out the agents' address. Behind a TLS-terminating proxy the scheme seen here is usually
/// plain http, which is why the forwarded values (when the proxy sends them) take precedence.
/// </summary>
/// <param name="Scheme">"http" / "https" of the request.</param>
/// <param name="Host">Host header, port included when not the default one ("localhost:7080").</param>
/// <param name="PathBase">Path base the application runs under, or empty.</param>
/// <param name="ForwardedScheme">First value of X-Forwarded-Proto, if any.</param>
/// <param name="ForwardedHost">First value of X-Forwarded-Host, if any.</param>
public record PrintAgentRequestOrigin(
    string Scheme,
    string Host,
    string? PathBase = null,
    string? ForwardedScheme = null,
    string? ForwardedHost = null);
