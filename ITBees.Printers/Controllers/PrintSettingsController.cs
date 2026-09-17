using ITBees.Printers.Controllers.Models;
using ITBees.Printers.Services.Settings;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

/// <summary>
/// Print settings of the logged-in user: the default first, then one entry per document type.
/// With <c>documentType</c> - just that one entry; this is what a "print" button asks for to
/// find out whether to print instantly or to hand the PDF to the browser.
/// </summary>
[Authorize]
public class PrintSettingsController : RestfulControllerBase<PrintSettingsController>
{
    private readonly IPrintSettingsService _printSettingsService;

    public PrintSettingsController(ILogger<PrintSettingsController> logger,
        IPrintSettingsService printSettingsService) : base(logger)
    {
        _printSettingsService = printSettingsService;
    }

    [HttpGet]
    [Produces(typeof(List<PrintSettingVm>))]
    public IActionResult Get(string? documentType)
    {
        return ReturnOkResult(() => documentType == null
            ? _printSettingsService.GetMine()
            : new List<PrintSettingVm> { _printSettingsService.GetMine(documentType) });
    }
}
