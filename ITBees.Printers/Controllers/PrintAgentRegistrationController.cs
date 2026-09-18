using ITBees.Printers.Controllers.Models;
using ITBees.Printers.Services.Agents;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

/// <summary>
/// Step two of the print agent's browser login: the connect page, opened by the agent and
/// confirmed by the logged-in user, asks for a one-time code here and hands it to the agent's
/// loopback callback. The agent then exchanges the code for its own token on the agent endpoints.
/// </summary>
[Authorize]
public class PrintAgentRegistrationController : RestfulControllerBase<PrintAgentRegistrationController>
{
    private readonly IPrintAgentRegistrationService _registrationService;

    public PrintAgentRegistrationController(ILogger<PrintAgentRegistrationController> logger,
        IPrintAgentRegistrationService registrationService) : base(logger)
    {
        _registrationService = registrationService;
    }

    [HttpPost]
    [Produces(typeof(PrintAgentRegistrationVm))]
    public IActionResult Post([FromBody] PrintAgentRegistrationIm registrationIm)
    {
        return ReturnOkResult(() => _registrationService.Create(registrationIm, new PrintAgentRequestOrigin(
            Request.Scheme,
            Request.Host.Value ?? string.Empty,
            Request.PathBase.Value,
            Request.Headers["X-Forwarded-Proto"].ToString(),
            Request.Headers["X-Forwarded-Host"].ToString())));
    }
}
