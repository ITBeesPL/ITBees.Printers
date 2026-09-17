using ITBees.Interfaces.Repository;
using ITBees.Printers.Controllers.Models;
using ITBees.Printers.DbModels;
using ITBees.Printers.AgentHub;
using ITBees.RestfulApiControllers.Exceptions;
using ITBees.UserManager.Interfaces;
using Microsoft.AspNetCore.Http;

namespace ITBees.Printers.Services.Settings;

public class PrintSettingsService : IPrintSettingsService
{
    public const int MaxCopies = 99;
    private const string DefaultSettingName = "Domyślnie (wszystkie dokumenty)";

    private readonly IAspCurrentUserService _aspCurrentUserService;
    private readonly IReadOnlyRepository<UserPrintSetting> _settingRoRepo;
    private readonly IWriteOnlyRepository<UserPrintSetting> _settingWoRepo;
    private readonly IReadOnlyRepository<PrintAgentPrinter> _printerRoRepo;
    private readonly IPrintAgentGateway _gateway;
    private readonly PrintersSettings _settings;

    public PrintSettingsService(
        IAspCurrentUserService aspCurrentUserService,
        IReadOnlyRepository<UserPrintSetting> settingRoRepo,
        IWriteOnlyRepository<UserPrintSetting> settingWoRepo,
        IReadOnlyRepository<PrintAgentPrinter> printerRoRepo,
        IPrintAgentGateway gateway,
        PrintersSettings settings)
    {
        _aspCurrentUserService = aspCurrentUserService;
        _settingRoRepo = settingRoRepo;
        _settingWoRepo = settingWoRepo;
        _printerRoRepo = printerRoRepo;
        _gateway = gateway;
        _settings = settings;
    }

    public List<PrintSettingVm> GetMine()
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);
        var context = LoadContext(userGuid);

        var result = new List<PrintSettingVm> { BuildVm(PrintDocumentType.DefaultKey, DefaultSettingName, context) };
        result.AddRange(KnownDocumentTypes().Select(x => BuildVm(x.Key, x.Name, context)));
        return result;
    }

    public PrintSettingVm GetMine(string? documentType)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);
        var key = NormalizeKey(documentType);

        // A frontend may print a document type the host never declared - it follows the default.
        var name = key == PrintDocumentType.DefaultKey
            ? DefaultSettingName
            : KnownDocumentTypes().FirstOrDefault(x => x.Key == key)?.Name ?? key;

        return BuildVm(key, name, LoadContext(userGuid));
    }

    public PrintSettingVm Save(PrintSettingUm printSettingUm)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);
        if (printSettingUm == null)
        {
            throw new FasApiErrorException("Brak danych ustawienia drukowania.", StatusCodes.Status400BadRequest);
        }

        var key = NormalizeKey(printSettingUm.DocumentType);
        var isDefault = key == PrintDocumentType.DefaultKey;
        var documentType = KnownDocumentTypes().FirstOrDefault(x => x.Key == key);
        if (!isDefault && documentType == null)
        {
            throw new FasApiErrorException($"Nieznany typ dokumentu „{key}”.", StatusCodes.Status400BadRequest);
        }

        if (!Enum.IsDefined(printSettingUm.Mode) || (isDefault && printSettingUm.Mode == PrintMode.UseDefault))
        {
            throw new FasApiErrorException("Nieprawidłowy sposób drukowania.", StatusCodes.Status400BadRequest);
        }

        var copies = printSettingUm.Copies ?? 1;
        if (copies < 1 || copies > MaxCopies)
        {
            throw new FasApiErrorException($"Liczba kopii musi być z zakresu 1-{MaxCopies}.",
                StatusCodes.Status400BadRequest);
        }

        if (printSettingUm.Mode == PrintMode.UseDefault)
        {
            _settingWoRepo.DeleteData(x => x.UserAccountGuid == userGuid && x.DocumentType == key);
            return BuildVm(key, documentType!.Name, LoadContext(userGuid));
        }

        var printerGuid = printSettingUm.PrinterGuid;
        if (printSettingUm.Mode == PrintMode.InstantPrint && printerGuid == null)
        {
            throw new FasApiErrorException("Wybierz drukarkę, na której dokumenty mają być drukowane.",
                StatusCodes.Status400BadRequest);
        }

        if (printerGuid != null && !IsMyPrinter(printerGuid.Value, userGuid))
        {
            throw new FasApiErrorException("Nie znaleziono wybranej drukarki.", StatusCodes.Status404NotFound);
        }

        var now = DateTime.UtcNow;
        var updated = _settingWoRepo.UpdateData(x => x.UserAccountGuid == userGuid && x.DocumentType == key, x =>
        {
            x.Mode = printSettingUm.Mode;
            x.PrinterGuid = printerGuid;
            x.Copies = copies;
            x.Modified = now;
        });
        if (updated.Count == 0)
        {
            _settingWoRepo.InsertData(new UserPrintSetting
            {
                Guid = Guid.NewGuid(),
                UserAccountGuid = userGuid,
                DocumentType = key,
                Mode = printSettingUm.Mode,
                PrinterGuid = printerGuid,
                Copies = copies,
                Modified = now
            });
        }

        return BuildVm(key, isDefault ? DefaultSettingName : documentType!.Name, LoadContext(userGuid));
    }

    private bool IsMyPrinter(Guid printerGuid, Guid userGuid)
    {
        return _printerRoRepo.HasData(x => x.Guid == printerGuid && x.PrintAgent.UserAccountGuid == userGuid);
    }

    private IEnumerable<PrintDocumentType> KnownDocumentTypes()
    {
        return (_settings.DocumentTypes ?? new List<PrintDocumentType>())
            .Where(x => !string.IsNullOrWhiteSpace(x?.Key) && x.Key != PrintDocumentType.DefaultKey)
            .GroupBy(x => x.Key)
            .Select(x => x.First());
    }

    private static string NormalizeKey(string? documentType)
    {
        var key = (documentType ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return PrintDocumentType.DefaultKey;
        }

        return key.Length <= PrintDocumentType.MaxKeyLength ? key : key[..PrintDocumentType.MaxKeyLength];
    }

    private SettingsContext LoadContext(Guid userGuid)
    {
        var rows = _settingRoRepo.GetData(x => x.UserAccountGuid == userGuid).ToList();
        var printers = _printerRoRepo
            .GetData(x => x.PrintAgent.UserAccountGuid == userGuid, x => x.PrintAgent)
            .ToList();
        return new SettingsContext(rows, printers);
    }

    private PrintSettingVm BuildVm(string key, string name, SettingsContext context)
    {
        var own = context.Rows.FirstOrDefault(x => x.DocumentType == key);
        var effective = own ?? context.Rows.FirstOrDefault(x => x.DocumentType == PrintDocumentType.DefaultKey);
        var printer = effective?.PrinterGuid == null
            ? null
            : context.Printers.FirstOrDefault(x => x.Guid == effective.PrinterGuid);

        return new PrintSettingVm
        {
            DocumentType = key,
            DocumentTypeName = name,
            IsDefault = key == PrintDocumentType.DefaultKey,
            // The default itself has nothing to inherit from - without a row it is "PDF".
            Mode = own?.Mode ?? (key == PrintDocumentType.DefaultKey ? PrintMode.DownloadPdf : PrintMode.UseDefault),
            PrinterGuid = own?.PrinterGuid,
            Copies = own?.Copies ?? 1,
            EffectiveMode = effective?.Mode ?? PrintMode.DownloadPdf,
            EffectivePrinterGuid = printer?.Guid,
            EffectivePrinterName = printer?.Name,
            EffectiveAgentMachineName = printer?.PrintAgent?.MachineName,
            EffectiveCopies = effective?.Copies ?? 1,
            IsPrinterReady = effective?.Mode == PrintMode.InstantPrint && printer is { IsAvailable: true } &&
                             _gateway.IsOnline(printer.PrintAgentGuid)
        };
    }

    private sealed record SettingsContext(List<UserPrintSetting> Rows, List<PrintAgentPrinter> Printers);
}
