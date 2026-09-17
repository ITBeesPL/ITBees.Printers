using ITBees.Printers.Controllers.Models;
using ITBees.Printers.Services.Jobs;
using ITBees.RestfulApiControllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Controllers;

/// <summary>
/// Instant printing. POST takes a PDF the frontend already has (any document the application
/// can download can be printed this way) and sends it to the printer the logged-in user chose
/// for its document type; GET reports how the job ended.
/// </summary>
[Authorize]
public class PrintJobController : RestfulControllerBase<PrintJobController>
{
    private readonly IPrintJobService _printJobService;

    public PrintJobController(ILogger<PrintJobController> logger, IPrintJobService printJobService) : base(logger)
    {
        _printJobService = printJobService;
    }

    [HttpGet]
    [Produces(typeof(PrintJobVm))]
    public IActionResult Get(Guid guid)
    {
        return ReturnOkResult(() => _printJobService.Get(guid));
    }

    [HttpPost]
    [Produces(typeof(PrintJobVm))]
    // The document arrives base64-encoded inside JSON - allow for the encoding overhead on top
    // of PrintersSettings.MaxDocumentSizeBytes (the service enforces the real limit).
    [RequestSizeLimit(64 * 1024 * 1024)]
    public Task<IActionResult> Post([FromBody] PrintJobIm printJobIm)
    {
        return ReturnOkResultAsync(async () => (object)await _printJobService.Create(printJobIm));
    }
}
