using System.Globalization;
using System.Text;

namespace ITBees.Printers.Services.Jobs;

public interface ITestPagePdfGenerator
{
    /// <summary>A one-page PDF confirming that the way from the application to the printer works.</summary>
    byte[] Generate(string serviceName, string printerName, DateTime printedAt);
}

/// <summary>
/// Writes the test page by hand - no PDF library, no fonts on the server (standard Helvetica).
/// The page is 50 x 30 mm on purpose: it fits the smallest label stock, and on a bigger sheet
/// the agent simply centers it. Texts are limited to ASCII.
/// </summary>
public class TestPagePdfGenerator : ITestPagePdfGenerator
{
    private const double PointsPerMm = 72.0 / 25.4;
    private const double WidthMm = 50;
    private const double HeightMm = 30;
    private const int MaxLineLength = 34;

    public byte[] Generate(string serviceName, string printerName, DateTime printedAt)
    {
        var width = WidthMm * PointsPerMm;
        var height = HeightMm * PointsPerMm;

        var content = new StringBuilder();
        // Frame 2 mm from the edge - shows at a glance whether the printout is clipped or shifted.
        var inset = 2 * PointsPerMm;
        content.Append("0.7 w\n");
        content.Append(Invariant($"{inset:0.##} {inset:0.##} {width - 2 * inset:0.##} {height - 2 * inset:0.##} re S\n"));

        var left = 5 * PointsPerMm;
        content.Append("BT\n");
        content.Append(Invariant($"/F1 9 Tf\n{left:0.##} {height - 9 * PointsPerMm:0.##} Td\n"));
        content.Append($"({Escape("Wydruk testowy - OK")}) Tj\n");
        content.Append("/F1 6.5 Tf\n0 -10 Td\n");
        content.Append($"({Escape(serviceName)}) Tj\n");
        content.Append("0 -9 Td\n");
        content.Append($"({Escape(printerName)}) Tj\n");
        content.Append("0 -9 Td\n");
        content.Append($"({Escape(printedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}) Tj\n");
        content.Append("0 -9 Td\n");
        content.Append($"({Escape("ITBees.Printers")}) Tj\n");
        content.Append("ET\n");

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R /ViewerPreferences << /PrintScaling /None >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            Invariant($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width:0.###} {height:0.###}] ") +
            "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream"
        };

        return Assemble(objects);
    }

    private static byte[] Assemble(IReadOnlyList<string> objects)
    {
        // Everything is ASCII, so string lengths equal byte offsets.
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xrefOffset = pdf.Length;
        pdf.Append($"xref\n0 {objects.Count + 1}\n");
        pdf.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string Invariant(FormattableString value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ASCII only: diacritics are stripped ("Łódź" prints as "Lodz"), anything else unknown
    /// becomes "?". PDF string delimiters are escaped and long lines cut.
    /// </summary>
    private static string Escape(string? text)
    {
        var value = RemoveDiacritics((text ?? string.Empty).Trim());
        if (value.Length > MaxLineLength)
        {
            value = value[..(MaxLineLength - 3)] + "...";
        }

        var escaped = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c is '(' or ')' or '\\')
            {
                escaped.Append('\\').Append(c);
            }
            else
            {
                escaped.Append(c is >= ' ' and <= '~' ? c : '?');
            }
        }

        return escaped.ToString();
    }

    private static string RemoveDiacritics(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // The stroke of "ł" is not a combining mark, so normalization leaves it alone.
            result.Append(c switch { 'ł' => 'l', 'Ł' => 'L', _ => c });
        }

        return result.ToString();
    }
}
