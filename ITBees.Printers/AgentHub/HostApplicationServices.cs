using Microsoft.Extensions.DependencyInjection;

namespace ITBees.Printers.AgentHub;

/// <summary>
/// The host application's root service provider, as seen from inside the agent listener.
/// The listener has a container of its own (it is a separate Kestrel instance), so this is
/// its only door to the application: repositories, settings and the services built on them.
/// </summary>
public class HostApplicationServices
{
    private readonly IServiceProvider _rootProvider;

    public HostApplicationServices(IServiceProvider rootProvider)
    {
        _rootProvider = rootProvider;
    }

    public IServiceScope CreateScope()
    {
        return _rootProvider.CreateScope();
    }
}
