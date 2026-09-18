namespace ITBees.Printers.Protocol;

// NOTE: every file in this folder is compiled into BOTH the server library and the Windows
// print agent (linked into ITBees.Printers.Agent.csproj). Keep them free of any dependency
// beyond the base class library.

/// <summary>
/// Names and paths the print agent and the server agree on. Everything listed under "agent
/// listener" is served on the dedicated agent port, never by the host application's own API.
/// </summary>
public static class PrintAgentProtocol
{
    public const int Version = 1;

    // --- Agent listener (dedicated port) ---

    /// <summary>SignalR hub the agent stays connected to. Requires the agent token.</summary>
    public const string HubPath = "/print-agent/hub";

    /// <summary>Anonymous POST: exchanges a one-time registration code for the agent token.</summary>
    public const string RegisterPath = "/print-agent/register";

    /// <summary>Anonymous GET: service name and protocol version - a cheap reachability check.</summary>
    public const string InfoPath = "/print-agent/info";

    // --- Browser login flow ---

    /// <summary>
    /// Page of the web application that asks the logged-in user to connect the agent. Appended
    /// to the site address the agent was started with (its sub-path included), unless that
    /// address already names a connect page itself.
    /// </summary>
    public const string DefaultConnectPagePath = "/print-agent/connect";

    /// <summary>Query parameters the agent adds to the connect page address.</summary>
    public const string PortParameter = "port";
    public const string StateParameter = "state";
    public const string MachineParameter = "machine";

    /// <summary>Path on the agent's loopback listener the connect page sends the browser back to.</summary>
    public const string CallbackPath = "/callback";

    /// <summary>Query parameters of that callback.</summary>
    public const string CodeParameter = "code";
    public const string HubUrlParameter = "hub";
    public const string ServiceNameParameter = "service";
    public const string ErrorParameter = "error";

    // --- Hub methods: server -> agent ---

    /// <summary>Prints a document. The agent answers with <see cref="AgentPrintJobAck"/>.</summary>
    public const string PrintMethod = "Print";

    /// <summary>Asks the agent to scan the system printers again and report them.</summary>
    public const string RefreshPrintersMethod = "RefreshPrinters";

    /// <summary>The user removed this agent from the account - the token is no longer valid.</summary>
    public const string RevokedMethod = "Revoked";

    // --- Hub methods: agent -> server ---

    public const string ReportPrintersMethod = "ReportPrinters";
    public const string ReportPrintJobResultMethod = "ReportPrintJobResult";
}
