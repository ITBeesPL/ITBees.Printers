using ITBees.Printers.Protocol;

namespace ITBees.Printers.Services.Agents;

/// <summary>
/// Everything the agent listener needs from the application. It runs outside of any user's
/// HTTP request, so - unlike the services behind the REST controllers - it never asks for
/// the current user: the agent's identity comes from its token.
/// </summary>
public interface IPrintAgentSessionService
{
    /// <summary>Exchanges a one-time registration code for an agent token.</summary>
    /// <exception cref="PrintAgentRegistrationException">The code is unknown, used or expired.</exception>
    AgentRegistrationResponse Register(AgentRegistrationRequest request);

    /// <summary>The agent the token belongs to, or null for an invalid / revoked token.</summary>
    PrintAgentIdentity? Authenticate(string? token);

    void AgentConnected(Guid agentGuid);
    void AgentDisconnected(Guid agentGuid);
    void SavePrinters(Guid agentGuid, AgentPrintersReport report);
    void SaveJobResult(Guid agentGuid, AgentPrintJobResult result);
}

public record PrintAgentIdentity(Guid AgentGuid, Guid UserAccountGuid, string MachineName);

public class PrintAgentRegistrationException : Exception
{
    public PrintAgentRegistrationException(string message) : base(message)
    {
    }
}
