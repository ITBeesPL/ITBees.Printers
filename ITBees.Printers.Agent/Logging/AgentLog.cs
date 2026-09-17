using System.Text;

namespace ITBees.Printers.Agent.Logging;

/// <summary>
/// Log of the agent: a daily file plus the last lines kept in memory for the status window.
/// Never gets document contents, tokens or registration codes.
/// </summary>
public class AgentLog
{
    private const int MaxLinesInMemory = 500;
    private const int DaysToKeep = 14;

    private readonly object _sync = new();
    private readonly LinkedList<string> _lines = new();

    public event Action<string>? LineAdded;

    public AgentLog()
    {
        RemoveOldFiles();
    }

    public void Info(string message) => Write("INF", message);
    public void Warning(string message) => Write("WRN", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERR", exception == null ? message : $"{message} [{exception.GetType().Name}: {exception.Message}]");

    public IReadOnlyList<string> Snapshot()
    {
        lock (_sync)
        {
            return _lines.ToList();
        }
    }

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";
        lock (_sync)
        {
            _lines.AddLast(line);
            while (_lines.Count > MaxLinesInMemory)
            {
                _lines.RemoveFirst();
            }

            try
            {
                File.AppendAllText(Path.Combine(AgentInfo.LogDirectory, $"agent-{DateTime.Now:yyyyMMdd}.log"),
                    line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // A full disk or a locked file must never stop the printing.
            }
        }

        LineAdded?.Invoke(line);
    }

    private static void RemoveOldFiles()
    {
        try
        {
            foreach (var file in Directory.GetFiles(AgentInfo.LogDirectory, "agent-*.log"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-DaysToKeep))
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
