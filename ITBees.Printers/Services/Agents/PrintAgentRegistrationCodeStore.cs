using System.Collections.Concurrent;
using ITBees.Printers.Services.Security;

namespace ITBees.Printers.Services.Agents;

/// <summary>
/// One-time codes of the browser login flow. A code is issued to the logged-in user on the
/// connect page, travels to the agent through its loopback callback and is exchanged for the
/// agent token on the agent listener. Codes live a few minutes and only in memory - after an
/// API restart the user simply starts the pairing again.
/// </summary>
public class PrintAgentRegistrationCodeStore
{
    private readonly ConcurrentDictionary<string, Entry> _codes = new();
    private readonly PrintersSettings _settings;
    private readonly Func<DateTime> _utcNow;

    public PrintAgentRegistrationCodeStore(PrintersSettings settings) : this(settings, () => DateTime.UtcNow)
    {
    }

    public PrintAgentRegistrationCodeStore(PrintersSettings settings, Func<DateTime> utcNow)
    {
        _settings = settings;
        _utcNow = utcNow;
    }

    public string Issue(Guid userAccountGuid)
    {
        RemoveExpired();

        var code = PrintAgentToken.CreateOneTimeCode();
        _codes[code] = new Entry(userAccountGuid, _utcNow().Add(_settings.RegistrationCodeLifetime));
        return code;
    }

    /// <summary>Spends the code. False for an unknown, already used or expired one.</summary>
    public bool TryConsume(string? code, out Guid userAccountGuid)
    {
        userAccountGuid = Guid.Empty;
        if (string.IsNullOrWhiteSpace(code) || !_codes.TryRemove(code, out var entry))
        {
            return false;
        }

        if (entry.ExpiresUtc < _utcNow())
        {
            return false;
        }

        userAccountGuid = entry.UserAccountGuid;
        return true;
    }

    private void RemoveExpired()
    {
        var now = _utcNow();
        foreach (var pair in _codes)
        {
            if (pair.Value.ExpiresUtc < now)
            {
                _codes.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record Entry(Guid UserAccountGuid, DateTime ExpiresUtc);
}
