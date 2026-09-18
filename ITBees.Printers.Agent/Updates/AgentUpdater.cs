using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ITBees.Printers.Agent.Logging;

namespace ITBees.Printers.Agent.Updates;

/// <summary>
/// Self-update of a published (single-file) agent. The build publishes latestversion.json next
/// to the exe; on every start the agent compares it with its own version and, when the user
/// agrees, downloads the new exe next to the running one ("*.update.exe").
///
/// The running process never touches its own file: a single-file app loads its assemblies from
/// its exe lazily, so renaming or replacing that file breaks the process mid-way (tried: it could
/// no longer start any process, and neither version was left running). Instead the previous
/// version closes and starts the downloaded file with <see cref="ApplyUpdateArgument"/>; the new
/// version waits for the previous process to end, copies itself over the previous exe (same
/// path, so "start with Windows" stays valid) and starts from there. That copy removes the
/// downloaded file.
/// </summary>
public sealed class AgentUpdater
{
    public const string DefaultManifestUrl = "https://api.itbees.pl/ITBeesFastPrintAgent/latestversion.json";

    /// <summary>
    /// "--apply-update &lt;exe to replace&gt; &lt;id of the process to wait for&gt;" - the contract
    /// between a version and the next one; keep it stable.
    /// </summary>
    public const string ApplyUpdateArgument = "--apply-update";

    private static readonly TimeSpan ManifestTimeout = TimeSpan.FromSeconds(15);

    // HttpClient.Timeout stops covering the body once the headers are in - a download that gets
    // nothing for this long is treated as dead instead.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    // How long the new version waits for the previous process to end and release its exe.
    private static readonly TimeSpan PreviousExitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);
    private const int ReplaceAttempts = 20;
    private const int LeftoverAttempts = 40;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly AgentLog _log;

    public AgentUpdater(AgentLog log)
    {
        _log = log;
    }

    /// <summary>
    /// Only a single-file build can replace itself. A build from bin\ is an exe plus DLLs that no
    /// file swap can update - and its version from the csproj would always look outdated.
    /// </summary>
    public static bool CanUpdateItself => Environment.ProcessPath != null && IsSingleFile;

    /// <summary>The manifest to check, or null when the check is switched off (<see cref="AgentInfo.UpdateUrlVariable"/>=off).</summary>
    public static Uri? ManifestUrl
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable(AgentInfo.UpdateUrlVariable);
            if (string.IsNullOrWhiteSpace(configured))
            {
                return new Uri(DefaultManifestUrl);
            }

            if (configured.Trim().Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return AddressNormalizer.TryNormalize(configured, out var url) ? new Uri(url) : null;
        }
    }

    private static string CurrentExecutable => Environment.ProcessPath!;

#pragma warning disable IL3000 // An empty Location is exactly how a single-file app is recognised.
    private static bool IsSingleFile => string.IsNullOrEmpty(typeof(AgentUpdater).Assembly.Location);
#pragma warning restore IL3000

    /// <summary>"C:\x\Agent.exe" -> "C:\x\Agent.update.exe": the download, started to install itself.</summary>
    private static string DownloadPathFor(string executable) => Path.ChangeExtension(executable, ".update.exe");

    /// <summary>The new exe copied next to the one it replaces, then renamed over it in one step.</summary>
    private static string CopyPathFor(string executable) => executable + ".tmp";

    /// <summary>The published version when it is newer than this one; null otherwise or when the check fails.</summary>
    public async Task<AvailableUpdate?> Check(CancellationToken cancellationToken)
    {
        if (!CanUpdateItself)
        {
            _log.Info("Update check skipped: not a published single-file build");
            return null;
        }

        if (ManifestUrl is not { } manifestUrl)
        {
            _log.Info($"Update check switched off ({AgentInfo.UpdateUrlVariable})");
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ManifestTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, manifestUrl);
            // A proxy or CDN on the way must not keep serving yesterday's version.
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await HttpClient.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();

            // ReadAsStringAsync drops a byte order mark, which JsonSerializer would choke on.
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);

            if (!TryParseVersion(manifest?.Version, out var published))
            {
                _log.Warning($"Update check: {manifestUrl} has no valid version");
                return null;
            }

            if (!TryParseVersion(AgentInfo.Version, out var current) || published <= current)
            {
                _log.Info($"Update check: {AgentInfo.Version} is up to date (published: {manifest!.Version})");
                return null;
            }

            if (ResolveDownloadUrl(manifestUrl, manifest!) is not { } downloadUrl)
            {
                _log.Warning($"Update check: {manifestUrl} names no https address of the exe");
                return null;
            }

            _log.Info($"Update available: {manifest!.Version} (running {AgentInfo.Version})");
            return new AvailableUpdate(manifest.Version!.Trim(), downloadUrl, manifest.Size, manifest.Sha256?.Trim());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            // No network, the server is down, a broken manifest - try again on the next start.
            _log.Warning($"Update check failed ({manifestUrl}): {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the new exe next to the running one (a folder without write access shows up
    /// before anything is changed) and checks it against the manifest. Nothing is left behind on
    /// failure.
    /// </summary>
    public async Task<DownloadedUpdate> Download(AvailableUpdate update, IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        var target = DownloadPathFor(CurrentExecutable);
        _log.Info($"Downloading update {update.Version} from {update.DownloadUrl}");
        try
        {
            using var response = await HttpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var header = new byte[2];
            long received = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920,
                             useAsync: true))
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var buffer = new byte[81920];
                while (true)
                {
                    stall.CancelAfter(StallTimeout);
                    int read;
                    try
                    {
                        read = await source.ReadAsync(buffer, stall.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new UpdateException(
                            $"Serwer przestał przesyłać plik (brak danych od {StallTimeout.TotalSeconds:0} s).");
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    if (received < header.Length)
                    {
                        Array.Copy(buffer, 0, header, (int)received,
                            (int)Math.Min(header.Length - received, read));
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    progress.Report(new DownloadProgress(received, total));
                }
            }

            // An error page served with 200, a truncated or a tampered-with download.
            if (header[0] != 'M' || header[1] != 'Z')
            {
                throw new UpdateException("Pobrany plik nie jest programem Windows.");
            }

            if (update.Size is > 0 && received != update.Size)
            {
                throw new UpdateException($"Pobrany plik ma {received} B zamiast {update.Size} B.");
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.IsNullOrEmpty(update.Sha256) && !sha256.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("Suma kontrolna (SHA-256) pobranego pliku się nie zgadza.");
            }

            _log.Info($"Update {update.Version} downloaded to {target} ({received} B, SHA-256 {sha256.ToLowerInvariant()})");
            return new DownloadedUpdate(target, update.Version);
        }
        catch (Exception e)
        {
            TryDelete(target);
            if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                _log.Info($"Download of update {update.Version} cancelled");
            }
            else
            {
                _log.Error($"Download of update {update.Version} failed", e);
            }

            throw;
        }
    }

    /// <summary>
    /// The previous version's last step, after it has released the single-instance lock: starts
    /// the downloaded exe to install itself. If that fails, this exe is started again instead.
    /// </summary>
    public static void Launch(DownloadedUpdate update, AgentLog log)
    {
        try
        {
            Start(update.File, ApplyUpdateArgument, CurrentExecutable,
                Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            log.Info($"Started {update.File} to install update {update.Version}");
        }
        catch (Exception e)
        {
            log.Error($"Could not start {update.File}", e);
            StartAfterFailure(CurrentExecutable, log);
        }
    }

    /// <summary>
    /// The new version, started from the downloaded file by <see cref="Launch"/>: waits for the
    /// previous process to end, puts a copy of itself in place of the previous exe and starts it.
    /// Returns false when the command line is not an update to apply (a normal start).
    /// </summary>
    public static bool TryApply(string[] args)
    {
        if (args.Length < 3 || !args[0].Equals(ApplyUpdateArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = Path.GetFullPath(args[1]);
        var log = new AgentLog();
        var copy = CopyPathFor(target);
        try
        {
            // Only ever an existing exe - never a path someone made up.
            if (!File.Exists(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{target} is not an existing exe");
            }

            WaitForExit(args[2]);

            // Copy first, then one rename over the old exe: an interruption leaves either the
            // old exe or the new one, never half a file.
            File.Copy(CurrentExecutable, copy, overwrite: true);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(copy, target, overwrite: true);
                    break;
                }
                catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < ReplaceAttempts)
                {
                    // The previous process may still hold its exe for a moment.
                    Thread.Sleep(RetryDelay);
                }
            }

            log.Info($"Update {AgentInfo.Version} installed in {target}");
            Start(target, "--minimized", AgentArguments.UpdatedArgument);
        }
        catch (Exception e)
        {
            log.Error($"Installing update {AgentInfo.Version} in {target} failed", e);
            TryDelete(copy);
            StartAfterFailure(target, log);
        }

        return true;
    }

    /// <summary>
    /// Deletes what an update leaves behind - the downloaded exe (as soon as the process that
    /// installed it has ended) and an interrupted copy. Runs in the background.
    /// </summary>
    public void RemoveLeftovers()
    {
        if (!CanUpdateItself)
        {
            return;
        }

        var leftovers = new[] { DownloadPathFor(CurrentExecutable), CopyPathFor(CurrentExecutable) }
            .Where(File.Exists)
            .ToList();
        if (leftovers.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < LeftoverAttempts && leftovers.Count > 0; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(RetryDelay);
                }

                leftovers.RemoveAll(file =>
                {
                    if (!TryDelete(file))
                    {
                        return false;
                    }

                    _log.Info($"Removed {file} left by an update");
                    return true;
                });
            }

            foreach (var file in leftovers)
            {
                _log.Warning($"Could not remove {file} left by an update");
            }
        });
    }

    public static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>"1.2.3-beta+abc" -> 1.2.3.0; missing parts count as 0, so "1.2" equals "1.2.0".</summary>
    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version();
        var core = (text ?? string.Empty).Trim().Split('+', '-')[0];
        if (!Version.TryParse(core, out var parsed))
        {
            return false;
        }

        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }

    /// <summary>
    /// The exe is expected next to the manifest unless the manifest names another address. The
    /// downloaded file is started without asking again, so plain http is only accepted for a test
    /// server on this computer.
    /// </summary>
    private static Uri? ResolveDownloadUrl(Uri manifestUrl, UpdateManifest manifest)
    {
        var address = !string.IsNullOrWhiteSpace(manifest.DownloadUrl) ? manifest.DownloadUrl : manifest.FileName;
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(manifestUrl, address.Trim(), out var url))
        {
            return null;
        }

        return url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback) ? url : null;
    }

    private static void WaitForExit(string processId)
    {
        if (!int.TryParse(processId, out var id))
        {
            return;
        }

        try
        {
            using var previous = Process.GetProcessById(id);
            previous.WaitForExit(PreviousExitTimeout);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
    }

    /// <summary>Whatever went wrong, the user is not left without a running agent.</summary>
    private static void StartAfterFailure(string executable, AgentLog log)
    {
        try
        {
            Start(executable, "--minimized", AgentArguments.UpdateFailedArgument);
        }
        catch (Exception e)
        {
            log.Error($"Could not start {executable} again", e);
        }
    }

    private static void Start(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo)?.Dispose();
    }

    private static HttpClient CreateHttpClient()
    {
        // Timeouts are per call: 15 s for the manifest, "no data for 30 s" for the download.
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ITBeesPrintAgent/{AgentInfo.Version}");
        return client;
    }
}
