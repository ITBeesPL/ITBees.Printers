using ITBees.Printers.Services.Agents;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

[Authorize]
public class PrintAgentController : RestfulControllerBase<PrintAgentController>
{
    private readonly IPrintAgentsService _printAgentsService;

    public PrintAgentController(ILogger<PrintAgentController> logger, IPrintAgentsService printAgentsService) :
        base(logger)
    {
        _printAgentsService = printAgentsService;
    }

    /// <summary>Disconnects one of the logged-in user's print agents for good.</summary>
    [HttpDelete]
    public Task<IActionResult> Delete(Guid guid)
    {
        return ReturnOkResultAsync(() => _printAgentsService.Delete(guid));
    }
}
