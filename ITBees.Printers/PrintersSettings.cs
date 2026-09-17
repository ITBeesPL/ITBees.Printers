namespace ITBees.Printers;

/// <summary>
/// Host-provided settings of the printing module - pass them to
/// <see cref="Setup.PrintersSetup.Register"/>. Every host (service) picks its own
/// <see cref="AgentPort"/>, so several services can run side by side on one server and one
/// print agent can stay connected to all of them, with a separate token for each.
/// </summary>
public class PrintersSettings
{
    public const int DefaultAgentPort = 7081;

    /// <summary>Name of the service as the print agent shows it, e.g. "Octopark Admin".</summary>
    public string ServiceName { get; set; } = "ITBees";

    /// <summary>
    /// TCP port of the dedicated agent listener (registration endpoint + the SignalR hub the
    /// agents stay connected to). The library opens it by itself - the host's own Kestrel
    /// configuration is not touched. 0 (or less) turns the listener off.
    /// </summary>
    public int AgentPort { get; set; } = DefaultAgentPort;

    /// <summary>
    /// Address of the agent listener as the agents reach it from outside, e.g.
    /// "https://adminapi.example.com:7443". Set it whenever the listener sits behind a reverse
    /// proxy / TLS terminator. When empty, the address is derived from the API request that
    /// starts the pairing: same scheme and host name, <see cref="AgentPort"/>.
    /// </summary>
    public string? PublicAgentUrl { get; set; }

    /// <summary>Role required to use printing. Null - every authenticated user.</summary>
    public string? RequiredRole { get; set; }

    /// <summary>
    /// Kinds of documents the host prints. Each one can get its own printer in the user's
    /// settings (labels go to the label printer, invoices to the office one); a document type
    /// without a setting of its own follows the user's default.
    /// </summary>
    public List<PrintDocumentType> DocumentTypes { get; set; } = new();

    /// <summary>Largest document accepted for instant printing.</summary>
    public int MaxDocumentSizeBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>How long the one-time code of the browser login flow stays valid.</summary>
    public TimeSpan RegistrationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long the server waits for the agent to confirm that it took a print job.</summary>
    public TimeSpan AgentAckTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Print job history older than this is pruned. 0 (or less) keeps it forever.</summary>
    public int PrintJobRetentionDays { get; set; } = 30;
}
