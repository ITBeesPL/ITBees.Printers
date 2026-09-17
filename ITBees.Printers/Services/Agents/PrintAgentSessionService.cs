using ITBees.Interfaces.Repository;
using ITBees.Printers.DbModels;
using ITBees.Printers.Protocol;
using ITBees.Printers.Services.Security;
using Microsoft.Extensions.Logging;

namespace ITBees.Printers.Services.Agents;

public class PrintAgentSessionService : IPrintAgentSessionService
{
    private const string UnknownMachineName = "Windows";

    private readonly PrintAgentRegistrationCodeStore _codeStore;
    private readonly IReadOnlyRepository<PrintAgent> _agentRoRepo;
    private readonly IWriteOnlyRepository<PrintAgent> _agentWoRepo;
    private readonly IReadOnlyRepository<PrintAgentPrinter> _printerRoRepo;
    private readonly IWriteOnlyRepository<PrintAgentPrinter> _printerWoRepo;
    private readonly IWriteOnlyRepository<PrintJob> _jobWoRepo;
    private readonly PrintersSettings _settings;
    private readonly ILogger<PrintAgentSessionService> _logger;

    public PrintAgentSessionService(
        PrintAgentRegistrationCodeStore codeStore,
        IReadOnlyRepository<PrintAgent> agentRoRepo,
        IWriteOnlyRepository<PrintAgent> agentWoRepo,
        IReadOnlyRepository<PrintAgentPrinter> printerRoRepo,
        IWriteOnlyRepository<PrintAgentPrinter> printerWoRepo,
        IWriteOnlyRepository<PrintJob> jobWoRepo,
        PrintersSettings settings,
        ILogger<PrintAgentSessionService> logger)
    {
        _codeStore = codeStore;
        _agentRoRepo = agentRoRepo;
        _agentWoRepo = agentWoRepo;
        _printerRoRepo = printerRoRepo;
        _printerWoRepo = printerWoRepo;
        _jobWoRepo = jobWoRepo;
        _settings = settings;
        _logger = logger;
    }

    public AgentRegistrationResponse Register(AgentRegistrationRequest request)
    {
        if (request == null || !_codeStore.TryConsume(request.Code, out var userAccountGuid))
        {
            throw new PrintAgentRegistrationException(
                "The registration code is invalid or has expired. Start the connection again.");
        }

        var machineName = Limit(request.MachineName, 200);
        if (machineName.Length == 0)
        {
            machineName = UnknownMachineName;
        }

        var now = DateTime.UtcNow;

        // Pairing the same computer again (reinstall, lost profile) takes over the existing
        // agent instead of adding a twin: its printers and the settings using them survive,
        // while the old token stops working.
        var existing = _agentRoRepo
            .GetData(x => x.UserAccountGuid == userAccountGuid && x.MachineName == machineName)
            .OrderByDescending(x => x.Created)
            .FirstOrDefault();

        var agentGuid = existing?.Guid ?? Guid.NewGuid();
        var token = PrintAgentToken.Create(agentGuid, out var tokenHash);

        if (existing != null)
        {
            _agentWoRepo.UpdateData(x => x.Guid == agentGuid, x =>
            {
                x.TokenHash = tokenHash;
                x.AgentVersion = LimitOrNull(request.AgentVersion, 50);
                x.OsVersion = LimitOrNull(request.OsVersion, 200);
                x.LastSeen = now;
            });
        }
        else
        {
            _agentWoRepo.InsertData(new PrintAgent
            {
                Guid = agentGuid,
                UserAccountGuid = userAccountGuid,
                MachineName = machineName,
                AgentVersion = LimitOrNull(request.AgentVersion, 50),
                OsVersion = LimitOrNull(request.OsVersion, 200),
                TokenHash = tokenHash,
                Created = now,
                LastSeen = now
            });
        }

        _logger.LogInformation("Print agent {AgentGuid} ({MachineName}) registered for user {UserAccountGuid}",
            agentGuid, machineName, userAccountGuid);

        return new AgentRegistrationResponse
        {
            AgentGuid = agentGuid,
            Token = token,
            ServiceName = _settings.ServiceName
        };
    }

    public PrintAgentIdentity? Authenticate(string? token)
    {
        if (!PrintAgentToken.TryParse(token, out var agentGuid, out var secret))
        {
            return null;
        }

        var agent = _agentRoRepo.GetData(x => x.Guid == agentGuid).FirstOrDefault();
        if (agent == null || !PrintAgentToken.Matches(secret, agent.TokenHash))
        {
            return null;
        }

        return new PrintAgentIdentity(agent.Guid, agent.UserAccountGuid, agent.MachineName);
    }

    public void AgentConnected(Guid agentGuid)
    {
        var now = DateTime.UtcNow;
        _agentWoRepo.UpdateData(x => x.Guid == agentGuid, x =>
        {
            x.LastConnected = now;
            x.LastSeen = now;
        });
    }

    public void AgentDisconnected(Guid agentGuid)
    {
        var now = DateTime.UtcNow;
        _agentWoRepo.UpdateData(x => x.Guid == agentGuid, x => x.LastSeen = now);
    }

    public void SavePrinters(Guid agentGuid, AgentPrintersReport report)
    {
        if (report == null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        // One entry per system name; Windows compares printer names case-insensitively.
        var reported = (report.Printers ?? new List<AgentPrinterInfo>())
            .Where(x => !string.IsNullOrWhiteSpace(x?.Name))
            .Select(x => new { Info = x, Name = Limit(x.Name, PrintAgentPrinter.MaxNameLength) })
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Info, StringComparer.OrdinalIgnoreCase);

        var known = _printerRoRepo.GetData(x => x.PrintAgentGuid == agentGuid).ToList();
        var knownNames = new HashSet<string>(known.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var printer in known)
        {
            var printerGuid = printer.Guid;
            if (reported.TryGetValue(printer.Name, out var info))
            {
                _printerWoRepo.UpdateData(x => x.Guid == printerGuid, x =>
                {
                    Apply(x, info);
                    x.IsAvailable = true;
                    x.LastReported = now;
                });
            }
            else if (printer.IsAvailable)
            {
                _printerWoRepo.UpdateData(x => x.Guid == printerGuid, x => x.IsAvailable = false);
            }
        }

        var added = reported
            .Where(x => !knownNames.Contains(x.Key))
            .Select(x =>
            {
                var printer = new PrintAgentPrinter
                {
                    Guid = Guid.NewGuid(),
                    PrintAgentGuid = agentGuid,
                    Name = x.Key,
                    IsAvailable = true,
                    FirstReported = now,
                    LastReported = now
                };
                Apply(printer, x.Value);
                return printer;
            })
            .ToList();
        if (added.Count > 0)
        {
            _printerWoRepo.InsertData(added);
        }

        _agentWoRepo.UpdateData(x => x.Guid == agentGuid, x =>
        {
            x.AgentVersion = LimitOrNull(report.AgentVersion, 50) ?? x.AgentVersion;
            x.OsVersion = LimitOrNull(report.OsVersion, 200) ?? x.OsVersion;
            x.LastSeen = now;
        });
    }

    public void SaveJobResult(Guid agentGuid, AgentPrintJobResult result)
    {
        if (result == null)
        {
            return;
        }

        var now = DateTime.UtcNow;

        // An agent may only finish jobs that were sent to it, and only once. A job whose
        // confirmation got lost on the way is stored as AgentOffline - if the agent printed it
        // after all, its report is the truth.
        var updated = _jobWoRepo.UpdateData(
            x => x.Guid == result.JobGuid && x.PrintAgentGuid == agentGuid &&
                 (x.Status == PrintJobStatus.SentToAgent || x.Status == PrintJobStatus.AgentOffline),
            x =>
            {
                x.Status = result.Success ? PrintJobStatus.Printed : PrintJobStatus.Failed;
                x.StatusMessage = LimitOrNull(result.Message, PrintJob.MaxStatusMessageLength);
                x.PagesPrinted = result.PagesPrinted;
                x.Completed = now;
            });

        if (updated.Count == 0)
        {
            _logger.LogWarning("Print agent {AgentGuid} reported a result for unknown job {JobGuid}",
                agentGuid, result.JobGuid);
            return;
        }

        if (!result.Success)
        {
            _logger.LogWarning("Print job {JobGuid} failed on agent {AgentGuid}: {Message}",
                result.JobGuid, agentGuid, result.Message);
        }

        _agentWoRepo.UpdateData(x => x.Guid == agentGuid, x => x.LastSeen = now);
    }

    private static void Apply(PrintAgentPrinter printer, AgentPrinterInfo info)
    {
        printer.IsDefault = info.IsDefault;
        printer.DriverName = LimitOrNull(info.DriverName, 260);
        printer.PortName = LimitOrNull(info.PortName, 260);
        printer.Location = LimitOrNull(info.Location, 260);
        printer.IsNetwork = info.IsNetwork;
        printer.IsOffline = info.IsOffline;
        printer.Status = LimitOrNull(info.Status, 100);
    }

    private static string Limit(string? value, int maxLength)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? LimitOrNull(string? value, int maxLength)
    {
        var limited = Limit(value, maxLength);
        return limited.Length == 0 ? null : limited;
    }
}
