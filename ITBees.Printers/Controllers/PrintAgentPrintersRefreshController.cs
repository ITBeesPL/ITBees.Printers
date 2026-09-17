using ITBees.Printers.Controllers.Models;
using ITBees.Printers.Services.Agents;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

/// <summary>Asks a connected print agent to scan the printers of its computer again.</summary>
[Authorize]
public class PrintAgentPrintersRefreshController : RestfulControllerBase<PrintAgentPrintersRefreshController>
{
    private readonly IPrintAgentsService _printAgentsService;

    public PrintAgentPrintersRefreshController(ILogger<PrintAgentPrintersRefreshController> logger,
        IPrintAgentsService printAgentsService) : base(logger)
    {
        _printAgentsService = printAgentsService;
    }

    [HttpPost]
    [Produces(typeof(PrintAgentPrintersRefreshVm))]
    public Task<IActionResult> Post(Guid agentGuid)
    {
        return ReturnOkResultAsync(async () => (object)new PrintAgentPrintersRefreshVm
        {
            AgentGuid = agentGuid,
            Requested = await _printAgentsService.RefreshPrinters(agentGuid)
        });
    }
}
