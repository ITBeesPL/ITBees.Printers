using System.Text.Json;
using ITBees.Printers.Protocol;
using ITBees.Printers.Services.Agents;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.AgentHub;

/// <summary>
/// Opens the dedicated agent port. Registering the library is all a host has to do: this
/// hosted service starts a second, minimal Kestrel instance on
/// <see cref="PrintersSettings.AgentPort"/> that serves nothing but the agent registration
/// endpoint and the <see cref="PrintAgentHub"/>.
///
/// A listener of its own - rather than one more endpoint of the host's Kestrel - keeps the
/// library from touching the host's server configuration (adding a Listen() call silently
/// disables a host's ASPNETCORE_URLS / UseUrls binding) and keeps the agents away from the
/// application's own middleware, CORS and authentication.
/// </summary>
public class PrintAgentHubHost : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceProvider _applicationServices;
    private readonly PrintAgentGateway _gateway;
    private readonly PrintersSettings _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PrintAgentHubHost> _logger;
    private IHost? _listener;

    public PrintAgentHubHost(
        IServiceProvider applicationServices,
        PrintAgentGateway gateway,
        PrintersSettings settings,
        ILoggerFactory loggerFactory)
    {
        _applicationServices = applicationServices;
        _gateway = gateway;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PrintAgentHubHost>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_settings.AgentPort <= 0)
        {
            _logger.LogInformation("Print agent listener is disabled (AgentPort = {Port})", _settings.AgentPort);
            return;
        }

        try
        {
            _listener = BuildListener();
            await _listener.StartAsync(cancellationToken);
            _gateway.Attach(_listener.Services.GetRequiredService<IHubContext<PrintAgentHub>>());
            _logger.LogInformation("Print agent listener of \"{ServiceName}\" started on port {Port} (hub: {HubPath})",
                _settings.ServiceName, _settings.AgentPort, PrintAgentProtocol.HubPath);
        }
        catch (Exception e)
        {
            // A busy port must not take the whole application down - it only loses instant
            // printing, and the frontend falls back to PDF files.
            _logger.LogError(e, "Print agent listener could not start on port {Port}: {Message}",
                _settings.AgentPort, e.Message);
            _listener?.Dispose();
            _listener = null;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
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

    private IHost BuildListener()
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
                webHost.UseKestrel(kestrel => kestrel.ListenAnyIP(_settings.AgentPort));
                webHost.ConfigureServices(services =>
                {
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
                    // The hub is for authenticated agents only; registration and info are anonymous.
                    app.UseMiddleware<PrintAgentTokenMiddleware>();
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<PrintAgentHub>(PrintAgentProtocol.HubPath);
                        endpoints.MapGet(PrintAgentProtocol.InfoPath, HandleInfo);
                        endpoints.MapPost(PrintAgentProtocol.RegisterPath, HandleRegister);
                    });
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
}
