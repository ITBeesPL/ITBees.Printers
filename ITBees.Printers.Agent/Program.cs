using ITBees.Printers.Agent.Ui;

namespace ITBees.Printers.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new SingleInstance();
        if (!singleInstance.IsFirstInstance)
        {
            // Already running: pass the command line on (e.g. a new --site) and leave.
            singleInstance.SendToFirstInstance(args);
            return;
        }

        ApplicationConfiguration.Initialize();

        var runtime = new AgentRuntime();
        Application.ThreadException += (_, e) => runtime.Log.Error("Unhandled UI exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            runtime.Log.Error("Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            runtime.Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        Application.Run(new AgentApplicationContext(runtime, singleInstance, AgentArguments.Parse(args)));
    }
}
