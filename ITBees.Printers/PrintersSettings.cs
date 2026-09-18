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
    /// Also serves the agent endpoints ("/print-agent/hub", "/print-agent/register",
    /// "/print-agent/info") on the host application's own port, in front of its pipeline
    /// (an IStartupFilter - nothing to add to Program.cs). Agents can then connect through the
    /// very address the API is already published at: same reverse proxy, same TLS certificate,
    /// no extra port to open in a firewall. The dedicated <see cref="AgentPort"/> keeps working
    /// next to it. On by default.
    /// </summary>
    public bool ExposeOnApplicationPort { get; set; } = true;

    /// <summary>
    /// Address the agents are told to connect to, e.g. "https://adminapi.example.com" or - for
    /// the dedicated port behind a TLS terminator - "https://adminapi.example.com:7443".
    /// A missing scheme is read as https. When empty, the address is worked out per pairing:
    /// with <see cref="ExposeOnApplicationPort"/> it is the address the frontend itself reached
    /// the API at (reported by the connect page, else taken from the request); otherwise the
    /// request's host with <see cref="AgentPort"/>. Leave it empty unless that guess is wrong.
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
