using ITBees.Printers.DbModels;

namespace ITBees.Printers.Controllers.Models;

/// <summary>A print agent of the logged-in user together with the printers it reported.</summary>
public class PrintAgentVm
{
    public PrintAgentVm()
    {
    }

    public PrintAgentVm(PrintAgent agent, bool isOnline, IEnumerable<PrintAgentPrinter> printers)
    {
        Guid = agent.Guid;
        MachineName = agent.MachineName;
        AgentVersion = agent.AgentVersion;
        OsVersion = agent.OsVersion;
        IsOnline = isOnline;
        Created = agent.Created;
        LastConnected = agent.LastConnected;
        LastSeen = agent.LastSeen;
        Printers = printers
            .OrderByDescending(x => x.IsAvailable)
            .ThenBy(x => x.Name)
            .Select(x => new PrintAgentPrinterVm(x, agent, isOnline))
            .ToList();
    }

    public Guid Guid { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string? AgentVersion { get; set; }
    public string? OsVersion { get; set; }

    /// <summary>The agent is connected to the agent hub right now.</summary>
    public bool IsOnline { get; set; }

    public DateTime Created { get; set; }
    public DateTime? LastConnected { get; set; }
    public DateTime? LastSeen { get; set; }
    public List<PrintAgentPrinterVm> Printers { get; set; } = new();
}

public class PrintAgentPrinterVm
{
    public PrintAgentPrinterVm()
    {
    }

    public PrintAgentPrinterVm(PrintAgentPrinter printer, PrintAgent agent, bool agentIsOnline)
    {
        Guid = printer.Guid;
        PrintAgentGuid = agent.Guid;
        AgentMachineName = agent.MachineName;
        Name = printer.Name;
        IsDefault = printer.IsDefault;
        DriverName = printer.DriverName;
        PortName = printer.PortName;
        Location = printer.Location;
        IsNetwork = printer.IsNetwork;
        IsOffline = printer.IsOffline;
        Status = printer.Status;
        IsAvailable = printer.IsAvailable;
        IsReady = agentIsOnline && printer.IsAvailable;
        LastReported = printer.LastReported;
    }

    public Guid Guid { get; set; }
    public Guid PrintAgentGuid { get; set; }
    public string AgentMachineName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string? DriverName { get; set; }
    public string? PortName { get; set; }
    public string? Location { get; set; }
    public bool IsNetwork { get; set; }

    /// <summary>Windows itself reports the printer as offline - a job would wait in its queue.</summary>
    public bool IsOffline { get; set; }
    public string? Status { get; set; }

    /// <summary>False when the printer vanished from the agent's machine.</summary>
    public bool IsAvailable { get; set; }

    /// <summary>A job sent now would reach the printer: the agent is online and still has it.</summary>
    public bool IsReady { get; set; }

    public DateTime LastReported { get; set; }
}
