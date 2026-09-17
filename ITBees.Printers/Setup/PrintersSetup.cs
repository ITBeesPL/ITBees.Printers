using ITBees.Printers.AgentHub;
using ITBees.Printers.Services.Agents;
using ITBees.Printers.Services.Jobs;
using ITBees.Printers.Services.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Printers.Setup;

public class PrintersSetup
{
    /// <summary>
    /// Registers the whole printing module: the services behind the REST controllers, the
    /// registry of connected print agents and the hosted service that opens the dedicated
    /// agent port (<see cref="PrintersSettings.AgentPort"/>) when the application starts.
    /// The entities are mapped separately - see <see cref="DbModelBuilder.Register"/>.
    /// </summary>
    public void Register(IServiceCollection services, PrintersSettings? settings = null)
    {
        services.AddSingleton(settings ?? new PrintersSettings());

        // Shared state: live agent connections and pending registration codes.
        services.AddSingleton<PrintAgentGateway>();
        services.AddSingleton<IPrintAgentGateway>(x => x.GetRequiredService<PrintAgentGateway>());
        services.AddSingleton<PrintAgentRegistrationCodeStore>();
        services.AddSingleton<ITestPagePdfGenerator, TestPagePdfGenerator>();

        // Behind the REST controllers - they act on behalf of the logged-in user.
        services.AddTransient<IPrintAgentRegistrationService, PrintAgentRegistrationService>();
        services.AddTransient<IPrintAgentsService, PrintAgentsService>();
        services.AddTransient<IPrintSettingsService, PrintSettingsService>();
        services.AddTransient<IPrintJobService, PrintJobService>();

        // Behind the agent listener - acts on behalf of an authenticated agent.
        services.AddTransient<IPrintAgentSessionService, PrintAgentSessionService>();

        services.AddHostedService<PrintAgentHubHost>();
        services.AddHostedService<PrintJobCleanupService>();
    }
}
