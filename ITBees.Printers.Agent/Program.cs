using ITBees.Printers.Agent.Ui;
using ITBees.Printers.Agent.Updates;

namespace ITBees.Printers.Agent;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Started by the previous version to put this one in its place - nothing else to do.
        if (AgentUpdater.TryApply(args))
        {
            return;
        }

        AgentRuntime runtime;
        DownloadedUpdate? pendingUpdate;
        using (var singleInstance = new SingleInstance())
        {
            if (!singleInstance.IsFirstInstance && !singleInstance.HandOverOrReplace(args))
            {
                // The same program is already running and took the command line (e.g. a new --site).
                return;
            }

            ApplicationConfiguration.Initialize();

            runtime = new AgentRuntime();
            Application.ThreadException += (_, e) => runtime.Log.Error("Unhandled UI exception", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                runtime.Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                runtime.Log.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            };

            var context = new AgentApplicationContext(runtime, singleInstance, AgentArguments.Parse(args));
            Application.Run(context);
            pendingUpdate = context.PendingUpdate;
        }

        // Only now, with the single-instance lock released, can the new version take over.
        if (pendingUpdate != null)
        {
            AgentUpdater.Launch(pendingUpdate, runtime.Log);
        }
    }
}
