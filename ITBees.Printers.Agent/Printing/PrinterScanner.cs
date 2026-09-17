using System.Drawing.Printing;
using System.Management;
using ITBees.Printers.Agent.Logging;
using ITBees.Printers.Protocol;

namespace ITBees.Printers.Agent.Printing;

/// <summary>Lists the printers installed for the current Windows user.</summary>
public class PrinterScanner
{
    private readonly AgentLog _log;
    private bool _wmiFailureLogged;

    public PrinterScanner(AgentLog log)
    {
        _log = log;
    }

    public List<AgentPrinterInfo> Scan()
    {
        try
        {
            return ScanWithWmi();
        }
        catch (Exception e)
        {
            // WMI can be broken or locked down - the plain list of names is still enough to print.
            if (!_wmiFailureLogged)
            {
                _log.Warning($"WMI printer query failed, falling back to the basic list: {e.Message}");
                _wmiFailureLogged = true;
            }

            return ScanBasic();
        }
    }

    /// <summary>Stable fingerprint of a scan - printers are reported again only when it changes.</summary>
    public static string Fingerprint(IEnumerable<AgentPrinterInfo> printers)
    {
        return string.Join("\n", printers
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"{x.Name}|{x.IsDefault}|{x.DriverName}|{x.PortName}|{x.Location}|{x.IsNetwork}|{x.IsOffline}|{x.Status}"));
    }

    private static List<AgentPrinterInfo> ScanWithWmi()
    {
        var printers = new List<AgentPrinterInfo>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, Default, DriverName, PortName, Location, Network, WorkOffline, PrinterStatus FROM Win32_Printer");
        foreach (var item in searcher.Get())
        {
            using var printer = (ManagementObject)item;
            var name = printer["Name"] as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var status = Convert.ToInt32(printer["PrinterStatus"] ?? 0);
            var workOffline = printer["WorkOffline"] as bool? ?? false;
            printers.Add(new AgentPrinterInfo
            {
                Name = name,
                IsDefault = printer["Default"] as bool? ?? false,
                DriverName = printer["DriverName"] as string,
                PortName = printer["PortName"] as string,
                Location = printer["Location"] as string,
                IsNetwork = printer["Network"] as bool? ?? false,
                IsOffline = workOffline || status == 7,
                Status = DescribeStatus(status)
            });
        }

        return printers;
    }

    private static List<AgentPrinterInfo> ScanBasic()
    {
        var defaultPrinter = new PrinterSettings().PrinterName;
        return PrinterSettings.InstalledPrinters.Cast<string>()
            .Select(name => new AgentPrinterInfo
            {
                Name = name,
                IsDefault = string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();
    }

    /// <summary>Win32_Printer.PrinterStatus.</summary>
    private static string? DescribeStatus(int status)
    {
        return status switch
        {
            3 => "Idle",
            4 => "Printing",
            5 => "Warming up",
            6 => "Stopped",
            7 => "Offline",
            1 or 2 => "Unknown",
            _ => null
        };
    }
}
