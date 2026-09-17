using ITBees.Models.Users;

namespace ITBees.Printers.DbModels;

/// <summary>
/// An installation of the Windows print agent paired with a user account. The agent logs in
/// to the agent hub with its own token - never with the user's credentials.
/// </summary>
public class PrintAgent
{
    public Guid Guid { get; set; }

    /// <summary>Owner of the agent. Only this user sees its printers and can print on them.</summary>
    public UserAccount UserAccount { get; set; } = null!;
    public Guid UserAccountGuid { get; set; }

    public string MachineName { get; set; } = string.Empty;
    public string? AgentVersion { get; set; }
    public string? OsVersion { get; set; }

    /// <summary>SHA-256 (hex) of the secret part of the agent token. The token itself is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTime Created { get; set; }
    public DateTime? LastConnected { get; set; }

    /// <summary>Last sign of life: a connection, a printers report, a finished job or a disconnect.</summary>
    public DateTime? LastSeen { get; set; }

    public List<PrintAgentPrinter> Printers { get; set; } = new();
}
