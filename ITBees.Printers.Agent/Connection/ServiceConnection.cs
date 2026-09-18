using System.Drawing.Printing;
using System.Net;
using System.Net.WebSockets;
using ITBees.Printers.Agent.Configuration;
using ITBees.Printers.Agent.Logging;
using ITBees.Printers.Agent.Printing;
using ITBees.Printers.Protocol;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Agent.Connection;

public enum ServiceConnectionState
{
    Disconnected,
    Connecting,
    Connected,

    /// <summary>The service no longer accepts the token (agent removed from the account) - the user has to log in again.</summary>
    LoginRequired
}

/// <summary>
/// The agent's live link to one service: keeps the SignalR connection to the service's agent
/// listener up (forever, with back-off), reports the printers of this computer and takes
/// print jobs.
/// </summary>
public class ServiceConnection
{
    private static readonly TimeSpan PrinterScanInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    // Connecting normally takes well under a second; this only has to beat a request that hangs.
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// The transports, best first - one per connection attempt. SignalR falls back to the next
    /// transport by itself only when one cannot even start. Reverse proxies break them in other
    /// ways too: nginx without WebSocket settings answers the upgrade with a plain 200 (that one
    /// SignalR handles), but its response buffering then holds the event stream back until the
    /// server gives up waiting for the handshake - the stream "starts", the handshake fails, and
    /// SignalR never gets to long polling. So the agent walks down this list itself and stays on
    /// the first transport that works, for as long as it runs.
    /// </summary>
    private static readonly HttpTransportType[] Transports =
    {
        HttpTransportType.WebSockets,
        HttpTransportType.ServerSentEvents,
        HttpTransportType.LongPolling
    };

    private readonly ProfileStore _profileStore;
    private readonly PrinterScanner _scanner;
    private readonly PrintQueue _printQueue;
    private readonly AgentLog _log;
    private readonly CancellationTokenSource _stop = new();
    private HubConnection? _connection;
    private int _transport;
    private string? _reportedFingerprint;

    public ServiceConnection(ServiceProfile profile, ProfileStore profileStore, PrinterScanner scanner,
        PrintQueue printQueue, AgentLog log)
    {
        Profile = profile;
        _profileStore = profileStore;
        _scanner = scanner;
        _printQueue = printQueue;
        _log = log;
        State = profile.HasToken ? ServiceConnectionState.Disconnected : ServiceConnectionState.LoginRequired;
    }

    public ServiceProfile Profile { get; }
    public ServiceConnectionState State { get; private set; }
    public string? LastError { get; private set; }
    public int ReportedPrinters { get; private set; }

    /// <summary>The transport of the current (or last) connection.</summary>
    public HttpTransportType Transport => ForcedTransports ?? Transports[_transport];

    /// <summary>Raised on any thread whenever the state shown to the user changes.</summary>
    public event Action<ServiceConnection>? Changed;

    public void Start()
    {
        _ = Task.Run(() => Run(_stop.Token));
    }

    public async Task Stop()
    {
        _stop.Cancel();
        var connection = _connection;
        if (connection != null)
        {
            await DisposeQuietly(connection);
        }
    }

    private async Task Run(CancellationToken cancellationToken)
    {
        var token = Profile.GetToken();
        if (token == null)
        {
            SetState(ServiceConnectionState.LoginRequired, "Brak tokenu - wymagane logowanie.");
            return;
        }

        // The reconnecting is done here rather than by SignalR's automatic reconnect: every
        // (re)connection is a fresh attempt of our own, so a revoked token always shows up the
        // same way - as the 401 of StartAsync - instead of being swallowed by a retry policy.
        var retryDelay = InitialRetryDelay;
        while (!cancellationToken.IsCancellationRequested && State != ServiceConnectionState.LoginRequired)
        {
            var closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var connection = BuildConnection(token, closed);
            _connection = connection;
            try
            {
                var outcome = await Connect(connection, cancellationToken);
                if (outcome == ConnectOutcome.Stopped)
                {
                    return;
                }

                if (outcome != ConnectOutcome.Connected)
                {
                    if (outcome == ConnectOutcome.RetryLater)
                    {
                        await Task.Delay(retryDelay, cancellationToken);
                        retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
                    }

                    continue;
                }

                retryDelay = InitialRetryDelay;

                // Connected: keep an eye on the printers until the connection goes away.
                while (await Task.WhenAny(closed.Task, Task.Delay(PrinterScanInterval, cancellationToken)) != closed.Task)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    await ReportPrinters(force: false);
                }

                var error = await closed.Task;
                if (cancellationToken.IsCancellationRequested || State == ServiceConnectionState.LoginRequired)
                {
                    return;
                }

                _log.Warning($"{Profile.DisplayName}: connection lost, reconnecting ({error?.Message ?? "closed by the service"})");
                SetState(ServiceConnectionState.Connecting, error?.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            finally
            {
                await DisposeQuietly(connection);
            }
        }
    }

    private HubConnection BuildConnection(string token, TaskCompletionSource<Exception?> closed)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(Profile.HubUrl.TrimEnd('/') + PrintAgentProtocol.HubPath, options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.Transports = Transport;
            });
        if (AgentInfo.Trace)
        {
            builder.ConfigureLogging(logging => logging
                .AddProvider(new AgentLogLoggerProvider(_log, Profile.DisplayName))
                .SetMinimumLevel(LogLevel.Trace));
        }

        var connection = builder.Build();

        connection.On<AgentPrintJob, AgentPrintJobAck>(PrintAgentProtocol.PrintMethod, HandlePrint);
        connection.On(PrintAgentProtocol.RefreshPrintersMethod, () => ReportPrinters(force: true));
        connection.On(PrintAgentProtocol.RevokedMethod, () =>
        {
            _log.Warning($"{Profile.DisplayName}: this agent was removed from the account");
            RequireLogin("Aplikacja została odłączona w ustawieniach konta.");
        });
        connection.Closed += error =>
        {
            closed.TrySetResult(error);
            return Task.CompletedTask;
        };

        return connection;
    }

    /// <summary>The transports set by <see cref="AgentInfo.TransportsVariable"/>, if any.</summary>
    private static HttpTransportType? ForcedTransports
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(AgentInfo.TransportsVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var transports = HttpTransportType.None;
            foreach (var name in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<HttpTransportType>(name, ignoreCase: true, out var transport))
                {
                    transports |= transport;
                }
            }

            return transports == HttpTransportType.None ? null : transports;
        }
    }

    private static async Task DisposeQuietly(HubConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch
        {
            // Going away anyway.
        }
    }

    /// <summary>One connection attempt. A connection object is good for one attempt only - the caller builds the next.</summary>
    private async Task<ConnectOutcome> Connect(HubConnection connection, CancellationToken cancellationToken)
    {
        using var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startTimeout.CancelAfter(StartTimeout);
        try
        {
            SetState(ServiceConnectionState.Connecting, LastError);
            await connection.StartAsync(startTimeout.Token);
            SetState(ServiceConnectionState.Connected, null);
            _log.Info($"{Profile.DisplayName}: connected to {Profile.HubUrl}" +
                      (Transport == HttpTransportType.WebSockets ? string.Empty : $" ({Describe(Transport)})"));
            await ReportPrinters(force: true);
            return ConnectOutcome.Connected;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return ConnectOutcome.Stopped;
        }
        catch (Exception e) when (IsUnauthorized(e))
        {
            // 401 is final: the agent was removed from the account, or paired again elsewhere.
            _log.Warning($"{Profile.DisplayName}: the service no longer accepts this agent's token");
            RequireLogin("Serwis nie rozpoznaje już tej aplikacji - zaloguj się ponownie.");
            return ConnectOutcome.Stopped;
        }
        catch (Exception e) when (startTimeout.IsCancellationRequested || IsTransportFailure(e))
        {
            // The service is there, this transport just does not get through - or nothing came
            // back in time at all (a proxy that swallows the WebSocket upgrade never answers).
            // SignalR reports a cancelled start as "unable to connect with any of the available
            // transports", not as an OperationCanceledException - hence the filter on the token.
            var reason = startTimeout.IsCancellationRequested
                ? $"no answer within {StartTimeout.TotalSeconds:0} s"
                : Reason(e);
            if (ForcedTransports == null && _transport < Transports.Length - 1)
            {
                var failed = Transport;
                _transport++;
                _log.Warning($"{Profile.DisplayName}: no connection over {Describe(failed)} to {Profile.HubUrl} " +
                             $"({reason}) - trying {Describe(Transport)}");
                return ConnectOutcome.RetryNow;
            }

            var error = startTimeout.IsCancellationRequested ? "Serwis nie odpowiada." : reason;
            if (LastError != error)
            {
                _log.Warning($"{Profile.DisplayName}: cannot connect to {Profile.HubUrl} ({Describe(Transport)}): {reason}");
            }

            SetState(ServiceConnectionState.Disconnected, error);
            return ConnectOutcome.RetryLater;
        }
        catch (Exception e)
        {
            if (LastError != e.Message)
            {
                _log.Warning($"{Profile.DisplayName}: cannot connect to {Profile.HubUrl}: {e.Message}");
            }

            SetState(ServiceConnectionState.Disconnected, e.Message);
            return ConnectOutcome.RetryLater;
        }
    }

    private enum ConnectOutcome
    {
        Connected,
        RetryNow,
        RetryLater,

        /// <summary>The application is closing, or the service wants a new login.</summary>
        Stopped
    }

    private AgentPrintJobAck HandlePrint(AgentPrintJob job)
    {
        if (job == null || string.IsNullOrWhiteSpace(job.ContentBase64))
        {
            return new AgentPrintJobAck { Accepted = false, Message = "Zlecenie nie zawiera dokumentu." };
        }

        if (!string.Equals(job.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentPrintJobAck { Accepted = false, Message = $"Nieobsługiwany format dokumentu: {job.ContentType}." };
        }

        if (!new PrinterSettings { PrinterName = job.PrinterName }.IsValid)
        {
            _log.Warning($"{Profile.DisplayName}: job {job.JobGuid:N} refused - no printer \"{job.PrinterName}\"");
            _ = ReportPrinters(force: true);
            return new AgentPrintJobAck
            {
                Accepted = false,
                Message = $"Drukarka „{job.PrinterName}” nie istnieje już na komputerze {AgentInfo.MachineName}."
            };
        }

        _printQueue.Enqueue(new QueuedPrintJob(job, Profile.DisplayName, ReportJobResult));
        return new AgentPrintJobAck { Accepted = true };
    }

    private async Task ReportJobResult(AgentPrintJobResult result)
    {
        // The link may be reconnecting right when the printing ends - give it a moment. Every
        // reconnect brings a new connection object, so look it up again on each attempt.
        for (var attempt = 0; attempt < 15 && !_stop.IsCancellationRequested; attempt++)
        {
            var connection = _connection;
            if (connection is { State: HubConnectionState.Connected })
            {
                try
                {
                    await connection.InvokeAsync(PrintAgentProtocol.ReportPrintJobResultMethod, result);
                    return;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _log.Warning($"{Profile.DisplayName}: reporting job {result.JobGuid:N} failed, retrying: {e.Message}");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        _log.Warning($"{Profile.DisplayName}: result of job {result.JobGuid:N} could not be delivered (offline)");
    }

    private async Task ReportPrinters(bool force)
    {
        var connection = _connection;
        if (connection == null || connection.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            var printers = _scanner.Scan();
            var fingerprint = PrinterScanner.Fingerprint(printers);
            if (!force && fingerprint == _reportedFingerprint)
            {
                return;
            }

            await connection.InvokeAsync(PrintAgentProtocol.ReportPrintersMethod, new AgentPrintersReport
            {
                MachineName = AgentInfo.MachineName,
                AgentVersion = AgentInfo.DisplayVersion,
                OsVersion = AgentInfo.OsVersion,
                Printers = printers
            });

            if (fingerprint != _reportedFingerprint)
            {
                _log.Info($"{Profile.DisplayName}: reported {printers.Count} printer(s)");
            }

            _reportedFingerprint = fingerprint;
            ReportedPrinters = printers.Count;
            Changed?.Invoke(this);
        }
        catch (Exception e)
        {
            _log.Warning($"{Profile.DisplayName}: reporting printers failed: {e.Message}");
        }
    }

    private void RequireLogin(string reason)
    {
        // Forget the dead token, so the next start does not knock on the door with it again.
        Profile.ClearToken();
        _profileStore.Save(Profile);
        SetState(ServiceConnectionState.LoginRequired, reason);

        // Not awaited: this may run inside the connection's own callback, and disposing the
        // connection from there would wait for the very callback that is disposing it.
        _ = Task.Run(Stop);
    }

    private void SetState(ServiceConnectionState state, string? error)
    {
        State = state;
        LastError = error;
        Changed?.Invoke(this);
    }

    public static string Describe(HttpTransportType transport)
    {
        return transport switch
        {
            HttpTransportType.WebSockets => "WebSocket",
            HttpTransportType.ServerSentEvents => "server-sent events",
            HttpTransportType.LongPolling => "long polling",
            _ => transport.ToString()
        };
    }

    private static bool IsUnauthorized(Exception? error)
    {
        return error switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => true,

            // A 401 on the event stream or on a poll comes wrapped per transport.
            AggregateException aggregate => aggregate.InnerExceptions.Any(x => IsUnauthorized(x.InnerException)),
            _ => false
        };
    }

    /// <summary>
    /// The service answered, but the transport or the handshake over it failed - unlike a
    /// service that is down or out of reach (the negotiation fails), which no other transport
    /// would change.
    /// </summary>
    private static bool IsTransportFailure(Exception error)
    {
        return error is
            // "Unable to connect to the server with any of the available transports": negotiated, not started.
            AggregateException or WebSocketException or
            // Started, but the handshake failed, broke off or timed out.
            HubException or IOException or OperationCanceledException or TimeoutException;
    }

    /// <summary>What actually went wrong, rather than SignalR's wrapper around it.</summary>
    private static string Reason(Exception error)
    {
        // One entry per transport; the transports this attempt did not use carry no inner exception.
        if (error is AggregateException aggregate &&
            aggregate.InnerExceptions.FirstOrDefault(x => x.InnerException != null) is { } tried)
        {
            return tried.InnerException!.Message;
        }

        return error.Message;
    }
}
