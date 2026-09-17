using ITBees.Printers.Controllers.Models;

namespace ITBees.Printers.Services.Agents;

public interface IPrintAgentRegistrationService
{
    /// <summary>
    /// Issues a one-time registration code bound to the logged-in user. The request's scheme
    /// and host are the fallback for the agent listener address when
    /// <see cref="PrintersSettings.PublicAgentUrl"/> is not configured.
    /// </summary>
    PrintAgentRegistrationVm Create(PrintAgentRegistrationIm? registrationIm, string requestScheme, string requestHost);
}
