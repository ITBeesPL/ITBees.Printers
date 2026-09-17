using System.Diagnostics;
using ITBees.Printers.Agent.Connection;

namespace ITBees.Printers.Agent.Ui;

/// <summary>The agent's only window: connected services, their state and the log.</summary>
public sealed class StatusForm : Form
{
    private readonly AgentRuntime _runtime;
    private readonly ListView _services = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false
    };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Font = new Font("Consolas", 8.5f),
        BackColor = SystemColors.Window
    };
    private readonly Button _login = new() { Text = "Zaloguj ponownie", AutoSize = true, Height = 30 };
    private readonly Button _open = new() { Text = "Otwórz serwis", AutoSize = true, Height = 30 };
    private readonly Button _remove = new() { Text = "Usuń", AutoSize = true, Height = 30 };

    public StatusForm(AgentRuntime runtime, Icon icon)
    {
        _runtime = runtime;

        Text = $"{AgentInfo.ProductName} {AgentInfo.Version} - {AgentInfo.MachineName}";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(640, 400);

        _services.Columns.Add("Serwis", 200);
        _services.Columns.Add("Adres", 260);
        _services.Columns.Add("Stan", 230);
        _services.Columns.Add("Drukarki", 80);
        _services.SelectedIndexChanged += (_, _) => UpdateButtons();

        var connect = new Button { Text = "Połącz z serwisem...", AutoSize = true, Height = 30 };
        connect.Click += (_, _) => ConnectNewSite();
        _login.Click += (_, _) => WithSelected(x => _ = _runtime.ConnectSite(x.Profile.SiteUrl, forceLogin: true));
        _open.Click += (_, _) => WithSelected(x => OpenInBrowser(x.Profile.SiteUrl));
        _remove.Click += (_, _) => WithSelected(RemoveService);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange(new Control[] { connect, _login, _open, _remove });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 190
        };
        split.Panel1.Controls.Add(_services);
        split.Panel1.Controls.Add(buttons);
        split.Panel2.Controls.Add(_log);
        split.Panel2.Controls.Add(new Label { Text = "Dziennik", Dock = DockStyle.Top, Height = 22 });

        Padding = new Padding(10);
        Controls.Add(split);

        _log.Lines = _runtime.Log.Snapshot().ToArray();
        _runtime.Log.LineAdded += OnLogLine;
        _runtime.Changed += OnRuntimeChanged;
        RefreshServices();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing the window only hides it - the agent keeps running in the tray.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _runtime.Log.LineAdded -= OnLogLine;
        _runtime.Changed -= OnRuntimeChanged;
        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ScrollLogToEnd();
    }

    public static string Describe(ServiceConnection connection)
    {
        return connection.State switch
        {
            ServiceConnectionState.Connected => "Połączono",
            ServiceConnectionState.Connecting => "Łączenie...",
            ServiceConnectionState.LoginRequired => "Wymaga zalogowania",
            _ => string.IsNullOrEmpty(connection.LastError) ? "Rozłączono" : "Brak połączenia - ponawiam"
        };
    }

    private void OnRuntimeChanged()
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(RefreshServices);
        }
    }

    private void OnLogLine(string line)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        BeginInvoke(() =>
        {
            _log.AppendText((_log.TextLength == 0 ? string.Empty : Environment.NewLine) + line);
        });
    }

    private void RefreshServices()
    {
        var selected = _services.SelectedItems.Count > 0 ? _services.SelectedItems[0].Tag : null;

        _services.BeginUpdate();
        _services.Items.Clear();
        foreach (var connection in _runtime.GetConnections())
        {
            var item = new ListViewItem(new[]
            {
                connection.Profile.DisplayName,
                connection.Profile.SiteUrl,
                Describe(connection),
                connection.State == ServiceConnectionState.Connected ? connection.ReportedPrinters.ToString() : "-"
            })
            {
                Tag = connection,
                ToolTipText = connection.LastError,
                Selected = ReferenceEquals(connection, selected)
            };
            _services.Items.Add(item);
        }

        _services.EndUpdate();
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var any = _services.SelectedItems.Count > 0;
        _login.Enabled = any;
        _open.Enabled = any;
        _remove.Enabled = any;
    }

    private void WithSelected(Action<ServiceConnection> action)
    {
        if (_services.SelectedItems.Count > 0 && _services.SelectedItems[0].Tag is ServiceConnection connection)
        {
            action(connection);
        }
    }

    private void ConnectNewSite()
    {
        using var dialog = new SiteUrlDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SiteUrl.Length > 0)
        {
            _ = _runtime.ConnectSite(dialog.SiteUrl);
        }
    }

    private void RemoveService(ServiceConnection connection)
    {
        var answer = MessageBox.Show(this,
            $"Odłączyć serwis „{connection.Profile.DisplayName}” od tego komputera?\n\n" +
            "Wpis o tym komputerze w ustawieniach drukowania serwisu usuniesz po zalogowaniu się do niego.",
            AgentInfo.ProductName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes)
        {
            _ = _runtime.RemoveService(connection);
        }
    }

    private void ScrollLogToEnd()
    {
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static void OpenInBrowser(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
