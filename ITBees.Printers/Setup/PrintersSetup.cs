using ITBees.Printers.AgentHub;
using ITBees.Printers.Services.Agents;
using ITBees.Printers.Services.Jobs;
using ITBees.Printers.Services.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Printers.Setup;

public class PrintersSetup
{
    /// <summary>
    /// Registers the whole printing module: the services behind the REST controllers, the
    /// registry of connected print agents and the agent endpoints - served on the dedicated
    /// agent port (<see cref="PrintersSettings.AgentPort"/>) and, unless switched off, on the
    /// application's own port too (<see cref="PrintersSettings.ExposeOnApplicationPort"/>).
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

        // Behind the agent endpoints - acts on behalf of an authenticated agent.
        services.AddTransient<IPrintAgentSessionService, PrintAgentSessionService>();

        // The agent endpoints: on the dedicated port, and (ExposeOnApplicationPort) in front of
        // the application's own pipeline as well - the startup filter needs no Program.cs change.
        services.AddSingleton<PrintAgentApplicationPortBridge>();
        services.AddTransient<IStartupFilter, PrintAgentApplicationPortStartupFilter>();
        services.AddHostedService<PrintAgentHubHost>();
        services.AddHostedService<PrintJobCleanupService>();
    }
}
