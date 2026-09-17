using System.Security.Claims;
using ITBees.Printers.Protocol;
using ITBees.Printers.Services.Agents;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.AgentHub;

/// <summary>
/// The hub print agents stay connected to. It lives in the agent listener's own container
/// (see <see cref="PrintAgentHubHost"/>); application services are reached through
/// <see cref="HostApplicationServices"/>, in a fresh scope per call - the same way a singleton
/// reaches scoped services. Only authenticated agents get here - see
/// <see cref="PrintAgentTokenMiddleware"/>.
/// </summary>
public class PrintAgentHub : Hub
{
    private readonly HostApplicationServices _hostServices;
    private readonly PrintAgentGateway _gateway;
    private readonly ILogger<PrintAgentHub> _logger;

    public PrintAgentHub(HostApplicationServices hostServices, PrintAgentGateway gateway, ILogger<PrintAgentHub> logger)
    {
        _hostServices = hostServices;
        _gateway = gateway;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var agentGuid = GetAgentGuid();
        _gateway.Register(agentGuid, Context);
        _logger.LogInformation("Print agent {AgentGuid} ({MachineName}) connected", agentGuid, GetMachineName());

        InSession(x => x.AgentConnected(agentGuid));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var agentGuid = GetAgentGuid();
        _gateway.Unregister(Context.ConnectionId);
        _logger.LogInformation("Print agent {AgentGuid} ({MachineName}) disconnected", agentGuid, GetMachineName());

        InSession(x => x.AgentDisconnected(agentGuid));
        await base.OnDisconnectedAsync(exception);
    }

    [HubMethodName(PrintAgentProtocol.ReportPrintersMethod)]
    public Task ReportPrinters(AgentPrintersReport report)
    {
        var agentGuid = GetAgentGuid();
        InSession(x => x.SavePrinters(agentGuid, report));
        return Task.CompletedTask;
    }

    [HubMethodName(PrintAgentProtocol.ReportPrintJobResultMethod)]
    public Task ReportPrintJobResult(AgentPrintJobResult result)
    {
        var agentGuid = GetAgentGuid();
        InSession(x => x.SaveJobResult(agentGuid, result));
        return Task.CompletedTask;
    }

    private void InSession(Action<IPrintAgentSessionService> action)
    {
        try
        {
            using var scope = _hostServices.CreateScope();
            action(scope.ServiceProvider.GetRequiredService<IPrintAgentSessionService>());
        }
        catch (Exception e)
        {
            // A database hiccup must not tear the agent's connection down.
            _logger.LogError(e, "Print agent hub call failed for agent {AgentGuid}: {Message}",
                Context.User?.FindFirstValue(PrintAgentAuthentication.AgentGuidClaim), e.Message);
        }
    }

    private Guid GetAgentGuid()
    {
        var value = Context.User?.FindFirstValue(PrintAgentAuthentication.AgentGuidClaim);
        return Guid.TryParse(value, out var agentGuid)
            ? agentGuid
            : throw new HubException("The connection is not authenticated as a print agent.");
    }

    private string? GetMachineName()
    {
        return Context.User?.FindFirstValue(PrintAgentAuthentication.MachineNameClaim);
    }
}
