using ITBees.Printers.Controllers.Models;

namespace ITBees.Printers.Services.Settings;

/// <summary>Print settings of the logged-in user - each user keeps their own.</summary>
public interface IPrintSettingsService
{
    /// <summary>The default setting first, then one entry per document type of the host.</summary>
    List<PrintSettingVm> GetMine();

    /// <summary>The setting of a single document type (unknown types follow the default).</summary>
    PrintSettingVm GetMine(string? documentType);

    PrintSettingVm Save(PrintSettingUm printSettingUm);
}
