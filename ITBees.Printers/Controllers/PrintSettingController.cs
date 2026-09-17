using ITBees.Printers.Controllers.Models;
using ITBees.Printers.Services.Settings;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

[Authorize]
public class PrintSettingController : RestfulControllerBase<PrintSettingController>
{
    private readonly IPrintSettingsService _printSettingsService;

    public PrintSettingController(ILogger<PrintSettingController> logger,
        IPrintSettingsService printSettingsService) : base(logger)
    {
        _printSettingsService = printSettingsService;
    }

    /// <summary>Saves the logged-in user's setting for one document type (or the "*" default).</summary>
    [HttpPut]
    [Produces(typeof(PrintSettingVm))]
    public IActionResult Put([FromBody] PrintSettingUm printSettingUm)
    {
        return ReturnOkResult(() => _printSettingsService.Save(printSettingUm));
    }
}
