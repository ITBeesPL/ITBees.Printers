namespace ITBees.Printers;

/// <summary>A kind of document the host application prints, e.g. warehouse labels.</summary>
public class PrintDocumentType
{
    /// <summary>
    /// Key of the user's default print setting - it applies to every document type that has
    /// no setting of its own.
    /// </summary>
    public const string DefaultKey = "*";

    public const int MaxKeyLength = 100;

    public PrintDocumentType()
    {
    }

    public PrintDocumentType(string key, string name)
    {
        Key = key;
        Name = name;
    }

    /// <summary>Stable identifier sent by the frontend with every print job, e.g. "StockLabel".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Name shown in the user's print settings, e.g. "Etykiety magazynowe 50 × 30 mm".</summary>
    public string Name { get; set; } = string.Empty;
}
