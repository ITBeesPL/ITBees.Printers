using ITBees.Printers.Protocol;

namespace ITBees.Printers.AgentHub;

/// <summary>
/// The application's side of the agent hub: tells which agents are connected right now and
/// talks to them. A singleton of the host's container - safe to inject anywhere.
/// </summary>
public interface IPrintAgentGateway
{
    bool IsOnline(Guid agentGuid);

    /// <summary>
    /// Hands a job to the agent and waits until it confirms taking it over.
    /// </summary>
    /// <exception cref="PrintAgentOfflineException">
    /// The agent is not connected, or did not answer within <see cref="PrintersSettings.AgentAckTimeout"/>.
    /// </exception>
    Task<AgentPrintJobAck> SendPrintJob(Guid agentGuid, AgentPrintJob job, CancellationToken cancellationToken = default);

    /// <summary>Asks the agent to report its printers again. False when it is not connected.</summary>
    Task<bool> RequestPrintersRefresh(Guid agentGuid);

    /// <summary>Tells the agent that it was removed from the account and drops its connections.</summary>
    Task Revoke(Guid agentGuid);
}

public class PrintAgentOfflineException : Exception
{
    public PrintAgentOfflineException(string message) : base(message)
    {
    }
}
