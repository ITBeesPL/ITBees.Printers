using ITBees.Printers.Protocol;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Printers.AgentHub;

/// <summary>
/// Hands the agent pipeline - built inside the agent listener's own container, see
/// <see cref="PrintAgentHubHost"/> - over to the host application, so that the very same
/// endpoints (same hub, same connections registry) can also be served on the application's port.
/// </summary>
public class PrintAgentApplicationPortBridge
{
    private volatile Target? _target;

    internal void Attach(RequestDelegate pipeline, IServiceProvider listenerServices)
    {
        _target = new Target(pipeline, listenerServices);
    }

    internal void Detach()
    {
        _target = null;
    }

    internal Target? Current => _target;

    internal sealed record Target(RequestDelegate Pipeline, IServiceProvider ListenerServices);
}

/// <summary>
/// Puts the agent endpoints in front of the host application's pipeline - without a line in its
/// Program.cs (<see cref="PrintersSettings.ExposeOnApplicationPort"/>). Only the three agent
/// endpoints are taken over; every other path, "/print-agent/connect" of a frontend served by
/// the same application included, goes on to the application untouched.
/// </summary>
public class PrintAgentApplicationPortStartupFilter : IStartupFilter
{
    private readonly PrintAgentApplicationPortBridge _bridge;
    private readonly PrintersSettings _settings;

    public PrintAgentApplicationPortStartupFilter(PrintAgentApplicationPortBridge bridge, PrintersSettings settings)
    {
        _bridge = bridge;
        _settings = settings;
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            if (_settings.ExposeOnApplicationPort)
            {
                app.Use(Handle);
            }

            next(app);
        };
    }

    private async Task Handle(HttpContext context, RequestDelegate next)
    {
        if (!IsAgentEndpoint(context.Request.Path))
        {
            await next(context);
            return;
        }

        var target = _bridge.Current;
        if (target == null)
        {
            // Starting up or shutting down - the agents simply try again in a moment.
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        // The pipeline belongs to the listener's container; give it that container's services
        // for the duration of the request (for a hub connection: for as long as it lives).
        var applicationServices = context.RequestServices;
        await using var scope = target.ListenerServices.CreateAsyncScope();
        context.RequestServices = scope.ServiceProvider;
        try
        {
            await target.Pipeline(context);
        }
        finally
        {
            context.RequestServices = applicationServices;
        }
    }

    private static bool IsAgentEndpoint(PathString path)
    {
        return path.StartsWithSegments(PrintAgentProtocol.HubPath) ||
               path.Equals(PrintAgentProtocol.RegisterPath, StringComparison.OrdinalIgnoreCase) ||
               path.Equals(PrintAgentProtocol.InfoPath, StringComparison.OrdinalIgnoreCase);
    }
}
