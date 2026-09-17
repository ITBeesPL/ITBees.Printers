using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace ITBees.Printers.Agent;

/// <summary>
/// One agent per Windows user. A second start (e.g. a desktop shortcut carrying another
/// --site) hands its command line over to the running instance and exits.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly string _name;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    public SingleInstance()
    {
        // Scoped by the data directory, so a test run with its own directory stays independent.
        var scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            AgentInfo.DataDirectory.ToLowerInvariant())))[..16];
        _name = $"ITBees.Printers.Agent.{scope}";
        _mutex = new Mutex(true, $@"Local\{_name}", out var createdNew);
        IsFirstInstance = createdNew;
    }

    public bool IsFirstInstance { get; }

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

    public bool SendToFirstInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(string.Join('\n', args));
            return true;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (IsFirstInstance)
        {
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }
}
