namespace ITBees.Printers.Controllers.Models;

/// <summary>Sent by the connect page once the logged-in user agrees to connect the print agent.</summary>
public class PrintAgentRegistrationIm
{
    /// <summary>Computer name the agent put in the connect page address - informational only.</summary>
    public string? MachineName { get; set; }

    /// <summary>
    /// Base address the frontend reaches this API at (e.g. "https://adminapi.example.com").
    /// Whatever sits between the browser and the application - proxies, TLS terminators - this is
    /// the one address known to work from the user's computer, scheme included, so it is what the
    /// agent is sent to when <see cref="PrintersSettings.PublicAgentUrl"/> is not configured.
    /// </summary>
    public string? ApiUrl { get; set; }
}

public class PrintAgentRegistrationVm
{
    public PrintAgentRegistrationVm()
    {
    }

    /// <summary>One-time code the connect page passes on to the agent's loopback callback.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Address the agent exchanges the code at and then stays connected to.</summary>
    public string HubUrl { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}
