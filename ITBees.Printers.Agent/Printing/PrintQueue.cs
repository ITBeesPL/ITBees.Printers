using System.Threading.Channels;
using ITBees.Printers.Agent.Logging;
using ITBees.Printers.Protocol;

namespace ITBees.Printers.Agent.Printing;

public record QueuedPrintJob(AgentPrintJob Job, string ServiceName, Func<AgentPrintJobResult, Task> ReportResult);

/// <summary>
/// Jobs of all connected services are printed one after another on a single worker - the
/// order of arrival is the order on paper, and rendering never runs on the UI thread.
/// </summary>
public class PrintQueue
{
    private readonly Channel<QueuedPrintJob> _channel = Channel.CreateUnbounded<QueuedPrintJob>();
    private readonly PdfPrinter _printer;
    private readonly AgentLog _log;

    public event Action<QueuedPrintJob, AgentPrintJobResult>? JobFinished;

    public PrintQueue(PdfPrinter printer, AgentLog log)
    {
        _printer = printer;
        _log = log;
    }

    public void Start(CancellationToken cancellationToken)
    {
        // A dedicated thread: printing blocks (WinRT rendering, the spooler) for seconds.
        var worker = new Thread(() => Work(cancellationToken)) { IsBackground = true, Name = "PrintQueue" };
        worker.Start();
    }

    public void Enqueue(QueuedPrintJob job)
    {
        _channel.Writer.TryWrite(job);
    }

    private void Work(CancellationToken cancellationToken)
    {
        try
        {
            while (_channel.Reader.WaitToReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult())
            {
                while (_channel.Reader.TryRead(out var queued))
                {
                    Process(queued);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The application is closing.
        }
    }

    private void Process(QueuedPrintJob queued)
    {
        var job = queued.Job;
        var result = new AgentPrintJobResult { JobGuid = job.JobGuid };
        try
        {
            _log.Info($"Printing \"{job.DocumentName}\" on \"{job.PrinterName}\" ({queued.ServiceName}, job {job.JobGuid:N})");
            var content = Convert.FromBase64String(job.ContentBase64);
            result.PagesPrinted = _printer.Print(content, job.PrinterName, job.DocumentName, job.Copies);
            result.Success = true;
            _log.Info($"Job {job.JobGuid:N} sent to the spooler: {result.PagesPrinted} page(s)");
        }
        catch (Exception e)
        {
            result.Success = false;
            result.Message = e.Message;
            _log.Error($"Job {job.JobGuid:N} failed", e);
        }

        JobFinished?.Invoke(queued, result);
        try
        {
            queued.ReportResult(result).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            _log.Warning($"Could not report the result of job {job.JobGuid:N}: {e.Message}");
        }
    }
}
