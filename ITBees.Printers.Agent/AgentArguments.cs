namespace ITBees.Printers.Agent;

/// <summary>
/// Command line of the agent:
///   ITBees.Printers.Agent.exe --site https://admin.example.com
/// The site is the web application to log in to. The address may carry a path when the
/// application serves its "connect print agent" page somewhere else than the default
/// /print-agent/connect. Without arguments the agent simply reconnects to the services it
/// already knows.
/// </summary>
public class AgentArguments
{
    public string? SiteUrl { get; private set; }

    /// <summary>Start without showing the status window (used by the autostart entry).</summary>
    public bool Minimized { get; private set; }

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
