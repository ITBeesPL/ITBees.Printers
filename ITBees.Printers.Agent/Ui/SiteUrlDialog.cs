namespace ITBees.Printers.Agent.Ui;

/// <summary>Asks for the address of the web application to connect to.</summary>
public sealed class SiteUrlDialog : Form
{
    private readonly TextBox _url = new() { Dock = DockStyle.Top, PlaceholderText = "https://admin.example.com" };

    public SiteUrlDialog()
    {
        Text = "Połącz z serwisem";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(460, 150);
        Padding = new Padding(14);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 52,
            Text = "Podaj adres serwisu WWW, z którego chcesz drukować. Otworzy się przeglądarka - " +
                   "zaloguj się tam jak zwykle i potwierdź połączenie."
        };

        var ok = new Button { Text = "Połącz", DialogResult = DialogResult.OK, Width = 100, Height = 30 };
        var cancel = new Button { Text = "Anuluj", DialogResult = DialogResult.Cancel, Width = 100, Height = 30 };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = 40
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(_url);
        Controls.Add(hint);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string SiteUrl => _url.Text.Trim();
}
