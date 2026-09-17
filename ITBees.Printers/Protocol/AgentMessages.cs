namespace ITBees.Printers.Protocol;

/// <summary>Body of the registration request - see <see cref="PrintAgentProtocol.RegisterPath"/>.</summary>
public class AgentRegistrationRequest
{
    /// <summary>One-time code the connect page handed to the agent through the loopback callback.</summary>
    public string Code { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? AgentVersion { get; set; }
    public string? OsVersion { get; set; }
}

public class AgentRegistrationResponse
{
    public Guid AgentGuid { get; set; }

    /// <summary>Long-lived agent token. Shown once - the server keeps only its hash.</summary>
    public string Token { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
}

/// <summary>Answer of <see cref="PrintAgentProtocol.InfoPath"/>.</summary>
public class AgentListenerInfo
{
    public string ServiceName { get; set; } = string.Empty;
    public int ProtocolVersion { get; set; }
}

/// <summary>Error body of the agent listener's HTTP endpoints.</summary>
public class AgentErrorResponse
{
    public string Message { get; set; } = string.Empty;
}

/// <summary>Full snapshot of the printers installed on the agent's machine.</summary>
public class AgentPrintersReport
{
    public string MachineName { get; set; } = string.Empty;
    public string? AgentVersion { get; set; }
    public string? OsVersion { get; set; }
    public List<AgentPrinterInfo> Printers { get; set; } = new();
}

public class AgentPrinterInfo
{
    /// <summary>System name of the printer - what a print job is addressed to.</summary>
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string? DriverName { get; set; }
    public string? PortName { get; set; }
    public string? Location { get; set; }
    public bool IsNetwork { get; set; }

    /// <summary>Windows reports the printer as offline (unplugged, "use printer offline").</summary>
    public bool IsOffline { get; set; }

    /// <summary>Printer status as reported by the system, e.g. "Idle", "Printing", "Error".</summary>
    public string? Status { get; set; }
}

public class AgentPrintJob
{
    public Guid JobGuid { get; set; }
    public string PrinterName { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;

    /// <summary>Only "application/pdf" for now.</summary>
    public string ContentType { get; set; } = "application/pdf";
    public string ContentBase64 { get; set; } = string.Empty;
    public int Copies { get; set; } = 1;
}

/// <summary>
/// Immediate answer to <see cref="PrintAgentProtocol.PrintMethod"/>: the job was queued on the
/// agent (or refused, e.g. the printer no longer exists). The outcome of the printing itself
/// follows as <see cref="AgentPrintJobResult"/>.
/// </summary>
public class AgentPrintJobAck
{
    public bool Accepted { get; set; }
    public string? Message { get; set; }
}

public class AgentPrintJobResult
{
    public Guid JobGuid { get; set; }
    public bool Success { get; set; }
    public string? Message { get; set; }
    public int PagesPrinted { get; set; }
}
