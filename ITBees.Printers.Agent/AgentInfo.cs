using System.Globalization;
using System.Reflection;

namespace ITBees.Printers.Agent;

public static class AgentInfo
{
    public const string ProductName = "ITBees Print Agent";

    /// <summary>Overrides the per-user data directory - lets a test run leave the real profile alone.</summary>
    public const string DataDirectoryVariable = "ITBEES_PRINT_AGENT_DATA_DIR";

    /// <summary>
    /// Diagnostics: when set, jobs for "print to file" printers (Microsoft Print to PDF, XPS)
    /// are written into this directory instead of raising the driver's "Save as" dialog.
    /// </summary>
    public const string PrintToDirectoryVariable = "ITBEES_PRINT_AGENT_PRINT_TO_DIR";

    /// <summary>
    /// Diagnostics: when set to "1", the login address is written to the log instead of being
    /// opened in the default browser.
    /// </summary>
    public const string NoBrowserVariable = "ITBEES_PRINT_AGENT_NO_BROWSER";

    public static bool DoNotOpenBrowser => Environment.GetEnvironmentVariable(NoBrowserVariable) == "1";

    /// <summary>
    /// Diagnostics: when set to "1", jobs are rendered but never handed to the Windows spooler -
    /// no paper, no print queue entry, and Windows does not make the printer the "last used"
    /// (default) one. With <see cref="PrintToDirectoryVariable"/> set, the rendered pages are
    /// saved there as PNG files.
    /// </summary>
    public const string DryRunVariable = "ITBEES_PRINT_AGENT_DRY_RUN";

    public static bool DryRun => Environment.GetEnvironmentVariable(DryRunVariable) == "1";

    /// <summary>
    /// Diagnostics: when set to "1", the SignalR client's own log (negotiation, transports,
    /// handshake) goes into the log file - enough to see what a proxy on the way does to the
    /// connection. File only, never the status window; tokens and documents are not in it.
    /// </summary>
    public const string TraceVariable = "ITBEES_PRINT_AGENT_TRACE";

    public static bool Trace => Environment.GetEnvironmentVariable(TraceVariable) == "1";

    /// <summary>
    /// Diagnostics: "WebSockets", "ServerSentEvents" and/or "LongPolling" (comma separated) -
    /// the only transports the agent may use, instead of trying them one after another.
    /// </summary>
    public const string TransportsVariable = "ITBEES_PRINT_AGENT_TRANSPORTS";

    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0").Split('+')[0];

    /// <summary>When this build was made (the BuildTimestamp metadata of the csproj), in UTC.</summary>
    public static DateTime? BuildTimeUtc { get; } = ReadBuildTime();

    /// <summary>
    /// "1.0.0 (build 2026-09-18 17:58)" - the build time tells apart builds of one version number.
    /// Shown in the window title, the tray menu and the log, and reported to the services (their
    /// print settings page shows which build a computer runs).
    /// </summary>
    public static string DisplayVersion =>
        BuildTimeUtc is { } built ? $"{Version} (build {built.ToLocalTime():yyyy-MM-dd HH:mm})" : Version;

    public static string MachineName => Environment.MachineName;

    public static string OsVersion => Environment.OSVersion.VersionString;

    /// <summary>Per-user settings (connected services, tokens).</summary>
    public static string DataDirectory { get; } = Directory.CreateDirectory(
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ITBees",
                "PrintAgent")).FullName;

    public static string LogDirectory { get; } = Directory.CreateDirectory(
        Environment.GetEnvironmentVariable(DataDirectoryVariable) is { Length: > 0 } overridden
            ? Path.Combine(overridden, "logs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ITBees",
                "PrintAgent", "logs")).FullName;

    public static string? PrintToDirectory =>
        Environment.GetEnvironmentVariable(PrintToDirectoryVariable) is { Length: > 0 } directory ? directory : null;

    private static DateTime? ReadBuildTime()
    {
        var value = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "BuildTimestamp")?.Value;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var built)
            ? built.ToUniversalTime()
            : null;
    }
}
