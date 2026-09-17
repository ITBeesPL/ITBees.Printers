using ITBees.Printers.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.AgentHub;

/// <summary>
/// Registry of the live agent connections plus the bridge to the hub. The hub runs in the
/// agent listener's own container (see <see cref="PrintAgentHubHost"/>), so its
/// <see cref="IHubContext{THub}"/> is handed over here once the listener is up.
/// </summary>
public class PrintAgentGateway : IPrintAgentGateway
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AgentConnection> _connections = new();
    private readonly PrintersSettings _settings;
    private readonly ILogger<PrintAgentGateway> _logger;
    private IHubContext<PrintAgentHub>? _hubContext;

    public PrintAgentGateway(PrintersSettings settings, ILogger<PrintAgentGateway> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    internal void Attach(IHubContext<PrintAgentHub> hubContext)
    {
        _hubContext = hubContext;
    }

    internal void Detach()
    {
        _hubContext = null;
        lock (_sync)
        {
            _connections.Clear();
        }
    }

    internal void Register(Guid agentGuid, HubCallerContext context)
    {
        lock (_sync)
        {
            _connections[context.ConnectionId] = new AgentConnection(agentGuid, context, DateTime.UtcNow);
        }
    }

    /// <summary>Returns true when that was the agent's last connection (the agent went offline).</summary>
    internal bool Unregister(string connectionId)
    {
        lock (_sync)
        {
            if (!_connections.Remove(connectionId, out var removed))
            {
                return false;
            }

            return _connections.Values.All(x => x.AgentGuid != removed.AgentGuid);
        }
    }

    public bool IsOnline(Guid agentGuid)
    {
        return _hubContext != null && FindNewest(agentGuid) != null;
    }

    public async Task<AgentPrintJobAck> SendPrintJob(Guid agentGuid, AgentPrintJob job,
        CancellationToken cancellationToken = default)
    {
        var hubContext = _hubContext;
        var connection = FindNewest(agentGuid);
        if (hubContext == null || connection == null)
        {
            throw new PrintAgentOfflineException("The print agent is not connected.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_settings.AgentAckTimeout);
        try
        {
            return await hubContext.Clients.Client(connection.Context.ConnectionId)
                .InvokeAsync<AgentPrintJobAck>(PrintAgentProtocol.PrintMethod, job, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PrintAgentOfflineException("The print agent did not confirm the job in time.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The connection went away while the invocation was pending, or the agent faulted.
            _logger.LogWarning(e, "Print agent {AgentGuid} failed to take job {JobGuid}: {Message}",
                agentGuid, job.JobGuid, e.Message);
            throw new PrintAgentOfflineException("The print agent did not take the job: " + e.Message);
        }
    }

    public async Task<bool> RequestPrintersRefresh(Guid agentGuid)
    {
        var hubContext = _hubContext;
        var connectionIds = FindAll(agentGuid).Select(x => x.Context.ConnectionId).ToList();
        if (hubContext == null || connectionIds.Count == 0)
        {
            return false;
        }

        await hubContext.Clients.Clients(connectionIds).SendAsync(PrintAgentProtocol.RefreshPrintersMethod);
        return true;
    }

    public async Task Revoke(Guid agentGuid)
    {
        var hubContext = _hubContext;
        var connections = FindAll(agentGuid);
        if (hubContext == null || connections.Count == 0)
        {
            return;
        }

        try
        {
            await hubContext.Clients.Clients(connections.Select(x => x.Context.ConnectionId).ToList())
                .SendAsync(PrintAgentProtocol.RevokedMethod);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not notify print agent {AgentGuid} about its removal: {Message}",
                agentGuid, e.Message);
        }

        // Give the message a moment to leave before the transport is cut. An agent that misses
        // it still finds out: its next connection attempt is answered with 401.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        foreach (var connection in connections)
        {
            connection.Context.Abort();
        }
    }

    private AgentConnection? FindNewest(Guid agentGuid)
    {
        return FindAll(agentGuid).MaxBy(x => x.ConnectedUtc);
    }

    private List<AgentConnection> FindAll(Guid agentGuid)
    {
        lock (_sync)
        {
            return _connections.Values.Where(x => x.AgentGuid == agentGuid).ToList();
        }
    }

    private sealed record AgentConnection(Guid AgentGuid, HubCallerContext Context, DateTime ConnectedUtc);
}
