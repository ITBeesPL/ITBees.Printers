using System.Net;
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

    public PrintAgentRegistrationVm Create(PrintAgentRegistrationIm? registrationIm,
        PrintAgentRequestOrigin requestOrigin)
    {
        var userGuid = _aspCurrentUserService.GetPrintingUserGuid(_settings);

        if (_settings.AgentPort <= 0 && !_settings.ExposeOnApplicationPort)
        {
            throw new FasApiErrorException("Drukowanie natychmiastowe jest wyłączone na tym serwerze.",
                StatusCodes.Status503ServiceUnavailable);
        }

        var hubUrl = BuildAgentUrl(_settings, registrationIm?.ApiUrl, requestOrigin);
        var code = _codeStore.Issue(userGuid);
        _logger.LogInformation(
            "Print agent registration code issued for user {UserAccountGuid} (machine: {MachineName}), agents connect to {HubUrl}",
            userGuid, registrationIm?.MachineName, hubUrl);

        return new PrintAgentRegistrationVm
        {
            Code = code,
            HubUrl = hubUrl,
            ServiceName = _settings.ServiceName,
            ExpiresInSeconds = (int)_settings.RegistrationCodeLifetime.TotalSeconds
        };
    }

    /// <summary>
    /// The address agents are sent to, in order of trust: the configured
    /// <see cref="PrintersSettings.PublicAgentUrl"/>; with the endpoints exposed on the
    /// application's port - the address the frontend reached the API at, then the request's own
    /// origin; with only the dedicated port - the request's host on that port.
    /// </summary>
    public static string BuildAgentUrl(PrintersSettings settings, string? reportedApiUrl,
        PrintAgentRequestOrigin origin)
    {
        if (TryNormalizeUrl(settings.PublicAgentUrl, out var configured))
        {
            return configured;
        }

        var scheme = NormalizeScheme(origin.ForwardedScheme) ?? NormalizeScheme(origin.Scheme) ?? "http";
        var host = FirstValue(origin.ForwardedHost) ?? origin.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }

        if (settings.ExposeOnApplicationPort)
        {
            if (TryNormalizeUrl(reportedApiUrl, out var reported))
            {
                return reported;
            }

            var pathBase = (origin.PathBase ?? string.Empty).TrimEnd('/');
            return $"{scheme}://{host}{pathBase}";
        }

        // Only the dedicated port: reachable on the API's host, without a proxy in between
        // (development, on-premise without TLS termination).
        return $"{scheme}://{HostNameOf(host)}:{settings.AgentPort}";
    }

    /// <summary>
    /// "adminapi.example.com" -> "https://adminapi.example.com". A missing scheme means https -
    /// except for this machine itself, where nobody runs TLS. False for anything that still is
    /// not an absolute http(s) address.
    /// </summary>
    public static bool TryNormalizeUrl(string? value, out string url)
    {
        url = string.Empty;
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            var host = HostNameOf(candidate.TrimStart('/').Split('/', '?', '#')[0]);
            candidate = (IsLoopback(host) ? "http://" : "https://") + candidate.TrimStart('/');
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        url = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }

    private static string? NormalizeScheme(string? value)
    {
        var scheme = FirstValue(value)?.ToLowerInvariant();
        return scheme is "http" or "https" ? scheme : null;
    }

    /// <summary>Forwarded headers accumulate one value per proxy: "https, http" - the first is the client's.</summary>
    private static string? FirstValue(string? headerValue)
    {
        var first = (headerValue ?? string.Empty).Split(',')[0].Trim();
        return first.Length == 0 ? null : first;
    }

    private static string HostNameOf(string hostWithOptionalPort)
    {
        if (hostWithOptionalPort.StartsWith('['))
        {
            var end = hostWithOptionalPort.IndexOf(']');
            return end > 0 ? hostWithOptionalPort[..(end + 1)] : hostWithOptionalPort;
        }

        var colon = hostWithOptionalPort.LastIndexOf(':');
        return colon > 0 ? hostWithOptionalPort[..colon] : hostWithOptionalPort;
    }

    private static bool IsLoopback(string host)
    {
        var bare = host.Trim('[', ']');
        return bare.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(bare, out var address) && IPAddress.IsLoopback(address));
    }
}
