using ITBees.Printers.Controllers.Models;
using ITBees.Printers.Services.Jobs;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

/// <summary>Prints a small test page on one of the logged-in user's printers.</summary>
[Authorize]
public class PrintTestPageController : RestfulControllerBase<PrintTestPageController>
{
    private readonly IPrintJobService _printJobService;

    public PrintTestPageController(ILogger<PrintTestPageController> logger, IPrintJobService printJobService) :
        base(logger)
    {
        _printJobService = printJobService;
    }

    [HttpPost]
    [Produces(typeof(PrintJobVm))]
    public Task<IActionResult> Post([FromBody] PrintTestPageIm printTestPageIm)
    {
        return ReturnOkResultAsync(async () => (object)await _printJobService.PrintTestPage(printTestPageIm));
    }
}
