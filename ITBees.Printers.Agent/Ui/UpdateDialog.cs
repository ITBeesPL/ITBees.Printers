using ITBees.Printers.Agent.Updates;

namespace ITBees.Printers.Agent.Ui;

/// <summary>
/// Offers a newer published version and, when accepted, downloads and installs it.
/// DialogResult.OK means downloaded and checked (<see cref="Downloaded"/>) - the caller closes the
/// agent and lets the new version install itself.
/// </summary>
public sealed class UpdateDialog : Form
{
    private readonly AgentUpdater _updater;
    private readonly AvailableUpdate _update;
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Top, Height = 20, Maximum = 1000, Visible = false };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 64, Padding = new Padding(0, 6, 0, 0) };
    private readonly Button _install = new()
        { Text = "Aktualizuj", AutoSize = true, MinimumSize = new Size(110, 30) };
    private readonly Button _later = new()
        { Text = "Nie teraz", AutoSize = true, MinimumSize = new Size(110, 30) };
    private CancellationTokenSource? _download;

    public UpdateDialog(AgentUpdater updater, AvailableUpdate update, Icon icon)
    {
        _updater = updater;
        _update = update;

        Text = $"{AgentInfo.ProductName} - aktualizacja";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        // Usually shown right after logging in to Windows, with no other window of the agent
        // open - it must neither hide behind other windows nor be impossible to find again.
        TopMost = true;
        ShowInTaskbar = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 250);
        Padding = new Padding(14);

        var message = new Label
        {
            Dock = DockStyle.Top,
            Height = 88,
            Text = $"Dostępna jest nowa wersja programu: {update.Version}.{Environment.NewLine}" +
                   $"Zainstalowana wersja: {AgentInfo.Version}.{Environment.NewLine}{Environment.NewLine}" +
                   "Czy chcesz ją teraz pobrać i zainstalować? Program uruchomi się ponownie, " +
                   "a poprzednia wersja zostanie usunięta."
        };

        _install.Click += async (_, _) => await DownloadAndInstall();
        _later.Click += (_, _) => Close();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40
        };
        buttons.Controls.Add(_later);
        buttons.Controls.Add(_install);

        // Docked controls stack in reverse order of adding: the last one added ends up on top.
        Controls.Add(_status);
        Controls.Add(_progress);
        Controls.Add(message);
        Controls.Add(buttons);
        AcceptButton = _install;
        CancelButton = _later;
    }

    public DownloadedUpdate? Downloaded { get; private set; }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing during the download cancels it - the partial file is removed.
        _download?.Cancel();
        base.OnFormClosing(e);
    }

    private async Task DownloadAndInstall()
    {
        _install.Enabled = false;
        _later.Text = "Anuluj";
        _progress.Style = ProgressBarStyle.Blocks;
        _progress.Value = 0;
        _progress.Visible = true;
        _status.ForeColor = SystemColors.ControlText;
        _status.Text = "Pobieranie...";

        var download = new CancellationTokenSource();
        _download = download;
        try
        {
            var downloaded = await _updater.Download(_update, new Progress<DownloadProgress>(ShowProgress), download.Token);
            if (download.IsCancellationRequested)
            {
                // Closed just as the last bytes came in.
                AgentUpdater.TryDelete(downloaded.File);
                return;
            }

            Downloaded = downloaded;
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException) when (download.IsCancellationRequested)
        {
            // Closed by the user.
        }
        catch (Exception e)
        {
            if (!IsDisposed)
            {
                ShowError(Describe(e));
            }
        }
        finally
        {
            _download = null;
            download.Dispose();
        }
    }

    private void ShowProgress(DownloadProgress progress)
    {
        // Progress reports are posted - the last ones may arrive after the dialog was closed.
        if (IsDisposed || _download == null)
        {
            return;
        }

        if (progress.Total is { } total && total > 0)
        {
            _progress.Value = (int)Math.Min(_progress.Maximum, progress.Received * _progress.Maximum / total);
            _status.Text = $"Pobieranie... {Megabytes(progress.Received)} / {Megabytes(total)} MB";
        }
        else
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _status.Text = $"Pobieranie... {Megabytes(progress.Received)} MB";
        }
    }

    private void ShowError(string message)
    {
        _progress.Visible = false;
        _status.ForeColor = Color.Firebrick;
        _status.Text = message;
        _install.Text = "Spróbuj ponownie";
        _install.Enabled = true;
        _later.Text = "Zamknij";
    }

    private string Describe(Exception e)
    {
        return e switch
        {
            UpdateException => e.Message,
            UnauthorizedAccessException =>
                $"Brak uprawnień do zapisu w folderze {Path.GetDirectoryName(Environment.ProcessPath)}. " +
                $"Pobierz nową wersję ręcznie: {_update.DownloadUrl}",
            HttpRequestException { StatusCode: { } status } =>
                $"Serwer aktualizacji odpowiedział błędem {(int)status} ({status}).",
            HttpRequestException => $"Nie udało się połączyć z serwerem aktualizacji: {e.Message}",
            IOException => $"Nie udało się zapisać nowej wersji: {e.Message}",
            _ => $"Aktualizacja nie powiodła się: {e.Message}"
        };
    }

    private static string Megabytes(long bytes) => (bytes / 1048576d).ToString("0.0");
}
