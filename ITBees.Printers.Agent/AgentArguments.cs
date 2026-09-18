namespace ITBees.Printers.Agent;

/// <summary>
/// Command line of the agent:
///   ITBees.Printers.Agent.exe --site admin.example.com
/// The site is the base address of the web application to log in to - "https://" may be left
/// out, and a sub-path the application is hosted under belongs to it ("example.com/adm"). The
/// connect page (/print-agent/connect) is appended, unless the address already names one.
/// Without arguments the agent simply reconnects to the services it already knows.
/// </summary>
public class AgentArguments
{
    /// <summary>Passed by the previous version to the copy it has just been updated to.</summary>
    public const string UpdatedArgument = "--updated";

    /// <summary>Passed to the previous version when the new one could not be installed.</summary>
    public const string UpdateFailedArgument = "--update-failed";

    public string? SiteUrl { get; private set; }

    /// <summary>Start without showing the status window (used by the autostart entry).</summary>
    public bool Minimized { get; private set; }

    /// <summary>Close down - sent by another copy of the agent taking this one's place (see <see cref="SingleInstance"/>).</summary>
    public bool Quit { get; private set; }

    /// <summary>Started by the previous version right after installing this one (see <see cref="Updates.AgentUpdater"/>).</summary>
    public bool Updated { get; private set; }

    public bool UpdateFailed { get; private set; }

    public static AgentArguments Parse(IReadOnlyList<string> args)
    {
        var result = new AgentArguments();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (IsOption(arg, "site", "s", "url"))
            {
                if (i + 1 < args.Count)
                {
                    result.SiteUrl = args[++i];
                }
            }
            else if (arg.StartsWith("--site=", StringComparison.OrdinalIgnoreCase))
            {
                result.SiteUrl = arg["--site=".Length..];
            }
            else if (IsOption(arg, "minimized", "m"))
            {
                result.Minimized = true;
            }
            else if (string.Equals(arg, SingleInstance.QuitArgument, StringComparison.OrdinalIgnoreCase))
            {
                result.Quit = true;
            }
            else if (string.Equals(arg, UpdatedArgument, StringComparison.OrdinalIgnoreCase))
            {
                result.Updated = true;
            }
            else if (string.Equals(arg, UpdateFailedArgument, StringComparison.OrdinalIgnoreCase))
            {
                result.UpdateFailed = true;
            }
            else if (result.SiteUrl == null && !arg.StartsWith('-') && !arg.StartsWith('/'))
            {
                // A bare address works too: ITBees.Printers.Agent.exe https://admin.example.com
                result.SiteUrl = arg;
            }
        }

        return result;
    }

    private static bool IsOption(string arg, params string[] names)
    {
        return names.Any(name =>
            string.Equals(arg, "--" + name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "-" + name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "/" + name, StringComparison.OrdinalIgnoreCase));
    }
}
