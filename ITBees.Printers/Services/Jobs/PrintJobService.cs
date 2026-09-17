using ITBees.Interfaces.Repository;
using ITBees.Printers.Controllers.Models;
using ITBees.Printers.DbModels;
using ITBees.Printers.AgentHub;
using ITBees.Printers.Protocol;
using ITBees.Printers.Services.Settings;
using ITBees.RestfulApiControllers.Exceptions;
using ITBees.UserManager.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Services.Jobs;

public class PrintJobService : IPrintJobService
{
    private const string TestPageDocumentType = "TestPage";
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    private readonly IAspCurrentUserService _aspCurrentUserService;
    private readonly IPrintSettingsService _printSettingsService;
    private readonly IReadOnlyRepository<PrintAgentPrinter> _printerRoRepo;
    private readonly IReadOnlyRepository<PrintJob> _jobRoRepo;
    private readonly IWriteOnlyRepository<PrintJob> _jobWoRepo;
    private readonly IPrintAgentGateway _gateway;
    private readonly ITestPagePdfGenerator _testPagePdfGenerator;
    private readonly PrintersSettings _settings;
    private readonly ILogger<PrintJobService> _logger;

    public PrintJobService(
        IAspCurrentUserService aspCurrentUserService,
        IPrintSettingsService printSettingsService,
        IReadOnlyRepository<PrintAgentPrinter> printerRoRepo,
        IReadOnlyRepository<PrintJob> jobRoRepo,
        IWriteOnlyRepository<PrintJob> jobWoRepo,
        IPrintAgentGateway gateway,
        ITestPagePdfGenerator testPagePdfGenerator,
        PrintersSettings settings,
        ILogger<PrintJobService> logger)
    {
        _aspCurrentUserService = aspCurrentUserService;
        _printSettingsService = printSettingsService;
        _printerRoRepo = printerRoRepo;
        _jobRoRepo = jobRoRepo;
        _jobWoRepo = jobWoRepo;
        _gateway = gateway;
        _testPagePdfGenerator = testPagePdfGenerator;
        _settings = settings;
        _logger = logger;
    }

    public Task<PrintJobVm> Create(PrintJobIm printJobIm)
    {
        if (printJobIm == null || string.IsNullOrWhiteSpace(printJobIm.ContentBase64))
        {
            throw new FasApiErrorException("Brak dokumentu do wydrukowania.", StatusCodes.Status400BadRequest);
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(printJobIm.ContentBase64);
        }
        catch (FormatException)
        {
            throw new FasApiErrorException("Dokument nie jest poprawnie zakodowany (base64).",
                StatusCodes.Status400BadRequest);
        }

        return Print(printJobIm.DocumentType, printJobIm.DocumentName ?? string.Empty, content, printJobIm.Copies);
    }

    public Task<PrintJobVm> Print(string? documentType, string documentName, byte[] pdfContent, int? copies = null)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);
        ValidateDocument(pdfContent);

        var setting = _printSettingsService.GetMine(documentType);
        if (setting.EffectiveMode != PrintMode.InstantPrint || setting.EffectivePrinterGuid == null)
        {
            throw new FasApiErrorException(
                "Drukowanie natychmiastowe nie jest włączone dla tego rodzaju dokumentów - " +
                "włącz je w ustawieniach drukowania albo pobierz plik PDF.", StatusCodes.Status409Conflict);
        }

        return Dispatch(userGuid, setting.EffectivePrinterGuid.Value, setting.DocumentType, documentName,
            pdfContent, copies ?? setting.EffectiveCopies);
    }

    public Task<PrintJobVm> PrintTestPage(PrintTestPageIm printTestPageIm)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);
        if (printTestPageIm == null || printTestPageIm.PrinterGuid == Guid.Empty)
        {
            throw new FasApiErrorException("Wskaż drukarkę.", StatusCodes.Status400BadRequest);
        }

        var printer = GetMyPrinterOrThrow(printTestPageIm.PrinterGuid, userGuid);
        var content = _testPagePdfGenerator.Generate(_settings.ServiceName, printer.Name, DateTime.Now);

        return Dispatch(userGuid, printer.Guid, TestPageDocumentType, "Wydruk testowy", content, 1);
    }

    public PrintJobVm Get(Guid guid)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);

        var job = _jobRoRepo.GetData(x => x.Guid == guid && x.UserAccountGuid == userGuid).FirstOrDefault();
        if (job == null)
        {
            throw new FasApiErrorException("Nie znaleziono zlecenia wydruku.", StatusCodes.Status404NotFound);
        }

        return new PrintJobVm(job);
    }

    private async Task<PrintJobVm> Dispatch(Guid userGuid, Guid printerGuid, string documentType,
        string documentName, byte[] content, int copies)
    {
        var printer = GetMyPrinterOrThrow(printerGuid, userGuid);
        var agentGuid = printer.PrintAgentGuid;

        var job = new PrintJob
        {
            Guid = Guid.NewGuid(),
            UserAccountGuid = userGuid,
            PrintAgentGuid = agentGuid,
            PrinterGuid = printer.Guid,
            PrinterName = printer.Name,
            AgentMachineName = printer.PrintAgent?.MachineName,
            DocumentType = documentType,
            DocumentName = NormalizeDocumentName(documentName),
            ContentLength = content.Length,
            Copies = Math.Clamp(copies, 1, PrintSettingsService.MaxCopies),
            Created = DateTime.UtcNow
        };

        if (!_gateway.IsOnline(agentGuid))
        {
            job.Status = PrintJobStatus.AgentOffline;
            job.StatusMessage = OfflineMessage(job.AgentMachineName);
            job.Completed = job.Created;
            return new PrintJobVm(_jobWoRepo.InsertData(job));
        }

        // Stored as "sent" before it is sent: once the agent has the job, the only writer of
        // this row is the agent's result - the confirmation below never has to race with it.
        job.Status = PrintJobStatus.SentToAgent;
        _jobWoRepo.InsertData(job);

        AgentPrintJobAck ack;
        try
        {
            ack = await _gateway.SendPrintJob(agentGuid, new AgentPrintJob
            {
                JobGuid = job.Guid,
                PrinterName = printer.Name,
                DocumentName = job.DocumentName,
                ContentBase64 = Convert.ToBase64String(content),
                Copies = job.Copies
            });
        }
        catch (PrintAgentOfflineException e)
        {
            _logger.LogWarning("Print job {JobGuid} did not reach agent {AgentGuid}: {Message}",
                job.Guid, agentGuid, e.Message);
            return Close(job.Guid, PrintJobStatus.AgentOffline, OfflineMessage(job.AgentMachineName));
        }

        if (ack is not { Accepted: true })
        {
            return Close(job.Guid, PrintJobStatus.Rejected,
                string.IsNullOrWhiteSpace(ack?.Message) ? "Aplikacja drukująca odrzuciła wydruk." : ack.Message);
        }

        return new PrintJobVm(job);
    }

    private PrintJobVm Close(Guid jobGuid, PrintJobStatus status, string message)
    {
        var now = DateTime.UtcNow;

        // Only a job still waiting for its result - a result that slipped in wins.
        var updated = _jobWoRepo.UpdateData(x => x.Guid == jobGuid && x.Status == PrintJobStatus.SentToAgent, x =>
        {
            x.Status = status;
            x.StatusMessage = message.Length <= PrintJob.MaxStatusMessageLength
                ? message
                : message[..PrintJob.MaxStatusMessageLength];
            x.Completed = now;
        });

        return new PrintJobVm(updated.FirstOrDefault() ?? _jobRoRepo.GetData(x => x.Guid == jobGuid).First());
    }

    private PrintAgentPrinter GetMyPrinterOrThrow(Guid printerGuid, Guid userGuid)
    {
        // A printer is usable only through an agent of the very same user.
        var printer = _printerRoRepo
            .GetData(x => x.Guid == printerGuid && x.PrintAgent.UserAccountGuid == userGuid, x => x.PrintAgent)
            .FirstOrDefault();
        if (printer == null)
        {
            throw new FasApiErrorException("Nie znaleziono drukarki.", StatusCodes.Status404NotFound);
        }

        return printer;
    }

    private void ValidateDocument(byte[]? content)
    {
        if (content == null || content.Length == 0)
        {
            throw new FasApiErrorException("Brak dokumentu do wydrukowania.", StatusCodes.Status400BadRequest);
        }

        if (content.Length > _settings.MaxDocumentSizeBytes)
        {
            throw new FasApiErrorException(
                $"Dokument jest za duży do drukowania natychmiastowego (limit {_settings.MaxDocumentSizeBytes / (1024 * 1024)} MB).",
                StatusCodes.Status413PayloadTooLarge);
        }

        if (!content.AsSpan().StartsWith(PdfSignature))
        {
            throw new FasApiErrorException("Drukować można tylko dokumenty PDF.", StatusCodes.Status400BadRequest);
        }
    }

    private static string NormalizeDocumentName(string? documentName)
    {
        var name = (documentName ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return "Dokument";
        }

        return name.Length <= PrintJob.MaxDocumentNameLength ? name : name[..PrintJob.MaxDocumentNameLength];
    }

    private static string OfflineMessage(string? machineName)
    {
        return string.IsNullOrWhiteSpace(machineName)
            ? "Aplikacja drukująca nie jest połączona."
            : $"Aplikacja drukująca na komputerze „{machineName}” nie jest połączona.";
    }
}
