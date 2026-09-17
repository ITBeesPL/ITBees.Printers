using ITBees.Printers.Controllers.Models;

namespace ITBees.Printers.Services.Jobs;

public interface IPrintJobService
{
    /// <summary>
    /// Prints a document uploaded by the frontend on the printer the logged-in user chose for
    /// its document type. Fails with 409 when that user does not use instant printing for it.
    /// </summary>
    Task<PrintJobVm> Create(PrintJobIm printJobIm);

    /// <summary>
    /// The same for a document produced on the server - for hosts that prefer a dedicated
    /// "print this" endpoint over sending the PDF through the browser.
    /// </summary>
    Task<PrintJobVm> Print(string? documentType, string documentName, byte[] pdfContent, int? copies = null);

    /// <summary>Prints a small test page on one of the logged-in user's printers.</summary>
    Task<PrintJobVm> PrintTestPage(PrintTestPageIm printTestPageIm);

    /// <summary>A job of the logged-in user - polled by the frontend until it is finished.</summary>
    PrintJobVm Get(Guid guid);
}
