namespace PimaFilter.Monitor;

public sealed class BlacklistEntryDialog : Form
{
    private readonly ComboBox _port = new();
    private readonly TextBox _code = new();
    private readonly TextBox _reason = new();
    private readonly TextBox _note = new();
    private readonly Label _preview = new();

    public BlacklistEntryDialog()
    {
        Text = "Adăugare obiect în blacklist";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 330);
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.FromArgb(226, 232, 240);
        Font = new Font("Segoe UI", 10f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        _port.DropDownStyle = ComboBoxStyle.DropDownList;
        _port.FormattingEnabled = true;
        _port.Items.AddRange(ObjectCodeMapper.Targets.Cast<object>().ToArray());
        _port.Format += (_, eventArgs) =>
        {
            if (eventArgs.ListItem is ObjectCodeMapper.Target target)
            {
                eventArgs.Value = $"{target.Name} — port {target.Port}";
            }
        };
        _port.SelectedIndex = 0;
        _port.SelectedIndexChanged += (_, _) => UpdatePreview();
        AddRow(layout, 0, "Port PIMA Guard", _port);

        _code.MaxLength = 5;
        _code.PlaceholderText = "selectați mai întâi ruta";
        _code.TextChanged += (_, _) => UpdatePreview();
        _code.KeyPress += (_, eventArgs) =>
        {
            var numericOnly = _port.SelectedItem is ObjectCodeMapper.Target target && target.NumericBase is not null;
            if (!char.IsControl(eventArgs.KeyChar)
                && (numericOnly ? !char.IsDigit(eventArgs.KeyChar) : !char.IsLetterOrDigit(eventArgs.KeyChar)))
            {
                eventArgs.Handled = true;
            }
        };
        AddRow(layout, 1, "Cod obiect", _code);

        _preview.Text = "Cod final: 01____";
        _preview.ForeColor = Color.FromArgb(96, 165, 250);
        _preview.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
        _preview.AutoSize = true;
        AddRow(layout, 2, "Rezultat", _preview);

        _reason.Text = "Obiect retras din Andromeda";
        AddRow(layout, 3, "Motiv", _reason);
        _note.Multiline = true;
        _note.Height = 65;
        AddRow(layout, 4, "Notă", _note);

        var explanation = new Label
        {
            Text = "Cadrul PIMA nu este modificat. Codul final este folosit numai pentru comparația cu blacklist-ul.",
            AutoSize = true,
            ForeColor = Color.FromArgb(148, 163, 184),
            MaximumSize = new Size(320, 0)
        };
        AddRow(layout, 5, string.Empty, explanation);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true
        };
        var save = new Button { Text = "Salvează", Width = 110, Height = 32 };
        var cancel = new Button { Text = "Renunță", Width = 110, Height = 32, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveAndClose();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 1, 6);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public int SelectedPort => ((ObjectCodeMapper.Target)_port.SelectedItem!).Port;
    public string PimaCode => _code.Text.Trim();
    public string Reason => _reason.Text.Trim();
    public string Note => _note.Text.Trim();

    private void SaveAndClose()
    {
        try
        {
            _ = ObjectCodeMapper.BuildFinalCode(SelectedPort, PimaCode);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Cod invalid", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _code.Focus();
        }
    }

    private void UpdatePreview()
    {
        if (_port.SelectedItem is not ObjectCodeMapper.Target target)
        {
            return;
        }

        _code.MaxLength = target.MaximumInputLength;
        _code.PlaceholderText = target.NumericBase is null ? "exemplu: E005" : "exemplu: 1234 sau 12345";
        var typed = _code.Text.Trim().ToUpperInvariant();
        var hasValidLength = typed.Length >= target.MinimumInputLength && typed.Length <= target.MaximumInputLength;
        if (hasValidLength && (target.NumericBase is null || typed.All(char.IsAsciiDigit)))
        {
            _preview.Text = $"Cod pentru blacklist: {ObjectCodeMapper.BuildFinalCode(target.Port, typed)}";
        }
        else if (target.NumericBase is int numericBase)
        {
            _preview.Text = $"Cod pentru blacklist: {numericBase} + {typed.PadLeft(5, '_')}";
        }
        else
        {
            _preview.Text = $"Cod pentru blacklist: {target.Prefix}{typed.PadRight(target.MaximumInputLength, '_')}";
        }
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(row == 4 ? SizeType.Absolute : SizeType.AutoSize, row == 4 ? 72 : 38));
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            ForeColor = Color.FromArgb(148, 163, 184),
            Margin = new Padding(0, 8, 10, 0)
        }, 0, row);
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 3, 0, 3);
        layout.Controls.Add(control, 1, row);
    }
}
