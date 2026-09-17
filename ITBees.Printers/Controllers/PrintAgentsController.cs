using ITBees.Printers.Controllers.Models;
using ITBees.Printers.Services.Agents;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

/// <summary>Print agents of the logged-in user, each with its printers and its online state.</summary>
[Authorize]
public class PrintAgentsController : RestfulControllerBase<PrintAgentsController>
{
    private readonly IPrintAgentsService _printAgentsService;

    public PrintAgentsController(ILogger<PrintAgentsController> logger, IPrintAgentsService printAgentsService) :
        base(logger)
    {
        _printAgentsService = printAgentsService;
    }

    [HttpGet]
    [Produces(typeof(List<PrintAgentVm>))]
    public IActionResult Get()
    {
        return ReturnOkResult(() => _printAgentsService.GetMine());
    }
}
