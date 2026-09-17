namespace ITBees.Printers.Controllers.Models;

/// <summary>Sent by the connect page once the logged-in user agrees to connect the print agent.</summary>
public class PrintAgentRegistrationIm
{
    /// <summary>Computer name the agent put in the connect page address - informational only.</summary>
    public string? MachineName { get; set; }
}

public class PrintAgentRegistrationVm
{
    public PrintAgentRegistrationVm()
    {
    }

    /// <summary>One-time code the connect page passes on to the agent's loopback callback.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Address of the agent listener - the agent exchanges the code and connects there.</summary>
    public string HubUrl { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;
    public int ExpiresInSeconds { get; set; }
}
