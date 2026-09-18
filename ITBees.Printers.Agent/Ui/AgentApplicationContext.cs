using ITBees.Printers.Agent.Connection;
using ITBees.Printers.Agent.Updates;

namespace ITBees.Printers.Agent.Ui;

/// <summary>The tray application: an icon with a menu, balloons for things that need attention, and the status window.</summary>
public sealed class AgentApplicationContext : ApplicationContext
{
    private readonly AgentRuntime _runtime;
    private readonly SingleInstance _singleInstance;
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly Control _uiThread = new();
    private readonly AgentUpdater _updater;
    private StatusForm? _statusForm;
    private UpdateDialog? _updateDialog;
    private bool _exiting;

    public AgentApplicationContext(AgentRuntime runtime, SingleInstance singleInstance, AgentArguments arguments)
    {
        _runtime = runtime;
        _singleInstance = singleInstance;
        _updater = new AgentUpdater(runtime.Log);
        _uiThread.CreateControl(); // a handle on the UI thread, to marshal background events onto it

        _autoStartItem = new ToolStripMenuItem("Uruchamiaj przy starcie Windows") { CheckOnClick = true };
        _autoStartItem.Click += (_, _) => AutoStart.Set(_autoStartItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"{AgentInfo.ProductName} {AgentInfo.DisplayVersion}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Pokaż okno", null, (_, _) => ShowStatusForm());
        menu.Items.Add("Połącz z serwisem...", null, (_, _) => ConnectNewSite());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Zakończ", null, async (_, _) => await Exit());
        menu.Opening += (_, _) => _autoStartItem.Checked = AutoStart.IsEnabled;

        _trayIcon = new NotifyIcon
        {
            Icon = _icons.Connecting,
            Text = AgentInfo.ProductName,
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowStatusForm();

        _runtime.Changed += () => OnUiThread(UpdateTrayIcon);
        _runtime.Notice += (kind, title, text) => OnUiThread(() => ShowBalloon(kind, title, text));
        _singleInstance.ArgumentsReceived += args => OnUiThread(() => HandleArguments(AgentArguments.Parse(args), true));

        _runtime.Start();
        _singleInstance.StartListening();
        UpdateTrayIcon();
        AnnounceReplacedCopy();
        HandleArguments(arguments, showWindow: !arguments.Minimized);

        _updater.RemoveLeftovers();
        if (arguments.Updated)
        {
            ShowBalloon(NoticeKind.Info, AgentInfo.ProductName, $"Zaktualizowano do wersji {AgentInfo.Version}.");
        }
        else if (arguments.UpdateFailed)
        {
            ShowBalloon(NoticeKind.Warning, "Aktualizacja nie powiodła się",
                "Działa dotychczasowa wersja programu. Szczegóły są w dzienniku.");
        }

        _ = CheckForUpdate();
    }

    /// <summary>Set when the agent closes to make way for an update - Program then starts the new version.</summary>
    public DownloadedUpdate? PendingUpdate { get; private set; }

    private void HandleArguments(AgentArguments arguments, bool showWindow)
    {
        if (arguments.Quit)
        {
            // Another copy of the agent (another folder - usually a newer build) takes over.
            _runtime.Log.Info("Another copy of the agent is taking over - closing");
            _ = Exit();
            return;
        }

        if (!string.IsNullOrWhiteSpace(arguments.SiteUrl))
        {
            _ = _runtime.ConnectSite(arguments.SiteUrl);
        }

        // Started by hand with nothing to do yet - show where to begin instead of hiding in the tray.
        if (showWindow && (string.IsNullOrWhiteSpace(arguments.SiteUrl) || _runtime.GetConnections().Count == 0))
        {
            ShowStatusForm();
        }
    }

    private void AnnounceReplacedCopy()
    {
        if (_singleInstance.ReplacedExecutable is not { } replaced)
        {
            return;
        }

        _runtime.Log.Info($"Replaced the copy of the agent that was running from {replaced}");
        if (AutoStart.RefersTo(replaced))
        {
            // "Start with Windows" would bring the replaced copy back on the next login.
            AutoStart.Set(true);
            _runtime.Log.Info("Start with Windows now starts this copy");
        }

        ShowBalloon(NoticeKind.Info, AgentInfo.ProductName,
            $"Zastąpiono wcześniej uruchomioną kopię aplikacji ({replaced}).");
    }

    /// <summary>On every start: a newer published version is offered until the user takes it.</summary>
    private async Task CheckForUpdate()
    {
        var update = await _updater.Check(CancellationToken.None);
        if (update == null || _exiting)
        {
            return;
        }

        using var dialog = new UpdateDialog(_updater, update, _icons.Connected);
        _updateDialog = dialog;
        var result = dialog.ShowDialog();
        _updateDialog = null;
        if (result != DialogResult.OK || dialog.Downloaded is not { } downloaded)
        {
            _runtime.Log.Info($"Update {update.Version} not installed - it will be offered again on the next start");
            return;
        }

        // The new version can only start once this one has released the single-instance lock -
        // Program does that after the message loop ends.
        PendingUpdate = downloaded;
        await Exit();
    }

    private void ShowStatusForm()
    {
        if (_statusForm == null || _statusForm.IsDisposed)
        {
            _statusForm = new StatusForm(_runtime, _icons.Connected);
        }

        _statusForm.Show();
        if (_statusForm.WindowState == FormWindowState.Minimized)
        {
            _statusForm.WindowState = FormWindowState.Normal;
        }

        _statusForm.Activate();
    }

    private void ConnectNewSite()
    {
        using var dialog = new SiteUrlDialog();
        if (dialog.ShowDialog() == DialogResult.OK && dialog.SiteUrl.Length > 0)
        {
            _ = _runtime.ConnectSite(dialog.SiteUrl);
        }
    }

    private void UpdateTrayIcon()
    {
        var connections = _runtime.GetConnections();
        var connected = connections.Count(x => x.State == ServiceConnectionState.Connected);

        if (connections.Count == 0 || connections.Any(x => x.State == ServiceConnectionState.LoginRequired))
        {
            _trayIcon.Icon = _icons.Attention;
        }
        else
        {
            _trayIcon.Icon = connected == connections.Count ? _icons.Connected : _icons.Connecting;
        }

        // NotifyIcon.Text is limited to 127 characters.
        var text = connections.Count == 0
            ? $"{AgentInfo.ProductName} - brak połączonych serwisów"
            : $"{AgentInfo.ProductName} - połączone serwisy: {connected}/{connections.Count}";
        _trayIcon.Text = text.Length <= 127 ? text : text[..127];
    }

    private void ShowBalloon(NoticeKind kind, string title, string text)
    {
        var icon = kind switch
        {
            NoticeKind.Error => ToolTipIcon.Error,
            NoticeKind.Warning => ToolTipIcon.Warning,
            _ => ToolTipIcon.Info
        };
        _trayIcon.ShowBalloonTip(5000, string.IsNullOrWhiteSpace(title) ? AgentInfo.ProductName : title,
            string.IsNullOrWhiteSpace(text) ? " " : text, icon);
    }

    private void OnUiThread(Action action)
    {
        if (_uiThread.IsHandleCreated && !_uiThread.IsDisposed)
        {
            _uiThread.BeginInvoke(action);
        }
    }

    private async Task Exit()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _updateDialog?.Close();
        _trayIcon.Visible = false;
        await _runtime.Shutdown();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _statusForm?.Dispose();
            _icons.Dispose();
            _uiThread.Dispose();
        }

        base.Dispose(disposing);
    }
}
