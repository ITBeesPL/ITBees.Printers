using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using ITBees.Printers.Agent.Logging;
using ITBees.Printers.Protocol;

namespace ITBees.Printers.Agent.Login;

public record LoginResult(string HubUrl, string ServiceName, Guid AgentGuid, string Token);

public class LoginCancelledException : Exception
{
    public LoginCancelledException() : base("Logowanie zostało anulowane w przeglądarce.")
    {
    }
}

/// <summary>
/// Logs the agent in through the user's browser - the way command line tools do it:
///  1. a one-shot listener is opened on a free loopback port,
///  2. the default browser is sent to the application's "connect print agent" page with that
///     port and a random state; the user logs in there as usual and confirms,
///  3. the page redirects the browser to http://127.0.0.1:{port}/callback with a one-time code
///     and the address of the service's agent listener,
///  4. the code is exchanged there for the agent's own long-lived token.
/// The user's password and session never pass through the agent.
/// </summary>
public class BrowserLoginFlow
{
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PageCheckTimeout = TimeSpan.FromSeconds(10);

    // Redirects are followed by hand: the automatic ones turn a POST into a GET, which is
    // exactly what an http -> https redirect in front of the registration endpoint would hit.
    private static readonly HttpClient HttpClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly AgentLog _log;

    public BrowserLoginFlow(AgentLog log)
    {
        _log = log;
    }

    /// <param name="siteUrl">Already normalized - see <see cref="AddressNormalizer"/>.</param>
    public async Task<LoginResult> Run(string siteUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(LoginTimeout);

        // Port 0: the system picks a free one. Loopback only - nothing is exposed to the network.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var state = CreateState();
            var connectPageUrl = BuildConnectPageUrl(siteUrl, port, state);

            await EnsureConnectPageExists(siteUrl, connectPageUrl, timeout.Token);

            if (AgentInfo.DoNotOpenBrowser)
            {
                // Diagnostics: somebody else (a test, a remote session) opens the address.
                _log.Info($"Log in by opening: {connectPageUrl}");
            }
            else
            {
                _log.Info($"Opening the browser to log in to {siteUrl} (callback port {port})");
                Process.Start(new ProcessStartInfo(connectPageUrl) { UseShellExecute = true });
            }

            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                var result = await HandleConnection(client, state, timeout.Token);
                if (result != null)
                {
                    return result;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Nie dokończono logowania w przeglądarce w ciągu 10 minut.");
        }
        finally
        {
            listener.Stop();
        }
    }

    public static string BuildConnectPageUrl(string siteUrl, int port, string state)
    {
        if (!AddressNormalizer.TryNormalize(siteUrl, out var normalized))
        {
            throw new ArgumentException($"„{siteUrl}” nie jest poprawnym adresem serwisu.");
        }

        var builder = new UriBuilder(normalized);
        if (string.IsNullOrEmpty(builder.Path) || builder.Path == "/")
        {
            builder.Path = PrintAgentProtocol.DefaultConnectPagePath;
        }

        var query = HttpUtility.ParseQueryString(builder.Query);
        query[PrintAgentProtocol.PortParameter] = port.ToString();
        query[PrintAgentProtocol.StateParameter] = state;
        query[PrintAgentProtocol.MachineParameter] = AgentInfo.MachineName;
        builder.Query = query.ToString();
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>
    /// Fails fast - before a browser tab with an error page pops up and the agent waits for a
    /// confirmation that can never come - when the address is dead or is not a web panel at all
    /// (the classic: the address of the API instead of the address of the panel).
    /// </summary>
    private async Task EnsureConnectPageExists(string siteUrl, string connectPageUrl,
        CancellationToken cancellationToken)
    {
        using var pageTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        pageTimeout.CancelAfter(PageCheckTimeout);
        try
        {
            using var response = await HttpClient.GetAsync(connectPageUrl, HttpCompletionOption.ResponseHeadersRead,
                pageTimeout.Token);
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                return; // Anything else (a login redirect included) is for the browser to deal with.
            }

            // Some static hostings answer 404 and still serve the single-page application -
            // only a 404 that is not an application shell means "no such page".
            var body = await response.Content.ReadAsStringAsync(pageTimeout.Token);
            if (response.Content.Headers.ContentType?.MediaType == "text/html" &&
                body.Contains("<script", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException($"Nie można połączyć się z {siteUrl}: {e.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"Serwis {siteUrl} nie odpowiada.");
        }

        throw new InvalidOperationException(
            $"Pod adresem {siteUrl} nie ma strony logowania aplikacji drukującej. Podaj adres panelu WWW, " +
            "w którym się logujesz (np. https://admin.example.com) - nie adres API.");
    }

    /// <summary>Null - not the callback we are waiting for (favicon, a stray or forged request); keep listening.</summary>
    private async Task<LoginResult?> HandleConnection(TcpClient client, string expectedState,
        CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        var target = await ReadRequestTarget(stream, cancellationToken);
        if (target == null)
        {
            return null;
        }

        var separator = target.IndexOf('?');
        var path = separator < 0 ? target : target[..separator];
        var query = HttpUtility.ParseQueryString(separator < 0 ? string.Empty : target[(separator + 1)..]);

        if (!string.Equals(path, PrintAgentProtocol.CallbackPath, StringComparison.OrdinalIgnoreCase))
        {
            await Respond(stream, "404 Not Found", Page("Nie znaleziono", "Ta strona nie istnieje.", false));
            return null;
        }

        // The state proves that the callback answers the login this agent has started.
        if (!FixedTimeEquals(query[PrintAgentProtocol.StateParameter], expectedState))
        {
            _log.Warning("Ignored a login callback with a wrong state value");
            await Respond(stream, "400 Bad Request", Page("Nieprawidłowe wywołanie",
                "To wywołanie nie pochodzi z logowania rozpoczętego przez aplikację drukującą.", false));
            return null;
        }

        if (!string.IsNullOrEmpty(query[PrintAgentProtocol.ErrorParameter]))
        {
            await Respond(stream, "200 OK", Page("Anulowano",
                "Aplikacja drukująca nie została połączona. Możesz zamknąć tę kartę.", false));
            throw new LoginCancelledException();
        }

        try
        {
            var result = await Register(query[PrintAgentProtocol.CodeParameter],
                query[PrintAgentProtocol.HubUrlParameter], query[PrintAgentProtocol.ServiceNameParameter],
                cancellationToken);
            await Respond(stream, "200 OK", Page("Połączono",
                $"Aplikacja drukująca na komputerze {AgentInfo.MachineName} jest połączona z serwisem " +
                $"„{result.ServiceName}”. Możesz zamknąć tę kartę i wybrać drukarkę w ustawieniach drukowania.", true));
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await Respond(stream, "200 OK", Page("Nie udało się połączyć", e.Message, false));
            throw;
        }
    }

    private async Task<LoginResult> Register(string? code, string? hubUrl, string? serviceName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(hubUrl))
        {
            throw new InvalidOperationException("Serwis nie przekazał danych potrzebnych do połączenia.");
        }

        // Tolerant on purpose: a service configured with a bare "api.example.com" still works.
        if (!AddressNormalizer.TryNormalize(hubUrl, out var baseUrl))
        {
            throw new InvalidOperationException(
                $"Serwis podał nieprawidłowy adres dla aplikacji drukującej: „{hubUrl}”.");
        }

        var request = new AgentRegistrationRequest
        {
            Code = code,
            MachineName = AgentInfo.MachineName,
            AgentVersion = AgentInfo.Version,
            OsVersion = AgentInfo.OsVersion
        };

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.PostAsJsonAsync(baseUrl + PrintAgentProtocol.RegisterPath, request,
                cancellationToken);

            // A proxy upgrading http to https: repeat the POST there, and talk https from now on.
            if (IsRedirect(response.StatusCode) &&
                TryGetSecureTwin(baseUrl, response.Headers.Location, out var secureBaseUrl))
            {
                response.Dispose();
                baseUrl = secureBaseUrl;
                response = await HttpClient.PostAsJsonAsync(baseUrl + PrintAgentProtocol.RegisterPath, request,
                    cancellationToken);
            }
        }
        catch (HttpRequestException e)
        {
            throw new InvalidOperationException(
                $"Nie można połączyć się z adresem aplikacji drukującej serwisu ({baseUrl}): {e.Message}");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                // The one-time code is spent on nothing, but the message is worth it: this is a
                // deployment problem of the service, not something the user did wrong.
                throw new InvalidOperationException(
                    $"Serwis nie udostępnia aplikacjom drukującym adresu {baseUrl} (HTTP {(int)response.StatusCode}). " +
                    "Administrator serwisu musi wystawić końcówki /print-agent/ pod adresem z ustawienia PublicAgentUrl.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await TryRead<AgentErrorResponse>(response, cancellationToken);
                throw new InvalidOperationException(
                    $"Serwis odrzucił połączenie ({(int)response.StatusCode}): {error?.Message ?? response.ReasonPhrase}");
            }

            var registration = await TryRead<AgentRegistrationResponse>(response, cancellationToken);
            if (registration == null || string.IsNullOrEmpty(registration.Token))
            {
                throw new InvalidOperationException("Serwis nie zwrócił tokenu aplikacji drukującej.");
            }

            var hub = new Uri(baseUrl);
            if (hub.Scheme == Uri.UriSchemeHttp && !hub.IsLoopback)
            {
                _log.Warning($"The agent address {baseUrl} is not encrypted (http)");
            }

            var name = string.IsNullOrWhiteSpace(registration.ServiceName) ? serviceName : registration.ServiceName;
            return new LoginResult(baseUrl, string.IsNullOrWhiteSpace(name) ? hub.Host : name!,
                registration.AgentGuid, registration.Token);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or
            HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
    }

    /// <summary>Only the same host over https is followed - never a redirect to somewhere else.</summary>
    private static bool TryGetSecureTwin(string baseUrl, Uri? location, out string secureBaseUrl)
    {
        secureBaseUrl = string.Empty;
        var current = new Uri(baseUrl);
        if (location == null || !location.IsAbsoluteUri || location.Scheme != Uri.UriSchemeHttps ||
            current.Scheme != Uri.UriSchemeHttp ||
            !string.Equals(location.Host, current.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var builder = new UriBuilder(current) { Scheme = Uri.UriSchemeHttps, Port = location.Port };
        secureBaseUrl = builder.Uri.AbsoluteUri.TrimEnd('/');
        return true;
    }

    private static async Task<T?> TryRead<T>(HttpResponseMessage response, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The request target ("/callback?state=...") of a GET, or null for anything else.</summary>
    private static async Task<string?> ReadRequestTarget(NetworkStream stream, CancellationToken cancellationToken)
    {
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(RequestReadTimeout);

        var buffer = new byte[16 * 1024];
        var length = 0;
        try
        {
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), readTimeout.Token);
                if (read == 0)
                {
                    break;
                }

                length += read;
                if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n"))
                {
                    break;
                }
            }
        }
        catch (Exception e) when (e is IOException ||
                                  (e is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // A speculative browser connection that never sent anything.
            return null;
        }

        var requestLine = Encoding.ASCII.GetString(buffer, 0, length).Split("\r\n")[0].Split(' ');
        return requestLine.Length >= 2 && requestLine[0] == "GET" ? requestLine[1] : null;
    }

    private static async Task Respond(NetworkStream stream, string status, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\n" +
            "Cache-Control: no-store\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(head);
            await stream.WriteAsync(body);
            await stream.FlushAsync();
        }
        catch (IOException)
        {
            // The browser tab is already gone - nothing to tell it.
        }
    }

    private static string Page(string title, string message, bool success)
    {
        var color = success ? "#16a34a" : "#dc2626";
        return $$"""
            <!doctype html><html lang="pl"><head><meta charset="utf-8"><title>{{AgentInfo.ProductName}}</title>
            <style>body{font:16px/1.5 system-ui,sans-serif;display:grid;place-items:center;min-height:90vh;margin:0;
            background:#f6f7f9;color:#1f2937}main{background:#fff;border-radius:14px;padding:32px 40px;max-width:480px;
            box-shadow:0 8px 30px #0001;text-align:center}h1{font-size:22px;margin:0 0 10px;color:{{color}}}
            p{margin:0}small{display:block;margin-top:18px;color:#6b7280}</style></head>
            <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p>
            <small>{{AgentInfo.ProductName}} {{AgentInfo.Version}}</small></main></body></html>
            """;
    }

    private static string CreateState()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-')
            .Replace('/', '_');
    }

    private static bool FixedTimeEquals(string? actual, string expected)
    {
        return actual != null && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));
    }
}
