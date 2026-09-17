using ITBees.Models.Users;

namespace ITBees.Printers.DbModels;

/// <summary>
/// How one user wants one kind of document printed. Settings are strictly personal - every
/// user keeps their own rows, one per document type (plus the "*" default).
/// </summary>
public class UserPrintSetting
{
    public Guid Guid { get; set; }

    public UserAccount UserAccount { get; set; } = null!;
    public Guid UserAccountGuid { get; set; }

    /// <summary>A <see cref="PrintDocumentType.Key"/>, or <see cref="PrintDocumentType.DefaultKey"/>.</summary>
    public string DocumentType { get; set; } = PrintDocumentType.DefaultKey;

    public PrintMode Mode { get; set; }

    /// <summary>
    /// Target <see cref="PrintAgentPrinter"/> of instant printing. Deliberately not a foreign
    /// key - removing an agent resets the settings that used its printers in code, which keeps
    /// the schema free of multiple cascade paths.
    /// </summary>
    public Guid? PrinterGuid { get; set; }

    public int Copies { get; set; } = 1;
    public DateTime Modified { get; set; }
}
