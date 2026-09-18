using Microsoft.Win32;

namespace ITBees.Printers.Agent;

/// <summary>
/// "Start with Windows" for the current user (HKCU Run key - no administrator rights needed).
/// Only ever changed by the user ticking the tray menu item.
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ITBees Print Agent";

    public static bool IsEnabled => RefersTo(Environment.ProcessPath ?? "\0");

    /// <summary>The "start with Windows" entry starts the given executable.</summary>
    public static bool RefersTo(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command &&
               command.Contains(executable, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
