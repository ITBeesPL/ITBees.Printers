using ITBees.Printers.DbModels;

namespace ITBees.Printers.Controllers.Models;

/// <summary>A document the frontend wants printed instantly, according to the user's print settings.</summary>
public class PrintJobIm
{
    /// <summary>A document type key of the host, e.g. "StockLabel". Empty - the user's default setting.</summary>
    public string? DocumentType { get; set; }

    /// <summary>Name shown in the Windows print queue and in the job history, e.g. the file name.</summary>
    public string? DocumentName { get; set; }

    /// <summary>The PDF document, base64-encoded.</summary>
    public string? ContentBase64 { get; set; }

    /// <summary>Overrides the number of copies from the user's setting.</summary>
    public int? Copies { get; set; }
}

public class PrintTestPageIm
{
    public Guid PrinterGuid { get; set; }
}

public class PrintJobVm
{
    public PrintJobVm()
    {
    }

    public PrintJobVm(PrintJob job)
    {
        Guid = job.Guid;
        DocumentType = job.DocumentType;
        DocumentName = job.DocumentName;
        PrinterName = job.PrinterName;
        AgentMachineName = job.AgentMachineName;
        Copies = job.Copies;
        Status = job.Status;
        StatusMessage = job.StatusMessage;
        PagesPrinted = job.PagesPrinted;
        Created = job.Created;
        Completed = job.Completed;
        IsFinished = job.Status != PrintJobStatus.Created && job.Status != PrintJobStatus.SentToAgent;
        ShouldFallBackToPdf = job.Status is PrintJobStatus.AgentOffline or PrintJobStatus.Rejected;
    }

    public Guid Guid { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string DocumentName { get; set; } = string.Empty;
    public string? PrinterName { get; set; }
    public string? AgentMachineName { get; set; }
    public int Copies { get; set; }
    public PrintJobStatus Status { get; set; }
    public string? StatusMessage { get; set; }
    public int? PagesPrinted { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Completed { get; set; }

    /// <summary>No further status change is expected - polling can stop.</summary>
    public bool IsFinished { get; set; }

    /// <summary>The document never reached the printer - the frontend should hand the PDF to the browser instead.</summary>
    public bool ShouldFallBackToPdf { get; set; }
}
