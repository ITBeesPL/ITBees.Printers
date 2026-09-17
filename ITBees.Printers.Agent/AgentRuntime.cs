using ITBees.Printers.Agent.Configuration;
using ITBees.Printers.Agent.Connection;
using ITBees.Printers.Agent.Logging;
using ITBees.Printers.Agent.Login;
using ITBees.Printers.Agent.Printing;
using ITBees.Printers.Protocol;

namespace ITBees.Printers.Agent;

public enum NoticeKind
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Everything the agent does, without any UI: the known services, their connections, the
/// browser login and the print queue. The tray application only displays it.
/// </summary>
public class AgentRuntime
{
    private readonly object _sync = new();
    private readonly List<ServiceConnection> _connections = new();
    private readonly HashSet<string> _loginsInProgress = new();
    private readonly ProfileStore _profileStore = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PrinterScanner _scanner;
    private readonly PrintQueue _printQueue;
    private readonly BrowserLoginFlow _loginFlow;

    public AgentRuntime()
    {
        Log = new AgentLog();
        _scanner = new PrinterScanner(Log);
        _printQueue = new PrintQueue(new PdfPrinter(Log), Log);
        _loginFlow = new BrowserLoginFlow(Log);

        // Successful printouts speak for themselves (and go to the log) - only failures pop up.
        _printQueue.JobFinished += (queued, result) =>
        {
            if (!result.Success)
            {
                Notice?.Invoke(NoticeKind.Error, $"Nie udało się wydrukować: {queued.Job.DocumentName}",
                    result.Message ?? string.Empty);
            }
        };
    }

    public AgentLog Log { get; }

    /// <summary>The list of services or the state of one of them changed. Raised on any thread.</summary>
    public event Action? Changed;

    /// <summary>Something worth a tray balloon. Raised on any thread.</summary>
    public event Action<NoticeKind, string, string>? Notice;

    public void Start()
    {
        Log.Info($"{AgentInfo.ProductName} {AgentInfo.Version} started on {AgentInfo.MachineName} " +
                 $"(protocol {PrintAgentProtocol.Version})");
        _printQueue.Start(_shutdown.Token);

        _profileStore.Load();
        foreach (var profile in _profileStore.GetAll())
        {
            AddConnection(profile).Start();
        }

        Changed?.Invoke();
    }

    public List<ServiceConnection> GetConnections()
    {
        lock (_sync)
        {
            return _connections.ToList();
        }
    }

    /// <summary>
    /// Makes sure the agent is connected to the given site: a known, working service is left
    /// alone; anything else goes through the browser login.
    /// </summary>
    public async Task ConnectSite(string siteUrl, bool forceLogin = false)
    {
        var key = ProfileStore.NormalizeSiteUrl(siteUrl);
        var existing = FindConnection(key);
        if (existing != null && existing.State != ServiceConnectionState.LoginRequired && !forceLogin)
        {
            Notice?.Invoke(NoticeKind.Info, existing.Profile.DisplayName, "Ten serwis jest już połączony.");
            return;
        }

        lock (_sync)
        {
            if (!_loginsInProgress.Add(key))
            {
                return; // The browser is already open for this site.
            }
        }

        try
        {
            var login = await _loginFlow.Run(siteUrl, _shutdown.Token);

            var profile = existing?.Profile ?? new ServiceProfile { SiteUrl = siteUrl.Trim() };
            profile.HubUrl = login.HubUrl;
            profile.ServiceName = login.ServiceName;
            profile.AgentGuid = login.AgentGuid;
            profile.ConnectedUtc = DateTime.UtcNow;
            profile.SetToken(login.Token);
            _profileStore.Save(profile);

            if (existing != null)
            {
                await RemoveConnection(existing);
            }

            AddConnection(profile).Start();
            Log.Info($"Logged in to {profile.DisplayName} ({profile.SiteUrl}), agent listener: {profile.HubUrl}");
            Notice?.Invoke(NoticeKind.Info, profile.DisplayName, "Połączono. Drukarki tego komputera są już widoczne w serwisie.");
        }
        catch (LoginCancelledException e)
        {
            Log.Info($"Login to {siteUrl} cancelled in the browser");
            Notice?.Invoke(NoticeKind.Warning, "Logowanie anulowane", e.Message);
        }
        catch (OperationCanceledException)
        {
            // The application is closing.
        }
        catch (Exception e)
        {
            Log.Error($"Login to {siteUrl} failed", e);
            Notice?.Invoke(NoticeKind.Error, "Nie udało się połączyć z serwisem", e.Message);
        }
        finally
        {
            lock (_sync)
            {
                _loginsInProgress.Remove(key);
            }

            Changed?.Invoke();
        }
    }

    /// <summary>Forgets the service on this computer. The account side is cleaned up in the service's print settings.</summary>
    public async Task RemoveService(ServiceConnection connection)
    {
        await RemoveConnection(connection);
        _profileStore.Remove(connection.Profile);
        Log.Info($"Service {connection.Profile.DisplayName} removed from this agent");
        Changed?.Invoke();
    }

    public async Task Shutdown()
    {
        _shutdown.Cancel();
        await Task.WhenAll(GetConnections().Select(x => x.Stop()));
        Log.Info("Agent stopped");
    }

    private ServiceConnection? FindConnection(string normalizedSiteUrl)
    {
        return GetConnections().FirstOrDefault(x => ProfileStore.NormalizeSiteUrl(x.Profile.SiteUrl) == normalizedSiteUrl);
    }

    private ServiceConnection AddConnection(ServiceProfile profile)
    {
        var connection = new ServiceConnection(profile, _profileStore, _scanner, _printQueue, Log);
        connection.Changed += _ => Changed?.Invoke();
        lock (_sync)
        {
            _connections.Add(connection);
        }

        return connection;
    }

    private async Task RemoveConnection(ServiceConnection connection)
    {
        lock (_sync)
        {
            _connections.Remove(connection);
        }

        await connection.Stop();
    }
}
