using ITBees.Printers.DbModels;

namespace ITBees.Printers.Controllers.Models;

/// <summary>
/// Print setting of the logged-in user for one document type - what the user chose
/// (<see cref="Mode"/>, <see cref="PrinterGuid"/>, <see cref="Copies"/>) and what it resolves to
/// after following the default (the Effective* properties). A frontend "print" button only
/// needs the effective part.
/// </summary>
public class PrintSettingVm
{
    public PrintSettingVm()
    {
    }

    /// <summary>A document type key, or "*" for the user's default setting.</summary>
    public string DocumentType { get; set; } = PrintDocumentType.DefaultKey;
    public string DocumentTypeName { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary><see cref="PrintMode.UseDefault"/> for a document type without a setting of its own.</summary>
    public PrintMode Mode { get; set; }
    public Guid? PrinterGuid { get; set; }
    public int Copies { get; set; } = 1;

    public PrintMode EffectiveMode { get; set; }
    public Guid? EffectivePrinterGuid { get; set; }
    public string? EffectivePrinterName { get; set; }
    public string? EffectiveAgentMachineName { get; set; }
    public int EffectiveCopies { get; set; } = 1;

    /// <summary>
    /// Instant printing would work right now: the effective printer still exists and its agent
    /// is connected. When false the frontend falls back to the PDF.
    /// </summary>
    public bool IsPrinterReady { get; set; }
}

public class PrintSettingUm
{
    /// <summary>A document type key, or "*" for the default setting.</summary>
    public string? DocumentType { get; set; }
    public PrintMode Mode { get; set; }

    /// <summary>Required for <see cref="PrintMode.InstantPrint"/>; must be a printer of the user's own agent.</summary>
    public Guid? PrinterGuid { get; set; }
    public int? Copies { get; set; }
}
