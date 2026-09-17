using ITBees.Printers.Agent.Connection;

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
    private StatusForm? _statusForm;

    public AgentApplicationContext(AgentRuntime runtime, SingleInstance singleInstance, AgentArguments arguments)
    {
        _runtime = runtime;
        _singleInstance = singleInstance;
        _uiThread.CreateControl(); // a handle on the UI thread, to marshal background events onto it

        _autoStartItem = new ToolStripMenuItem("Uruchamiaj przy starcie Windows") { CheckOnClick = true };
        _autoStartItem.Click += (_, _) => AutoStart.Set(_autoStartItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"{AgentInfo.ProductName} {AgentInfo.Version}") { Enabled = false });
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
        HandleArguments(arguments, showWindow: !arguments.Minimized);
    }

    private void HandleArguments(AgentArguments arguments, bool showWindow)
    {
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
