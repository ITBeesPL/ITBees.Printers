using System.Security.Claims;
using ITBees.Printers.Protocol;
using ITBees.Printers.Services.Agents;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.AgentHub;

public static class PrintAgentAuthentication
{
    public const string Scheme = "PrintAgentToken";
    public const string AgentGuidClaim = "print_agent_guid";
    public const string UserAccountGuidClaim = "print_agent_user_guid";
    public const string MachineNameClaim = "print_agent_machine";
}

/// <summary>
/// Guards everything under the hub path: a request gets through only with a valid agent token,
/// sent as "Authorization: Bearer ..." (the .NET SignalR client does that on every request,
/// the WebSocket upgrade included) or as the "access_token" query parameter (clients that
/// cannot set headers on a WebSocket). The agent's identity lands in HttpContext.User, which
/// is what the hub sees as Context.User.
///
/// A middleware rather than an authentication scheme, so that the agent listener does not
/// need the ASP.NET Core authentication stack (and its data protection key ring) at all.
/// </summary>
public class PrintAgentTokenMiddleware
{
    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly HostApplicationServices _hostServices;
    private readonly ILogger<PrintAgentTokenMiddleware> _logger;

    public PrintAgentTokenMiddleware(RequestDelegate next, HostApplicationServices hostServices,
        ILogger<PrintAgentTokenMiddleware> logger)
    {
        _next = next;
        _hostServices = hostServices;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(PrintAgentProtocol.HubPath))
        {
            await _next(context);
            return;
        }

        PrintAgentIdentity? agent;
        try
        {
            using var scope = _hostServices.CreateScope();
            agent = scope.ServiceProvider.GetRequiredService<IPrintAgentSessionService>().Authenticate(ReadToken(context));
        }
        catch (Exception e)
        {
            // Not the agent's fault (e.g. the database is away) - it should simply retry later.
            _logger.LogError(e, "Print agent token could not be verified: {Message}", e.Message);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        if (agent == null)
        {
            // 401 tells the agent that its token is gone for good - it asks the user to log in again.
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(PrintAgentAuthentication.AgentGuidClaim, agent.AgentGuid.ToString()),
            new Claim(PrintAgentAuthentication.UserAccountGuidClaim, agent.UserAccountGuid.ToString()),
            new Claim(PrintAgentAuthentication.MachineNameClaim, agent.MachineName)
        }, PrintAgentAuthentication.Scheme));

        await _next(context);
    }

    private static string? ReadToken(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return header[BearerPrefix.Length..].Trim();
        }

        return context.Request.Query["access_token"].ToString();
    }
}
