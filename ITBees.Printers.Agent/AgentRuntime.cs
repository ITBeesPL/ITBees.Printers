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
    private readonly Dictionary<string, PendingLogin> _pendingLogins = new();
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
        Log.Info($"{AgentInfo.ProductName} {AgentInfo.DisplayVersion} started on {AgentInfo.MachineName} " +
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
    public async Task ConnectSite(string typedSiteUrl, bool forceLogin = false)
    {
        // "admin.example.com" is what people type - the scheme is on us.
        if (!AddressNormalizer.TryNormalize(typedSiteUrl, out var siteUrl))
        {
            Log.Error($"Login to {typedSiteUrl} failed: not a valid address");
            Notice?.Invoke(NoticeKind.Error, "Nieprawidłowy adres serwisu",
                $"„{typedSiteUrl}” nie jest poprawnym adresem. Podaj adres panelu WWW, np. admin.example.com.");
            return;
        }

        var key = ProfileStore.NormalizeSiteUrl(siteUrl);
        var existing = FindConnection(key);
        if (existing != null && existing.State != ServiceConnectionState.LoginRequired && !forceLogin)
        {
            Notice?.Invoke(NoticeKind.Info, existing.Profile.DisplayName, "Ten serwis jest już połączony.");
            return;
        }

        // Asking again for a site whose login is still open (the tab was closed, the wrong
        // account was used...) starts over instead of making the user wait out the old attempt.
        var pending = new PendingLogin(siteUrl, CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token));
        lock (_sync)
        {
            if (_pendingLogins.Remove(key, out var previous))
            {
                previous.Cancellation.Cancel();
            }

            _pendingLogins[key] = pending;
        }

        Changed?.Invoke();
        try
        {
            var login = await _loginFlow.Run(siteUrl, pending.Cancellation.Token);

            var profile = existing?.Profile ?? new ServiceProfile { SiteUrl = siteUrl };
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
            // Replaced by a newer attempt, cancelled by the user, or the application is closing.
            Log.Info($"Login to {siteUrl} abandoned");
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
                // Only this attempt's own entry - a newer one may already have taken the slot.
                if (_pendingLogins.TryGetValue(key, out var current) && ReferenceEquals(current, pending))
                {
                    _pendingLogins.Remove(key);
                }
            }

            pending.Cancellation.Dispose();
            Changed?.Invoke();
        }
    }

    /// <summary>Sites whose browser login is open right now.</summary>
    public List<string> GetPendingLogins()
    {
        lock (_sync)
        {
            return _pendingLogins.Values.Select(x => x.SiteUrl).ToList();
        }
    }

    public void CancelLogin(string siteUrl)
    {
        lock (_sync)
        {
            if (_pendingLogins.TryGetValue(ProfileStore.NormalizeSiteUrl(siteUrl), out var pending))
            {
                pending.Cancellation.Cancel();
            }
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

    private sealed record PendingLogin(string SiteUrl, CancellationTokenSource Cancellation);

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
