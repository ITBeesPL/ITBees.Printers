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

    public static string Version { get; } =
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "1.0.0").Split('+')[0];

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
}
