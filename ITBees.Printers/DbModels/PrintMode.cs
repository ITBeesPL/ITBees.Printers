namespace ITBees.Printers.DbModels;

public enum PrintMode
{
    /// <summary>The "print" button hands the PDF to the browser (new tab / download).</summary>
    DownloadPdf = 0,

    /// <summary>The "print" button sends the document straight to the chosen printer through the print agent.</summary>
    InstantPrint = 1,

    /// <summary>
    /// Only for a specific document type: follow the user's default setting. Never stored -
    /// choosing it removes the document type's own row.
    /// </summary>
    UseDefault = 2
}
