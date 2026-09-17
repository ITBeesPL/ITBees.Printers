using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.InteropServices.WindowsRuntime;
using ITBees.Printers.Agent.Logging;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace ITBees.Printers.Agent.Printing;

/// <summary>
/// Prints a PDF silently - no dialogs. Pages are rendered by Windows itself (Windows.Data.Pdf)
/// at the printer's resolution and handed to the spooler through System.Drawing.Printing, so
/// it works with every printer that has a Windows driver, label printers included.
///
/// Layout rules (what "actual size" means in a PDF viewer): a page that fits the paper is
/// printed 1:1 and centered - a 50 x 30 mm label on 50 x 30 mm stock lands exactly where it
/// should; a page bigger than the paper is shrunk to the printable area. A page is rotated
/// when its orientation differs from the paper's.
/// </summary>
public class PdfPrinter
{
    private const double DipsPerInch = 96.0;
    private const int FallbackDpi = 300;
    private const int MaxDpi = 600;

    // Keeps one rendered page well under ~100 MB (32 bpp): A4 drops from 600 to 300 dpi.
    private const long MaxPixelsPerPage = 25_000_000;

    // 1/100 inch - paper sizes reported by drivers are rounded, so "fits" needs some slack.
    private const float FitTolerance = 2f;

    private readonly AgentLog _log;

    public PdfPrinter(AgentLog log)
    {
        _log = log;
    }

    /// <summary>Returns the number of pages sent to the spooler (copies included).</summary>
    public int Print(byte[] pdfContent, string printerName, string documentName, int copies)
    {
        var document = LoadDocument(pdfContent);
        if (document.PageCount == 0)
        {
            throw new InvalidOperationException("Dokument PDF nie zawiera żadnej strony.");
        }

        using var printDocument = new PrintDocument();
        printDocument.PrinterSettings.PrinterName = printerName;
        if (!printDocument.PrinterSettings.IsValid)
        {
            throw new InvalidOperationException($"Drukarka „{printerName}” nie istnieje na tym komputerze.");
        }

        printDocument.DocumentName = documentName;
        printDocument.PrintController = new StandardPrintController(); // no "Printing page..." window
        printDocument.OriginAtMargins = false;
        RedirectFilePrinter(printDocument, documentName);

        // Not every driver honors the Copies setting - when it cannot, the pages are repeated.
        copies = Math.Clamp(copies, 1, 99);
        var driverCopies = copies <= printDocument.PrinterSettings.MaximumCopies;
        if (driverCopies)
        {
            printDocument.PrinterSettings.Copies = (short)copies;
        }

        var sequence = Enumerable.Repeat(Enumerable.Range(0, (int)document.PageCount), driverCopies ? 1 : copies)
            .SelectMany(x => x)
            .ToList();
        var position = 0;

        printDocument.QueryPageSettings += (_, e) =>
        {
            using var page = document.GetPage((uint)sequence[position]);
            var paper = e.PageSettings.PaperSize;
            var pageIsLandscape = page.Size.Width > page.Size.Height * 1.01;
            var pageIsPortrait = page.Size.Height > page.Size.Width * 1.01;
            var paperIsLandscape = paper.Width > paper.Height;

            // A square page never needs rotating; otherwise rotate when the shapes disagree.
            e.PageSettings.Landscape = (pageIsLandscape && !paperIsLandscape) || (pageIsPortrait && paperIsLandscape);
        };

        printDocument.PrintPage += (_, e) =>
        {
            using var page = document.GetPage((uint)sequence[position]);
            DrawPage(page, e);
            position++;
            e.HasMorePages = position < sequence.Count;
        };

        printDocument.Print();
        return sequence.Count * (driverCopies ? copies : 1);
    }

    private void DrawPage(PdfPage page, PrintPageEventArgs e)
    {
        var graphics = e.Graphics ?? throw new InvalidOperationException("The printer gave no drawing surface.");

        // Everything below is in 1/100 inch - the unit of System.Drawing.Printing.
        var pageWidth = (float)(page.Size.Width / DipsPerInch * 100);
        var pageHeight = (float)(page.Size.Height / DipsPerInch * 100);
        var paper = e.PageBounds; // physical sheet, already swapped for landscape
        var printable = GetPrintableArea(e.PageSettings);

        RectangleF target;
        if (pageWidth <= paper.Width + FitTolerance && pageHeight <= paper.Height + FitTolerance)
        {
            target = new RectangleF((paper.Width - pageWidth) / 2, (paper.Height - pageHeight) / 2, pageWidth,
                pageHeight);
        }
        else
        {
            var scale = Math.Min(printable.Width / pageWidth, printable.Height / pageHeight);
            var width = pageWidth * scale;
            var height = pageHeight * scale;
            target = new RectangleF(printable.X + (printable.Width - width) / 2,
                printable.Y + (printable.Height - height) / 2, width, height);
        }

        var (dpiX, dpiY) = ChooseRenderDpi(e.PageSettings, target);
        using var bitmap = Render(page, (int)Math.Round(target.Width / 100 * dpiX),
            (int)Math.Round(target.Height / 100 * dpiY));

        // With OriginAtMargins = false the drawing origin is the corner of the printable area,
        // not of the sheet - shift back by the hard margins to address the sheet itself.
        target.Offset(-printable.X, -printable.Y);
        graphics.PageUnit = GraphicsUnit.Display;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor; // already rendered at the device resolution
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(bitmap, target);
    }

    /// <summary>PageSettings.PrintableArea ignores Landscape - swap it by hand.</summary>
    private static RectangleF GetPrintableArea(PageSettings settings)
    {
        var area = settings.PrintableArea;
        if (!settings.Landscape)
        {
            return area;
        }

        // Rotating the sheet by 90 degrees moves the top margin to the left side.
        return new RectangleF(area.Y, area.X, area.Height, area.Width);
    }

    private static (int X, int Y) ChooseRenderDpi(PageSettings settings, RectangleF target)
    {
        var resolution = settings.PrinterResolution;
        // Draft/Low/Medium/High resolutions report negative numbers instead of dpi.
        var x = resolution.X > 0 ? resolution.X : FallbackDpi;
        var y = resolution.Y > 0 ? resolution.Y : FallbackDpi;

        // Step down by whole divisors, so one rendered pixel still maps to whole device dots.
        for (var divisor = 1; divisor <= 8; divisor++)
        {
            var dpiX = x / divisor;
            var dpiY = y / divisor;
            var pixels = (long)(target.Width / 100 * dpiX) * (long)(target.Height / 100 * dpiY);
            if (dpiX <= MaxDpi && dpiY <= MaxDpi && pixels <= MaxPixelsPerPage)
            {
                return (Math.Max(dpiX, 72), Math.Max(dpiY, 72));
            }
        }

        return (150, 150);
    }

    private static Bitmap Render(PdfPage page, int widthPixels, int heightPixels)
    {
        using var stream = new InMemoryRandomAccessStream();
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = (uint)Math.Max(1, widthPixels),
            DestinationHeight = (uint)Math.Max(1, heightPixels),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
        };

        // Printing runs on a worker thread of its own (see PrintQueue) - blocking here is fine.
        page.RenderToStreamAsync(stream, options).AsTask().GetAwaiter().GetResult();

        // Bitmap keeps reading from its stream, so give it a private managed copy.
        using var rendered = new Bitmap(stream.AsStreamForRead());
        return new Bitmap(rendered);
    }

    private static PdfDocument LoadDocument(byte[] content)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(content);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.DetachStream();
            }

            stream.Seek(0);
            return PdfDocument.LoadFromStreamAsync(stream).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException("Nie udało się odczytać dokumentu PDF: " + e.Message, e);
        }
    }

    /// <summary>
    /// Diagnostics only: a "print to file" printer would raise a "Save as" dialog; with
    /// ITBEES_PRINT_AGENT_PRINT_TO_DIR set its output goes to that directory instead.
    /// </summary>
    private void RedirectFilePrinter(PrintDocument printDocument, string documentName)
    {
        var directory = AgentInfo.PrintToDirectory;
        if (directory == null)
        {
            return;
        }

        var safeName = string.Concat(documentName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(Directory.CreateDirectory(directory).FullName,
            $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{safeName}.out");
        printDocument.PrinterSettings.PrintToFile = true;
        printDocument.PrinterSettings.PrintFileName = path;
        _log.Info($"Print output redirected to {path}");
    }
}
