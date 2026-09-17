using ITBees.Printers.Controllers.Models;

namespace ITBees.Printers.Services.Agents;

/// <summary>The logged-in user's own print agents.</summary>
public interface IPrintAgentsService
{
    List<PrintAgentVm> GetMine();

    /// <summary>
    /// Removes the agent from the account: its token stops working, its connection is
    /// dropped and the print settings that used its printers fall back to the PDF.
    /// </summary>
    Task Delete(Guid guid);

    /// <summary>Asks a connected agent to scan its printers again. False when it is offline.</summary>
    Task<bool> RefreshPrinters(Guid agentGuid);
}
