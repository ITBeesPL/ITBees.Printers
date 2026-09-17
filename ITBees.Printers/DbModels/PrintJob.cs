using ITBees.Models.Users;

namespace ITBees.Printers.DbModels;

/// <summary>
/// One document sent for instant printing. Only the metadata is stored - the document itself
/// goes straight to the agent and is never persisted on the server.
/// </summary>
public class PrintJob
{
    public const int MaxDocumentNameLength = 260;
    public const int MaxStatusMessageLength = 1000;

    public Guid Guid { get; set; }

    public UserAccount UserAccount { get; set; } = null!;
    public Guid UserAccountGuid { get; set; }

    // Plain values, no foreign keys - the history outlives the agent and the printer.
    public Guid? PrintAgentGuid { get; set; }
    public Guid? PrinterGuid { get; set; }
    public string? PrinterName { get; set; }
    public string? AgentMachineName { get; set; }

    public string DocumentType { get; set; } = PrintDocumentType.DefaultKey;
    public string DocumentName { get; set; } = string.Empty;
    public int ContentLength { get; set; }
    public int Copies { get; set; } = 1;

    public PrintJobStatus Status { get; set; }
    public string? StatusMessage { get; set; }
    public int? PagesPrinted { get; set; }

    public DateTime Created { get; set; }
    public DateTime? Completed { get; set; }
}

public enum PrintJobStatus
{
    Created = 0,

    /// <summary>The agent confirmed that it queued the job; the result has not arrived yet.</summary>
    SentToAgent = 1,

    /// <summary>The agent handed every page to the Windows spooler.</summary>
    Printed = 2,

    /// <summary>The agent could not print the document - see <see cref="PrintJob.StatusMessage"/>.</summary>
    Failed = 3,

    /// <summary>The agent was not connected (or did not answer) - the frontend falls back to the PDF.</summary>
    AgentOffline = 4,

    /// <summary>The agent refused the job, e.g. the printer no longer exists on that machine.</summary>
    Rejected = 5
}
