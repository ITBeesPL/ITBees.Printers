namespace ITBees.Printers.Controllers.Models;

public class PrintAgentPrintersRefreshVm
{
    public PrintAgentPrintersRefreshVm()
    {
    }

    public Guid AgentGuid { get; set; }

    /// <summary>False when the agent is not connected - there was nobody to ask.</summary>
    public bool Requested { get; set; }
}
