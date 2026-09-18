using System.Text.Json;
using ITBees.Printers.Protocol;
using ITBees.Printers.Services.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.AgentHub;

/// <summary>
/// Runs the agent endpoints: the registration endpoint and the <see cref="PrintAgentHub"/>.
/// Registering the library is all a host has to do - this hosted service builds a second,
/// minimal web host with a container of its own and
///  - opens the dedicated agent port (<see cref="PrintersSettings.AgentPort"/>) on a Kestrel
///    instance of its own, and
///  - hands the same pipeline to the host application, which serves it on its own port as well
///    (<see cref="PrintersSettings.ExposeOnApplicationPort"/>, see
///    <see cref="PrintAgentApplicationPortStartupFilter"/>).
///
/// A listener of its own - rather than one more Listen() on the host's Kestrel - keeps the
/// library from touching the host's server configuration (adding a Listen() call silently
/// disables a host's ASPNETCORE_URLS / UseUrls binding), and the separate container keeps the
/// agents away from the application's own middleware, CORS and authentication either way.
/// </summary>
public class PrintAgentHubHost : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _applicationServices;
    private readonly PrintAgentGateway _gateway;
    private readonly PrintAgentApplicationPortBridge _applicationPortBridge;
    private readonly PrintersSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PrintAgentHubHost> _logger;
    private IHost? _listener;

    public PrintAgentHubHost(
        IServiceProvider applicationServices,
        PrintAgentGateway gateway,
        PrintAgentApplicationPortBridge applicationPortBridge,
        PrintersSettings settings,
        ILoggerFactory loggerFactory)
    {
        _applicationServices = applicationServices;
        _gateway = gateway;
        _applicationPortBridge = applicationPortBridge;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PrintAgentHubHost>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dedicatedPort = _settings.AgentPort > 0;
        if (!dedicatedPort && !_settings.ExposeOnApplicationPort)
        {
            _logger.LogInformation("Print agent endpoints are disabled (no AgentPort, ExposeOnApplicationPort off)");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_settings.PublicAgentUrl) &&
            !PrintAgentRegistrationService.TryNormalizeUrl(_settings.PublicAgentUrl, out _))
        {
            _logger.LogWarning(
                "PublicAgentUrl \"{PublicAgentUrl}\" is not a valid http(s) address and is ignored - the agents' address will be worked out per pairing",
                _settings.PublicAgentUrl);
        }

        if (dedicatedPort && !await TryStart(listenOnAgentPort: true, cancellationToken))
        {
            // A busy port must not take the application down - nor instant printing, as long
            // as the agents can still come in through the application's own port.
            dedicatedPort = false;
        }

        if (!dedicatedPort && _settings.ExposeOnApplicationPort)
        {
            await TryStart(listenOnAgentPort: false, cancellationToken);
        }

        if (_listener != null)
        {
            _logger.LogInformation(
                "Print agent endpoints of \"{ServiceName}\" are up: dedicated port {DedicatedPort}, on the application's port: {OnApplicationPort} (hub: {HubPath})",
                _settings.ServiceName, dedicatedPort ? _settings.AgentPort.ToString() : "off",
                _settings.ExposeOnApplicationPort ? "yes" : "no", PrintAgentProtocol.HubPath);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _applicationPortBridge.Detach();
        _gateway.Detach();
        if (_listener == null)
        {
            return;
        }

        try
        {
            await _listener.StopAsync(cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Print agent listener did not stop cleanly: {Message}", e.Message);
        }
        finally
        {
            _listener.Dispose();
            _listener = null;
        }
    }

    private async Task<bool> TryStart(bool listenOnAgentPort, CancellationToken cancellationToken)
    {
        IHost? listener = null;
        try
        {
            listener = BuildListener(listenOnAgentPort);
            await listener.StartAsync(cancellationToken);
            _gateway.Attach(listener.Services.GetRequiredService<IHubContext<PrintAgentHub>>());
            _listener = listener;
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Print agent endpoints could not start{Where}: {Message}",
                listenOnAgentPort ? $" on port {_settings.AgentPort}" : string.Empty, e.Message);
            _applicationPortBridge.Detach();
            listener?.Dispose();
            return false;
        }
    }

    private IHost BuildListener(bool listenOnAgentPort)
    {
        // A bare HostBuilder on purpose: no appsettings, no environment variables, no command
        // line - nothing of the host's configuration (URLs, Kestrel section) may leak in here.
        return new HostBuilder()
            .ConfigureServices(services =>
            {
                // Logs go where the application's logs go.
                services.AddSingleton(_loggerFactory);
                services.AddSingleton<IHostLifetime, SilentLifetime>();
            })
            .ConfigureWebHost(webHost =>
            {
                if (listenOnAgentPort)
                {
                    webHost.UseKestrel(kestrel => kestrel.ListenAnyIP(_settings.AgentPort));
                }

                webHost.ConfigureServices(services =>
                {
                    if (!listenOnAgentPort)
                    {
                        // Requests only arrive through the application's port - nothing to listen on.
                        services.AddSingleton<IServer, NoListenerServer>();
                    }

                    services.AddSingleton(new HostApplicationServices(_applicationServices));
                    services.AddSingleton(_gateway);
                    services.AddSingleton(_settings);
                    services.AddRouting();
                    services.AddSignalR(signalR =>
                    {
                        // Agents only send small reports; documents travel the other way.
                        signalR.MaximumReceiveMessageSize = 1024 * 1024;
                        signalR.KeepAliveInterval = TimeSpan.FromSeconds(15);
                        signalR.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
                    });
                });
                webHost.Configure(app =>
                {
                    // Built once as a delegate of its own, so that the application's port can
                    // run the very same pipeline (same hub, same live connections).
                    var pipeline = app.New();

                    // The hub is for authenticated agents only; registration and info are anonymous.
                    pipeline.UseMiddleware<PrintAgentTokenMiddleware>();
                    pipeline.UseRouting();
                    pipeline.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<PrintAgentHub>(PrintAgentProtocol.HubPath);
                        endpoints.MapGet(PrintAgentProtocol.InfoPath, HandleInfo);
                        endpoints.MapPost(PrintAgentProtocol.RegisterPath, HandleRegister);
                    });

                    var requestDelegate = pipeline.Build();
                    if (_settings.ExposeOnApplicationPort)
                    {
                        _applicationPortBridge.Attach(requestDelegate, app.ApplicationServices);
                    }

                    app.Run(requestDelegate);
                });
            }, options => options.SuppressEnvironmentConfiguration = true)
            .Build();
    }

    private Task HandleInfo(HttpContext context)
    {
        return WriteJson(context, StatusCodes.Status200OK, new AgentListenerInfo
        {
            ServiceName = _settings.ServiceName,
            ProtocolVersion = PrintAgentProtocol.Version
        });
    }

    private async Task HandleRegister(HttpContext context)
    {
        AgentRegistrationRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<AgentRegistrationRequest>(
                context.Request.Body, JsonOptions, context.RequestAborted);
        }
        catch (JsonException)
        {
            await WriteJson(context, StatusCodes.Status400BadRequest,
                new AgentErrorResponse { Message = "The request body is not valid JSON." });
            return;
        }

        try
        {
            using var scope = _applicationServices.CreateScope();
            var response = scope.ServiceProvider.GetRequiredService<IPrintAgentSessionService>().Register(request!);
            await WriteJson(context, StatusCodes.Status200OK, response);
        }
        catch (PrintAgentRegistrationException e)
        {
            await WriteJson(context, StatusCodes.Status400BadRequest, new AgentErrorResponse { Message = e.Message });
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Print agent registration failed: {Message}", e.Message);
            await WriteJson(context, StatusCodes.Status500InternalServerError,
                new AgentErrorResponse { Message = "The print agent could not be registered." });
        }
    }

    private static Task WriteJson<T>(HttpContext context, int statusCode, T body)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        return JsonSerializer.SerializeAsync(context.Response.Body, body, JsonOptions, context.RequestAborted);
    }

    /// <summary>
    /// The listener lives and dies with the application's host - it must not react to Ctrl+C /
    /// SIGTERM on its own, nor announce "Application started" a second time.
    /// </summary>
    private sealed class SilentLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>A web host needs a server; this one never accepts anything by itself.</summary>
    private sealed class NoListenerServer : IServer
    {
        public IFeatureCollection Features { get; } = new FeatureCollection();

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
