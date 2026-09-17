using ITBees.Interfaces.Repository;
using ITBees.Printers.DbModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Services.Jobs;

/// <summary>
/// Keeps the print job history bounded: once a day deletes jobs older than
/// <see cref="PrintersSettings.PrintJobRetentionDays"/>.
/// </summary>
public class PrintJobCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    // Never compete with the application's startup (migrations, warm-up) for the database.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);

    private readonly IServiceProvider _serviceProvider;
    private readonly PrintersSettings _settings;
    private readonly ILogger<PrintJobCleanupService> _logger;

    public PrintJobCleanupService(IServiceProvider serviceProvider, PrintersSettings settings,
        ILogger<PrintJobCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.PrintJobRetentionDays <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                Prune();
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Application is shutting down.
        }
    }

    private void Prune()
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-_settings.PrintJobRetentionDays);
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IWriteOnlyRepository<PrintJob>>();
            var deleted = repo.DeleteData(x => x.Created < cutoff);
            if (deleted > 0)
            {
                _logger.LogInformation("Pruned {Count} print jobs older than {Days}d", deleted,
                    _settings.PrintJobRetentionDays);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error pruning print jobs: {Message}", e.Message);
        }
    }
}
