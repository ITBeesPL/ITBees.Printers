using ITBees.Printers.Controllers.Models;
using ITBees.RestfulApiControllers.Exceptions;
using ITBees.UserManager.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Services.Agents;

public class PrintAgentRegistrationService : IPrintAgentRegistrationService
{
    private readonly IAspCurrentUserService _aspCurrentUserService;
    private readonly PrintAgentRegistrationCodeStore _codeStore;
    private readonly PrintersSettings _settings;
    private readonly ILogger<PrintAgentRegistrationService> _logger;

    public PrintAgentRegistrationService(
        IAspCurrentUserService aspCurrentUserService,
        PrintAgentRegistrationCodeStore codeStore,
        PrintersSettings settings,
        ILogger<PrintAgentRegistrationService> logger)
    {
        _aspCurrentUserService = aspCurrentUserService;
        _codeStore = codeStore;
        _settings = settings;
        _logger = logger;
    }

    public PrintAgentRegistrationVm Create(PrintAgentRegistrationIm? registrationIm, string requestScheme,
        string requestHost)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);

        if (_settings.AgentPort <= 0)
        {
            throw new FasApiErrorException("Drukowanie natychmiastowe jest wyłączone na tym serwerze.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var code = _codeStore.Issue(userGuid);
        _logger.LogInformation("Print agent registration code issued for user {UserAccountGuid} (machine: {MachineName})",
            userGuid, registrationIm?.MachineName);

        return new PrintAgentRegistrationVm
        {
            Code = code,
            HubUrl = BuildAgentUrl(_settings, requestScheme, requestHost),
            ServiceName = _settings.ServiceName,
            ExpiresInSeconds = (int)_settings.RegistrationCodeLifetime.TotalSeconds
        };
    }

    public static string BuildAgentUrl(PrintersSettings settings, string requestScheme, string requestHost)
    {
        var configured = settings.PublicAgentUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrEmpty(configured))
        {
            return configured;
        }

        // No reverse proxy in the picture (development, on-premise without TLS termination):
        // the agent listener is reachable on the same host as the API, on its own port.
        var scheme = string.Equals(requestScheme, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        var host = string.IsNullOrWhiteSpace(requestHost) ? "localhost" : requestHost;
        return $"{scheme}://{host}:{settings.AgentPort}";
    }
}
