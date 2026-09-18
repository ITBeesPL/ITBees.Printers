using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ITBees.Printers.Agent;

/// <summary>
/// One agent per Windows user. A second start of the same program (e.g. a desktop shortcut
/// carrying another --site) hands its command line over to the running instance and exits.
///
/// A second start of ANOTHER copy of the agent - another folder, which in practice means a
/// newer build or an update - replaces the running one instead: the copy started last wins.
/// Handing a new build's command line over to an old running copy would silently keep the old
/// code at work (which is exactly how a fixed agent kept opening the wrong page).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Sent to a running copy that is being replaced: close down.</summary>
    public const string QuitArgument = "--quit";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    // Copies that know --quit close within a second or two; older builds ignore it and are ended.
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(5);

    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private bool _ownsMutex;

    public SingleInstance()
    {
        // Scoped by the data directory, so a test run with its own directory stays independent.
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            AgentInfo.DataDirectory.ToLowerInvariant())))[..16];
        _name = $"ITBees.Printers.Agent.{scope}";
        _mutex = new Mutex(true, $@"Local\{_name}", out var createdNew);
        _ownsMutex = createdNew;
    }

    public bool IsFirstInstance => _ownsMutex;

    /// <summary>Executable of the copy this one replaced at start, if any.</summary>
    public string? ReplacedExecutable { get; private set; }

    /// <summary>Raised on a background thread with the command line of a later start.</summary>
    public event Action<string[]>? ArgumentsReceived;

    public void StartListening()
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var payload = await reader.ReadToEndAsync(_stop.Token);
                    ArgumentsReceived?.Invoke(payload.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    // A broken client connection - wait for the next one.
                }
            }
        });
    }

    /// <summary>
    /// For a process that is not the first instance. Returns false when the running instance
    /// takes over the command line (the same program is running - or it cannot be reached) and
    /// this process should leave; true when this process replaced another copy of the agent and
    /// goes on as the one and only instance.
    /// </summary>
    public bool HandOverOrReplace(string[] args)
    {
        NamedPipeClientStream? client = null;
        try
        {
            client = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(ConnectTimeout);

            using var running = GetServerProcess(client);
            var runningExecutable = TryGetExecutable(running);
            if (running == null || runningExecutable == null || IsThisExecutable(runningExecutable))
            {
                Write(client, args);
                return false;
            }

            // Another copy. Copies that know --quit close properly; older builds show their
            // window for a moment and are then ended - their state is on disk anyway.
            Write(client, new[] { QuitArgument });
            client.Dispose();
            client = null;

            if (!WaitForMutex(GracefulExitTimeout))
            {
                try
                {
                    running.Kill();
                    running.WaitForExit((int)KillTimeout.TotalMilliseconds);
                }
                catch (Exception e) when (e is Win32Exception or InvalidOperationException)
                {
                    // Already gone, or not ours to end - the mutex decides below.
                }

                if (!WaitForMutex(KillTimeout))
                {
                    return false;
                }
            }

            ReplacedExecutable = runningExecutable;
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            // Nobody answers - the other instance is starting or closing. Leave it alone.
            return false;
        }
        finally
        {
            client?.Dispose();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private bool WaitForMutex(TimeSpan timeout)
    {
        try
        {
            _ownsMutex = _mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            // The owner was ended without releasing it - ownership passes to us all the same.
            _ownsMutex = true;
        }

        return _ownsMutex;
    }

    private static void Write(Stream client, string[] args)
    {
        using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true);
        writer.Write(string.Join('\n', args));
    }

    /// <summary>The process that serves the pipe - i.e. the instance holding this data directory.</summary>
    private static Process? GetServerProcess(NamedPipeClientStream client)
    {
        if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId))
        {
            return null;
        }

        try
        {
            return Process.GetProcessById((int)processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? TryGetExecutable(Process? process)
    {
        try
        {
            return process?.MainModule?.FileName;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsThisExecutable(string executable)
    {
        return Environment.ProcessPath != null && string.Equals(Path.GetFullPath(executable),
            Path.GetFullPath(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
}
