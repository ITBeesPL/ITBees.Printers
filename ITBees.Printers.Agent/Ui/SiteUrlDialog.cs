namespace ITBees.Printers.Agent.Ui;

/// <summary>Asks for the address of the web application to connect to.</summary>
public sealed class SiteUrlDialog : Form
{
    private readonly TextBox _url = new() { Dock = DockStyle.Top, PlaceholderText = "np. admin.example.com" };

    public SiteUrlDialog()
    {
        Text = "Połącz z serwisem";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 190);
        Padding = new Padding(14);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 58,
            Text = "Podaj adres panelu WWW, w którym się logujesz i z którego chcesz drukować " +
                   "(nie adres API). Otworzy się przeglądarka - zaloguj się tam jak zwykle i potwierdź połączenie."
        };

        var schemeNote = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 6, 0, 0),
            Text = "Wystarczy sama nazwa, np. admin.example.com - „https://” dopiszemy sami."
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

        // Docked controls stack in reverse order of adding: the last one added ends up on top.
        Controls.Add(schemeNote);
        Controls.Add(_url);
        Controls.Add(hint);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        // An address the agent cannot make sense of keeps the dialog open instead of failing later.
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK)
            {
                return;
            }

            if (!AddressNormalizer.TryNormalize(_url.Text, out var _))
            {
                e.Cancel = true;
                MessageBox.Show(this, "Podaj adres panelu WWW serwisu, np. admin.example.com.",
                    AgentInfo.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                _url.Focus();
            }
        };
    }

    /// <summary>What the user typed, completed to a full address ("admin.example.com" -> "https://admin.example.com").</summary>
    public string SiteUrl => AddressNormalizer.TryNormalize(_url.Text, out var url) ? url : _url.Text.Trim();
}
