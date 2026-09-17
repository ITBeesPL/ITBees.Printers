using ITBees.Interfaces.Repository;
using ITBees.Printers.Controllers.Models;
using ITBees.Printers.DbModels;
using ITBees.Printers.AgentHub;
using ITBees.RestfulApiControllers.Exceptions;
using ITBees.UserManager.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Services.Agents;

public class PrintAgentsService : IPrintAgentsService
{
    private readonly IAspCurrentUserService _aspCurrentUserService;
    private readonly IReadOnlyRepository<PrintAgent> _agentRoRepo;
    private readonly IWriteOnlyRepository<PrintAgent> _agentWoRepo;
    private readonly IReadOnlyRepository<PrintAgentPrinter> _printerRoRepo;
    private readonly IWriteOnlyRepository<UserPrintSetting> _settingWoRepo;
    private readonly IPrintAgentGateway _gateway;
    private readonly PrintersSettings _settings;
    private readonly ILogger<PrintAgentsService> _logger;

    public PrintAgentsService(
        IAspCurrentUserService aspCurrentUserService,
        IReadOnlyRepository<PrintAgent> agentRoRepo,
        IWriteOnlyRepository<PrintAgent> agentWoRepo,
        IReadOnlyRepository<PrintAgentPrinter> printerRoRepo,
        IWriteOnlyRepository<UserPrintSetting> settingWoRepo,
        IPrintAgentGateway gateway,
        PrintersSettings settings,
        ILogger<PrintAgentsService> logger)
    {
        _aspCurrentUserService = aspCurrentUserService;
        _agentRoRepo = agentRoRepo;
        _agentWoRepo = agentWoRepo;
        _printerRoRepo = printerRoRepo;
        _settingWoRepo = settingWoRepo;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    public List<PrintAgentVm> GetMine()
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);

        var agents = _agentRoRepo.GetData(x => x.UserAccountGuid == userGuid).ToList();
        var agentGuids = agents.Select(x => x.Guid).ToList();
        var printers = _printerRoRepo.GetData(x => agentGuids.Contains(x.PrintAgentGuid)).ToList();

        return agents
            .Select(agent => new PrintAgentVm(agent, _gateway.IsOnline(agent.Guid),
                printers.Where(x => x.PrintAgentGuid == agent.Guid)))
            // Connected computers first - these are the ones the user can print on right now.
            .OrderByDescending(x => x.IsOnline)
            .ThenBy(x => x.MachineName)
            .ToList();
    }

    public async Task Delete(Guid guid)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);
        var agent = GetMineOrThrow(guid, userGuid);

        var printerGuids = _printerRoRepo.GetData(x => x.PrintAgentGuid == agent.Guid)
            .Select(x => (Guid?)x.Guid)
            .ToList();

        // Settings are not tied to printers by a foreign key - reset them here. "Instant print"
        // without a printer would be a dead end, so they go back to the PDF.
        if (printerGuids.Count > 0)
        {
            var now = DateTime.UtcNow;
            _settingWoRepo.UpdateData(x => x.UserAccountGuid == userGuid && printerGuids.Contains(x.PrinterGuid), x =>
            {
                x.PrinterGuid = null;
                x.Mode = PrintMode.DownloadPdf;
                x.Modified = now;
            });
        }

        // The row first (the token dies with it), then the live connection.
        _agentWoRepo.DeleteData(x => x.Guid == agent.Guid);
        _logger.LogInformation("Print agent {AgentGuid} ({MachineName}) removed by user {UserAccountGuid}",
            agent.Guid, agent.MachineName, userGuid);

        await _gateway.Revoke(agent.Guid);
    }

    public async Task<bool> RefreshPrinters(Guid agentGuid)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);
        var agent = GetMineOrThrow(agentGuid, userGuid);

        return await _gateway.RequestPrintersRefresh(agent.Guid);
    }

    private PrintAgent GetMineOrThrow(Guid guid, Guid userGuid)
    {
        // Somebody else's agent is reported exactly like a missing one.
        var agent = _agentRoRepo.GetData(x => x.Guid == guid && x.UserAccountGuid == userGuid).FirstOrDefault();
        if (agent == null)
        {
            throw new FasApiErrorException("Nie znaleziono aplikacji drukującej.", StatusCodes.Status404NotFound);
        }

        return agent;
    }
}
