namespace ITBees.Printers.DbModels;

/// <summary>A printer installed on the machine of a <see cref="PrintAgent"/>, as last reported by it.</summary>
public class PrintAgentPrinter
{
    public const int MaxNameLength = 260;

    public Guid Guid { get; set; }

    public PrintAgent PrintAgent { get; set; } = null!;
    public Guid PrintAgentGuid { get; set; }

    /// <summary>System name of the printer - unique within one agent.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The default printer of the Windows user the agent runs as.</summary>
    public bool IsDefault { get; set; }
    public string? DriverName { get; set; }
    public string? PortName { get; set; }
    public string? Location { get; set; }
    public bool IsNetwork { get; set; }
    public bool IsOffline { get; set; }
    public string? Status { get; set; }

    /// <summary>
    /// False once the printer is missing from the agent's latest report. The row stays, so a
    /// print setting pointing at it survives the printer being reinstalled under the same name.
    /// </summary>
    public bool IsAvailable { get; set; }

    public DateTime FirstReported { get; set; }
    public DateTime LastReported { get; set; }
}
