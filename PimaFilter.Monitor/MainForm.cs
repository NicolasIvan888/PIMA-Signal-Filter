using System.Diagnostics;
using System.Text;

namespace PimaFilter.Monitor;

public sealed class MainForm : Form
{
    private static readonly Color BackgroundColor = Color.FromArgb(15, 23, 42);
    private static readonly Color PanelColor = Color.FromArgb(24, 31, 45);
    private static readonly Color BorderColor = Color.FromArgb(51, 65, 85);
    private static readonly Color TextColor = Color.FromArgb(226, 232, 240);
    private static readonly Color MutedColor = Color.FromArgb(148, 163, 184);
    private static readonly Color GreenColor = Color.FromArgb(52, 211, 153);
    private static readonly Color YellowColor = Color.FromArgb(251, 191, 36);
    private static readonly Color RedColor = Color.FromArgb(248, 113, 113);
    private static readonly Color BlueColor = Color.FromArgb(96, 165, 250);

    private readonly MonitorDataReader _reader;
    private readonly BlacklistStore _blacklistStore;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private readonly Dictionary<string, MetricCard> _cards = [];
    private readonly List<StatusSample> _history = [];
    private readonly BindingSource _routeSource = new();
    private readonly BindingSource _eventSource = new();
    private readonly BindingSource _blacklistSource = new();

    private readonly Label _healthBadge = new();
    private readonly Label _modeBadge = new();
    private readonly Label _updatedLabel = new();
    private readonly Label _alertLabel = new();
    private readonly DataGridView _routeGrid = new();
    private readonly DataGridView _eventGrid = new();
    private readonly ActivityChart _chart = new();
    private readonly TextBox _searchBox = new();
    private readonly ComboBox _categoryBox = new();
    private readonly Button _pauseButton = new();
    private readonly DataGridView _blacklistGrid = new();
    private readonly Label _blacklistCountLabel = new();
    private readonly Panel _operationalContent = new();
    private readonly Button _eventsTabButton = new();
    private readonly Button _blacklistTabButton = new();
    private Control? _eventsView;
    private Control? _blacklistView;

    private FilterStatus? _previousStatus;
    private IReadOnlyList<EventRow> _events = [];
    private IReadOnlyList<EventRow> _visibleEvents = [];
    private bool _refreshing;
    private bool _paused;
    private DateTimeOffset _lastEventRefreshUtc = DateTimeOffset.MinValue;
    private string? _blacklistFingerprint;

    public MainForm(string dataDirectory)
    {
        _reader = new MonitorDataReader(dataDirectory);
        _blacklistStore = new BlacklistStore(dataDirectory);
        Text = "PIMA Signal Filter Monitor";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 720);
        Size = new Size(1440, 900);
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9.5f);

        BuildInterface();
        _timer.Tick += async (_, _) => await RefreshDashboardAsync();
        Shown += async (_, _) =>
        {
            await RefreshDashboardAsync();
            _timer.Start();
        };
    }

    public Task RefreshNowAsync() => RefreshDashboardAsync();

    public void ShowBlacklistTab()
    {
        SelectOperationalView(showBlacklist: true);
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(18),
            BackColor = BackgroundColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 225));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(BuildCards(), 0, 1);
        root.Controls.Add(BuildRoutes(), 0, 2);
        root.Controls.Add(BuildOperationalTabs(), 0, 3);
        root.Controls.Add(BuildCompactChart(), 0, 4);

        _alertLabel.Dock = DockStyle.Fill;
        _alertLabel.TextAlign = ContentAlignment.MiddleLeft;
        _alertLabel.ForeColor = MutedColor;
        _alertLabel.Text = "Inițializare monitor…";
        root.Controls.Add(_alertLabel, 0, 5);
    }

    private Control BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "PIMA SIGNAL FILTER",
            Font = new Font("Segoe UI Semibold", 19f, FontStyle.Bold),
            ForeColor = TextColor,
            AutoSize = true,
            Location = new Point(0, 4)
        };
        var subtitle = new Label
        {
            Text = "Monitor operațional • rutele sunt afișate din configurația serviciului",
            ForeColor = MutedColor,
            AutoSize = true,
            Location = new Point(2, 39)
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);

        ConfigureBadge(_healthBadge, "SE CONECTEAZĂ", BorderColor, 170);
        ConfigureBadge(_modeBadge, "MOD —", BlueColor, 115);
        _updatedLabel.ForeColor = MutedColor;
        _updatedLabel.AutoSize = true;
        _updatedLabel.TextAlign = ContentAlignment.MiddleRight;

        var refreshButton = CreateButton("Actualizează", async (_, _) => await RefreshDashboardAsync());
        _pauseButton.Text = "Pauză";
        StyleButton(_pauseButton);
        _pauseButton.Click += (_, _) =>
        {
            _paused = !_paused;
            _pauseButton.Text = _paused ? "Continuă" : "Pauză";
            _pauseButton.BackColor = _paused ? Color.FromArgb(120, 53, 15) : PanelColor;
        };
        var logsButton = CreateButton("Deschide jurnalele", (_, _) => OpenDirectory(_reader.LogDirectory));

        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        right.Controls.Add(_healthBadge);
        right.Controls.Add(_modeBadge);
        right.Controls.Add(_updatedLabel);
        right.Controls.Add(refreshButton);
        right.Controls.Add(_pauseButton);
        right.Controls.Add(logsButton);
        panel.Controls.Add(right);
        return panel;
    }

    private Control BuildCards()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            Padding = new Padding(0, 2, 0, 2)
        };
        for (var index = 0; index < layout.ColumnCount; index++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / layout.ColumnCount));
        }

        AddCard(layout, "connections", "CONEXIUNI", BlueColor, 0);
        AddCard(layout, "rate", "RITM MES./MIN", BlueColor, 1);
        AddCard(layout, "allowed", "PERMISE", GreenColor, 2);
        AddCard(layout, "blocked", "BLOCATE", YellowColor, 3);
        AddCard(layout, "errors", "ERORI", RedColor, 4);
        AddCard(layout, "invalid", "CADRE INVALIDE", YellowColor, 5);
        AddCard(layout, "ack", "ACK EVMan", GreenColor, 6);
        return layout;
    }

    private Control BuildRoutes()
    {
        var routesPanel = CreateSection("CANALE PIMA GUARD");
        ConfigureGrid(_routeGrid);
        _routeGrid.DataSource = _routeSource;
        routesPanel.Controls.Add(_routeGrid);
        _routeGrid.BringToFront();
        return routesPanel;
    }

    private Control BuildCompactChart()
    {
        var strip = new Panel { Dock = DockStyle.Fill, BackColor = BackgroundColor };
        _chart.Dock = DockStyle.Right;
        _chart.Width = 360;
        strip.Controls.Add(_chart);
        return strip;
    }

    private Control BuildEvents()
    {
        var section = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelColor,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(4)
        };
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 3)
        };
        toolbar.Controls.Add(new Label
        {
            Text = "JURNAL DE DIAGNOSTIC",
            AutoSize = true,
            ForeColor = TextColor,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Margin = new Padding(0, 7, 18, 0)
        });
        toolbar.Controls.Add(new Label
        {
            Text = "Caută obiect:",
            AutoSize = true,
            ForeColor = MutedColor,
            Margin = new Padding(0, 7, 6, 0)
        });
        _searchBox.Width = 150;
        _searchBox.BackColor = Color.FromArgb(30, 41, 59);
        _searchBox.ForeColor = TextColor;
        _searchBox.BorderStyle = BorderStyle.FixedSingle;
        _searchBox.TextChanged += (_, _) => ApplyEventFilter();
        toolbar.Controls.Add(_searchBox);

        _categoryBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _categoryBox.BackColor = Color.FromArgb(30, 41, 59);
        _categoryBox.ForeColor = TextColor;
        _categoryBox.Items.AddRange(["Toate", "Permise", "De blocat în Monitor", "Blocate", "Erori", "Cadre invalide"]);
        _categoryBox.SelectedIndex = 0;
        _categoryBox.SelectedIndexChanged += (_, _) => ApplyEventFilter();
        toolbar.Controls.Add(_categoryBox);

        var folderButton = CreateButton("Dosar date", (_, _) => OpenDirectory(_reader.DataDirectory));
        toolbar.Controls.Add(folderButton);
        toolbar.Controls.Add(CreateButton("Ce înseamnă?", (_, _) => MessageBox.Show(
            this,
            "PERMIS: filtrul a primit ACK 06 de la Andromeda și l-a trimis PIMAGuard.\n" +
            "CADRU INVALID: datele primite nu respectă formatul configurat; dublu clic arată octeții originali.\n" +
            "EROARE: transmiterea către Andromeda nu a fost confirmată; poate urma o retransmitere.\n" +
            "BLOCAT: o regulă activă a oprit semnalul și filtrul a trimis ACK local.\n\n" +
            "Obiect final este codul folosit de blacklist; Cod PIMA este codul din semnal. " +
            "Sursa este adresa expeditorului. Așteptare .2 (ms) este timpul până la ACK sau timeout.\n\n" +
            "Mesajele permise sunt afișate prin eșantionare. Contoarele de sus sunt complete; " +
            "după actualizarea serviciului, erorile și cadrele invalide noi sunt păstrate integral în fișierele jurnal. " +
            "Tabelul arată doar cele mai recente rânduri.",
            "Cum se citește jurnalul",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information)));
        section.Controls.Add(toolbar);

        ConfigureGrid(_eventGrid);
        _eventGrid.DataSource = _eventSource;
        _eventGrid.CellFormatting += EventGridCellFormatting;
        _eventGrid.CellDoubleClick += ShowEventDetails;
        section.Controls.Add(_eventGrid);
        _eventGrid.BringToFront();
        return section;
    }

    private Control BuildOperationalTabs()
    {
        var container = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = BackgroundColor,
            Padding = Padding.Empty
        };
        var navigation = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = BackgroundColor
        };
        ConfigureNavigationButton(_eventsTabButton, "Mesaje și erori", 0);
        ConfigureNavigationButton(_blacklistTabButton, "Lista neagră", 184);
        _eventsTabButton.Click += (_, _) => SelectOperationalView(showBlacklist: false);
        _blacklistTabButton.Click += (_, _) => SelectOperationalView(showBlacklist: true);
        navigation.Controls.Add(_eventsTabButton);
        navigation.Controls.Add(_blacklistTabButton);

        _operationalContent.Dock = DockStyle.Fill;
        _operationalContent.BackColor = BackgroundColor;
        _eventsView = BuildEvents();
        _blacklistView = BuildBlacklist();
        _eventsView.Dock = DockStyle.Fill;
        _blacklistView.Dock = DockStyle.Fill;
        _operationalContent.Controls.Add(_eventsView);
        _operationalContent.Controls.Add(_blacklistView);

        container.Controls.Add(_operationalContent);
        container.Controls.Add(navigation);
        SelectOperationalView(showBlacklist: false);
        return container;
    }

    private void SelectOperationalView(bool showBlacklist)
    {
        if (_eventsView is null || _blacklistView is null)
        {
            return;
        }

        _eventsView.Visible = !showBlacklist;
        _blacklistView.Visible = showBlacklist;
        if (showBlacklist)
        {
            _blacklistView.BringToFront();
        }
        else
        {
            _eventsView.BringToFront();
        }
        SetNavigationButtonSelected(_eventsTabButton, !showBlacklist);
        SetNavigationButtonSelected(_blacklistTabButton, showBlacklist);
    }

    private Control BuildBlacklist()
    {
        var section = CreateSection("OBIECTE CONFIGURATE ÎN BLACKLIST");
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 4)
        };
        toolbar.Controls.Add(CreateButton("+ Adaugă obiect", async (_, _) => await AddBlacklistEntryAsync()));
        toolbar.Controls.Add(CreateButton("Activează", async (_, _) => await SetSelectedBlacklistEnabledAsync(true)));
        toolbar.Controls.Add(CreateButton("Dezactivează", async (_, _) => await SetSelectedBlacklistEnabledAsync(false)));
        var remove = CreateButton("Șterge", async (_, _) => await RemoveSelectedBlacklistEntryAsync());
        remove.ForeColor = RedColor;
        toolbar.Controls.Add(remove);
        toolbar.Controls.Add(CreateButton("Reîncarcă", async (_, _) => await RefreshBlacklistAsync()));
        toolbar.Controls.Add(CreateButton("Exportă CSV", async (_, _) => await ExportBlacklistAsync()));

        _blacklistCountLabel.AutoSize = true;
        _blacklistCountLabel.ForeColor = MutedColor;
        _blacklistCountLabel.Margin = new Padding(14, 8, 0, 0);
        toolbar.Controls.Add(_blacklistCountLabel);
        section.Controls.Add(toolbar);

        ConfigureGrid(_blacklistGrid);
        _blacklistGrid.DataSource = _blacklistSource;
        _blacklistGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _blacklistGrid.MultiSelect = false;
        _blacklistGrid.CellFormatting += (_, eventArgs) =>
        {
            if (_blacklistGrid.Columns[eventArgs.ColumnIndex].Name == "Stare"
                && eventArgs.Value is string state)
            {
                eventArgs.CellStyle.ForeColor = state == "ACTIV" ? RedColor : MutedColor;
            }
        };
        section.Controls.Add(_blacklistGrid);
        _blacklistGrid.BringToFront();
        return section;
    }

    private async Task RefreshDashboardAsync()
    {
        if (_refreshing || _paused)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var status = await _reader.ReadStatusAsync(CancellationToken.None);
            UpdateHistory(status);
            UpdateCards(status);
            UpdateRoutes(status);
            if (_eventsView?.Visible == true
                && DateTimeOffset.UtcNow - _lastEventRefreshUtc >= TimeSpan.FromSeconds(10))
            {
                _events = _reader.ReadRecentEvents();
                _lastEventRefreshUtc = DateTimeOffset.UtcNow;
                ApplyEventFilter();
            }
            if (_blacklistView?.Visible == true)
            {
                await RefreshBlacklistAsync();
            }
            UpdateHealth(status);
            _previousStatus = status;
        }
        catch (Exception exception)
        {
            SetHealth("FĂRĂ DATE", RedColor);
            _alertLabel.Text = exception.Message;
            _alertLabel.ForeColor = RedColor;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void UpdateHistory(FilterStatus status)
    {
        var sampleTime = status.TimestampLocal.LocalDateTime;
        if (_history.Count == 0 || _history[^1].Time != sampleTime)
        {
            _history.Add(new StatusSample(
                sampleTime,
                status.Allowed,
                status.Errors,
                status.Routes.ToDictionary(item => item.Key, item => item.Value.Messages)));
            if (_history.Count > 120)
            {
                _history.RemoveAt(0);
            }
            _chart.SetSamples(_history);
        }
    }

    private void UpdateCards(FilterStatus status)
    {
        var elapsedSeconds = _previousStatus is null
            ? 0
            : Math.Max(0.1, (status.TimestampLocal - _previousStatus.TimestampLocal).TotalSeconds);
        var rate = _previousStatus is null
            ? 0
            : Math.Max(0, status.Allowed - _previousStatus.Allowed) * 60d / elapsedSeconds;

        SetCard("connections", status.TotalConnections.ToString("N0"), $"active: {status.ActiveConnections:N0}");
        SetCard("rate", rate.ToString("N1"), "mesaje permise");
        SetCard("allowed", status.Allowed.ToString("N0"), $"EvMan: {status.AcksRelayed:N0} • verificări locale: {status.ProbesAccepted:N0}");
        SetCard("blocked", status.Blocked.ToString("N0"), $"ACK local: {status.AcksGenerated:N0} • în Monitor: {status.WouldBlock:N0}");
        SetCard("errors", status.Errors.ToString("N0"), DeltaText(status.Errors, _previousStatus?.Errors));
        SetCard("invalid", status.InvalidFrames.ToString("N0"), DeltaText(status.InvalidFrames, _previousStatus?.InvalidFrames));
        SetCard("ack", status.AcksRelayed.ToString("N0"), status.Allowed == status.AcksRelayed + status.ProbesAccepted ? "sincronizat" : "verifică diferența");
    }

    private void UpdateRoutes(FilterStatus status)
    {
        var elapsedSeconds = _previousStatus is null
            ? 0
            : Math.Max(0.1, (status.TimestampLocal - _previousStatus.TimestampLocal).TotalSeconds);
        var rows = new List<object>();
        foreach (var port in status.Routes.Keys.OrderBy(value => int.TryParse(value, out var number) ? number : int.MaxValue))
        {
            status.Routes.TryGetValue(port, out var route);
            RouteStatus? previousRoute = null;
            _previousStatus?.Routes.TryGetValue(port, out previousRoute);
            route ??= new RouteStatus();
            var rate = previousRoute is null
                ? 0
                : Math.Max(0, route.Messages - previousRoute.Messages) * 60d / elapsedSeconds;
            var unresolved = Math.Max(0, route.Messages - route.Allowed - route.Blocked);
            rows.Add(new
            {
                Port = port,
                Ruta = route.Name,
                Format = route.FrameFormat,
                Filtru = route.FilterEnabled ? "ACTIV" : "DOAR TRANSMITE",
                Stare = route.Messages > 0 ? "TRAFIC" : "AȘTEAPTĂ TRAFIC",
                Active = route.ActiveConnections,
                Conexiuni = route.TotalConnections,
                Mesaje = route.Messages,
                Permise = route.Allowed,
                Blocate = route.Blocked,
                ArFiBlocate = route.WouldBlock,
                Probleme = unresolved,
                MesajeMinut = Math.Round(rate, 1)
            });
        }
        var firstVisibleRow = _routeGrid.RowCount > 0 ? _routeGrid.FirstDisplayedScrollingRowIndex : 0;
        var selectedPort = _routeGrid.SelectedRows.Count > 0
            ? _routeGrid.SelectedRows[0].Cells["Port"].Value?.ToString()
            : null;
        _routeGrid.CurrentCell = null;
        _routeSource.DataSource = rows;
        _routeGrid.CurrentCell = null;
        _routeGrid.ClearSelection();
        if (selectedPort is not null)
        {
            foreach (DataGridViewRow row in _routeGrid.Rows)
            {
                if (row.Cells["Port"].Value?.ToString() == selectedPort)
                {
                    row.Selected = true;
                    break;
                }
            }
        }
        RestoreScroll(_routeGrid, firstVisibleRow);
        ColorRouteRows();
    }

    private void UpdateHealth(FilterStatus status)
    {
        _modeBadge.Text = $"MOD {status.Mode.ToUpperInvariant()}";
        _modeBadge.BackColor = status.Mode.Equals("Monitor", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb(30, 64, 175)
            : Color.FromArgb(146, 64, 14);
        _updatedLabel.Text = $"actualizat {status.TimestampLocal:HH:mm:ss}  ";

        var age = DateTimeOffset.Now - status.TimestampLocal;
        var newErrors = _previousStatus is null ? 0 : status.Errors - _previousStatus.Errors;
        var newInvalid = _previousStatus is null ? 0 : status.InvalidFrames - _previousStatus.InvalidFrames;
        if (age > TimeSpan.FromSeconds(20))
        {
            SetHealth("SERVICIU FĂRĂ RĂSPUNS", RedColor);
            SetAlert($"status.json nu a fost actualizat de {age.TotalSeconds:N0} secunde.", RedColor);
        }
        else if (newErrors > 0 || newInvalid > 0)
        {
            SetHealth("ATENȚIE", YellowColor);
            SetAlert($"În ultimul interval: +{newErrors} erori, +{newInvalid} cadre invalide. Verifică tabelul de evenimente.", YellowColor);
        }
        else if (status.Allowed != status.AcksRelayed + status.ProbesAccepted)
        {
            SetHealth("VERIFICĂ ACK", YellowColor);
            SetAlert($"Permise {status.Allowed:N0}, ACK EvMan {status.AcksRelayed:N0}, verificări locale {status.ProbesAccepted:N0}.", YellowColor);
        }
        else if (status.TotalConnections == 0)
        {
            SetHealth("PREGĂTIT", YellowColor);
            SetAlert("Serviciul ascultă; încă nu a primit conexiuni de la PIMA Guard.", MutedColor);
        }
        else
        {
            SetHealth("SĂNĂTOS", GreenColor);
            var last = status.LastMessageAt is null ? "niciun mesaj" : $"ultimul mesaj {status.LastMessageAt:HH:mm:ss}";
            SetAlert($"Contoare exacte • {last} • blacklist: {status.BlacklistCount} • jurnale eșantionate: permise 1/{status.LogSampling.AllowedEveryN}, blocate 1/{status.LogSampling.BlockedEveryN}, erori 1/{status.LogSampling.ErrorEveryN}.", MutedColor);
        }
    }

    private void ApplyEventFilter()
    {
        var search = _searchBox.Text.Trim();
        var selected = _categoryBox.SelectedItem?.ToString() ?? "Toate";
        IEnumerable<EventRow> filtered = _events;
        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(row =>
                row.ObjectCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.RawCode.Contains(search, StringComparison.OrdinalIgnoreCase)
                || row.Event.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        filtered = selected switch
        {
            "Permise" => filtered.Where(row => row.Category == "allowed" && row.Action != "monitor-would-block"),
            "De blocat în Monitor" => filtered.Where(row => row.Action == "monitor-would-block"),
            "Blocate" => filtered.Where(row => row.Category == "blocked"),
            "Erori" => filtered.Where(row => row.Category == "errors"),
            "Cadre invalide" => filtered.Where(row => row.Category == "invalid"),
            _ => filtered
        };

        _visibleEvents = filtered.Take(250).ToArray();
        var firstVisibleRow = _eventGrid.RowCount > 0 ? _eventGrid.FirstDisplayedScrollingRowIndex : 0;
        _eventGrid.CurrentCell = null;
        _eventSource.DataSource = _visibleEvents
            .Select(row => new
            {
                Ora = row.Time.ToString("HH:mm:ss.fff"),
                Tip = TranslateCategory(row),
                Port = row.Port,
                ObiectFinal = row.ObjectCode,
                CodPIMA = row.RawCode,
                DateSemnal = row.Event,
                Sursa = row.Remote,
                AsteptareMs = row.EvManMilliseconds is null ? "" : row.EvManMilliseconds.Value.ToString("N1"),
                Cauza = row.Detail
            })
            .ToList();
        _eventGrid.CurrentCell = null;
        _eventGrid.ClearSelection();
        RestoreScroll(_eventGrid, firstVisibleRow);
        foreach (var (name, heading) in new[]
        {
            ("ObiectFinal", "Obiect final"), ("CodPIMA", "Cod PIMA"),
            ("DateSemnal", "Date semnal"), ("AsteptareMs", "Așteptare .2 (ms)"),
            ("Cauza", "Cauză / detalii")
        })
        {
            if (_eventGrid.Columns[name] is { } column) column.HeaderText = heading;
        }
    }

    private static void RestoreScroll(DataGridView grid, int firstVisibleRow)
    {
        if (firstVisibleRow >= 0 && grid.RowCount > firstVisibleRow)
        {
            try
            {
                grid.FirstDisplayedScrollingRowIndex = firstVisibleRow;
            }
            catch (InvalidOperationException)
            {
                // The grid has fewer displayable rows after resizing.
            }
        }
    }

    private void ShowEventDetails(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _visibleEvents.Count) return;
        var row = _visibleEvents[e.RowIndex];
        var details = $"Ora: {row.Time:yyyy-MM-dd HH:mm:ss.fff}\n" +
            $"Tip: {TranslateCategory(row)} | Port: {row.Port} | Sursa: {row.Remote}\n" +
            $"Obiect final: {row.ObjectCode} | Cod PIMA: {row.RawCode}\n" +
            $"Cauză: {row.Detail}\n\n" +
            $"Cadru hex: {(string.IsNullOrEmpty(row.FrameHex) ? "neînregistrat în această versiune" : row.FrameHex)}\n" +
            $"Cadru text: {row.FrameAscii}";
        MessageBox.Show(this, details, "Detalii mesaj / eroare", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task RefreshBlacklistAsync()
    {
        try
        {
            var entries = await _blacklistStore.LoadAsync(CancellationToken.None);
            var rows = entries.Select(entry =>
            {
                var port = ObjectCodeMapper.ToPort(entry.ObjectCode);
                var numeric = long.TryParse(entry.ObjectCode, out var value) ? value.ToString() : entry.ObjectCode;
                return new
                {
                    CodFinal = entry.ObjectCode,
                    ObiectAndromeda = numeric,
                    Port = port?.ToString() ?? "Necunoscut",
                    CodPIMA = ObjectCodeMapper.ToPimaCode(entry.ObjectCode),
                    Stare = entry.Enabled ? "ACTIV" : "DEZACTIVAT",
                    Motiv = entry.Reason,
                    Nota = entry.Note
                };
            }).ToList();

            var fingerprint = string.Join("\u001F", entries.Select(entry =>
                $"{entry.ObjectCode}\u001E{entry.Enabled}\u001E{entry.Reason}\u001E{entry.Note}"));
            _blacklistCountLabel.Text = $"Total: {entries.Count} • active: {entries.Count(entry => entry.Enabled)}";

            // The automatic two-second refresh must not pull the user back to
            // the selected row while they are reading lower rows in the list.
            if (string.Equals(_blacklistFingerprint, fingerprint, StringComparison.Ordinal))
            {
                return;
            }

            var selectedCode = _blacklistGrid.CurrentRow?.Cells["CodFinal"].Value?.ToString();
            var firstVisibleRow = _blacklistGrid.RowCount > 0
                ? _blacklistGrid.FirstDisplayedScrollingRowIndex
                : -1;

            _blacklistSource.DataSource = rows;
            _blacklistFingerprint = fingerprint;

            if (!string.IsNullOrWhiteSpace(selectedCode))
            {
                var matchingRow = _blacklistGrid.Rows.Cast<DataGridViewRow>()
                    .FirstOrDefault(row => string.Equals(
                        row.Cells["CodFinal"].Value?.ToString(),
                        selectedCode,
                        StringComparison.OrdinalIgnoreCase));
                if (matchingRow is not null)
                {
                    _blacklistGrid.CurrentCell = matchingRow.Cells[0];
                    matchingRow.Selected = true;
                }
            }

            if (firstVisibleRow >= 0 && firstVisibleRow < _blacklistGrid.RowCount)
            {
                _blacklistGrid.FirstDisplayedScrollingRowIndex = firstVisibleRow;
            }
        }
        catch (Exception exception)
        {
            _blacklistCountLabel.Text = $"Eroare blacklist: {exception.Message}";
            _blacklistCountLabel.ForeColor = RedColor;
        }
    }

    private async Task ExportBlacklistAsync()
    {
        try
        {
            var entries = await _blacklistStore.LoadAsync(CancellationToken.None);
            using var dialog = new SaveFileDialog
            {
                Title = "Export listă neagră",
                Filter = "Fișier CSV (*.csv)|*.csv",
                DefaultExt = "csv",
                AddExtension = true,
                FileName = $"blacklist-{DateTime.Now:yyyy-MM-dd}.csv",
                InitialDirectory = _reader.DataDirectory
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            var rows = new List<string>
            {
                "Cod final;Rută;Port;Format;Cod obiect;Stare;Motiv;Notă"
            };
            foreach (var entry in entries.OrderBy(item => item.ObjectCode, StringComparer.OrdinalIgnoreCase))
            {
                var port = ObjectCodeMapper.ToPort(entry.ObjectCode);
                var target = port is null ? null : ObjectCodeMapper.GetTarget(port.Value);
                rows.Add(string.Join(";", [
                    Csv(entry.ObjectCode),
                    Csv(target?.Name ?? "Necunoscut"),
                    Csv(port?.ToString() ?? string.Empty),
                    Csv(target?.FrameFormat ?? string.Empty),
                    Csv(ObjectCodeMapper.ToPimaCode(entry.ObjectCode)),
                    Csv(entry.Enabled ? "ACTIV" : "DEZACTIVAT"),
                    Csv(entry.Reason),
                    Csv(entry.Note)
                ]));
            }

            await File.WriteAllTextAsync(
                dialog.FileName,
                string.Join(Environment.NewLine, rows),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(
                this,
                $"Au fost exportate {entries.Count} obiecte în:\n{dialog.FileName}",
                "Export finalizat",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowBlacklistError(exception);
        }
    }

    private static string Csv(string value) =>
        $"\"{value.Replace("\"", "\"\"")}\"";

    private async Task AddBlacklistEntryAsync()
    {
        if (ObjectCodeMapper.Targets.Count == 0)
        {
            MessageBox.Show("Configure at least one route before adding an object.", "PIMA Signal Filter");
            return;
        }
        using var dialog = new BlacklistEntryDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var objectCode = ObjectCodeMapper.BuildFinalCode(dialog.SelectedPort, dialog.PimaCode);
        if (_previousStatus?.Mode.Equals("Block", StringComparison.OrdinalIgnoreCase) == true)
        {
            var confirmation = MessageBox.Show(
                this,
                $"Filtrul este în modul BLOCK. După adăugare, obiectul {objectCode} va fi blocat în aproximativ două secunde. Continuați?",
                "Confirmare blocare activă",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            await _blacklistStore.UpsertAsync(
                objectCode,
                dialog.Reason,
                dialog.Note,
                CancellationToken.None);
            await RefreshBlacklistAsync();
            MessageBox.Show(
                this,
                $"Obiectul PIMA {dialog.PimaCode}, port {dialog.SelectedPort}, a fost salvat ca {objectCode}.\nServiciul recitește lista automat.",
                "Blacklist actualizat",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowBlacklistError(exception);
        }
    }

    private async Task SetSelectedBlacklistEnabledAsync(bool enabled)
    {
        var objectCode = SelectedBlacklistCode();
        if (objectCode is null)
        {
            return;
        }

        if (enabled && _previousStatus?.Mode.Equals("Block", StringComparison.OrdinalIgnoreCase) == true)
        {
            var confirmation = MessageBox.Show(
                this,
                $"Activarea codului {objectCode} îl va bloca imediat deoarece serviciul este în modul BLOCK. Continuați?",
                "Confirmare activare",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            await _blacklistStore.SetEnabledAsync(objectCode, enabled, CancellationToken.None);
            await RefreshBlacklistAsync();
        }
        catch (Exception exception)
        {
            ShowBlacklistError(exception);
        }
    }

    private async Task RemoveSelectedBlacklistEntryAsync()
    {
        var objectCode = SelectedBlacklistCode();
        if (objectCode is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Ștergeți definitiv obiectul {objectCode} din blacklist?\nVa fi păstrată automat copia blacklist.json.bak.",
            "Confirmare ștergere",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirmation != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _blacklistStore.RemoveAsync(objectCode, CancellationToken.None);
            await RefreshBlacklistAsync();
        }
        catch (Exception exception)
        {
            ShowBlacklistError(exception);
        }
    }

    private string? SelectedBlacklistCode()
    {
        if (_blacklistGrid.CurrentRow?.Cells["CodFinal"].Value is string code)
        {
            return code;
        }

        MessageBox.Show(
            this,
            "Selectați mai întâi un obiect din tabel.",
            "Blacklist",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
        return null;
    }

    private void ShowBlacklistError(Exception exception) => MessageBox.Show(
        this,
        exception.Message,
        "Eroare la modificarea blacklist-ului",
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);

    private void EventGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_eventGrid.Columns[e.ColumnIndex].Name != "Tip" || e.Value is not string value)
        {
            return;
        }

        e.CellStyle.ForeColor = value switch
        {
            "EROARE" => RedColor,
            "CADRU INVALID" => YellowColor,
            "BLOCAT" => YellowColor,
            "AR FI BLOCAT" => YellowColor,
            _ => GreenColor
        };
    }

    private void ColorRouteRows()
    {
        foreach (DataGridViewRow row in _routeGrid.Rows)
        {
            var state = row.Cells["Stare"].Value?.ToString();
            var problems = Convert.ToInt64(row.Cells["Probleme"].Value ?? 0);
            row.Cells["Stare"].Style.ForeColor = state == "ACTIV" ? GreenColor : YellowColor;
            row.Cells["Probleme"].Style.ForeColor = problems > 0 ? RedColor : GreenColor;
        }
    }

    private static string TranslateCategory(EventRow row) => row.Category switch
    {
        "errors" => "EROARE",
        "invalid" => "CADRU INVALID",
        "blocked" => "BLOCAT",
        _ when row.Action == "monitor-would-block" => "AR FI BLOCAT",
        _ => "PERMIS"
    };

    private void AddCard(TableLayoutPanel layout, string key, string title, Color accent, int column)
    {
        var card = new MetricCard(title, accent);
        _cards[key] = card;
        layout.Controls.Add(card, column, 0);
    }

    private void SetCard(string key, string value, string note)
    {
        _cards[key].SetValue(value, note);
    }

    private static string DeltaText(long current, long? previous)
    {
        var delta = previous is null ? 0 : current - previous.Value;
        return delta == 0 ? "fără creștere" : $"+{delta} acum";
    }

    private void SetHealth(string text, Color color)
    {
        _healthBadge.Text = text;
        _healthBadge.BackColor = Color.FromArgb(
            Math.Max(0, color.R - 80),
            Math.Max(0, color.G - 80),
            Math.Max(0, color.B - 80));
        _healthBadge.ForeColor = color;
    }

    private void SetAlert(string text, Color color)
    {
        _alertLabel.Text = text;
        _alertLabel.ForeColor = color;
    }

    private static void ConfigureBadge(Label label, string text, Color color, int width)
    {
        label.Text = text;
        label.BackColor = Color.FromArgb(30, 41, 59);
        label.ForeColor = color;
        label.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        label.TextAlign = ContentAlignment.MiddleCenter;
        label.Size = new Size(width, 34);
        label.Margin = new Padding(5, 0, 5, 0);
    }

    private static Panel CreateSection(string title)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = PanelColor,
            Padding = new Padding(10, 28, 10, 8),
            Margin = new Padding(4)
        };
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 24,
            Location = new Point(10, 4),
            ForeColor = MutedColor,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold)
        });
        return panel;
    }

    private static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.BackgroundColor = PanelColor;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.GridColor = BorderColor;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 41, 59);
        grid.ColumnHeadersDefaultCellStyle.ForeColor = MutedColor;
        grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        grid.DefaultCellStyle.BackColor = PanelColor;
        grid.DefaultCellStyle.ForeColor = TextColor;
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(30, 64, 175);
        grid.DefaultCellStyle.SelectionForeColor = Color.White;
        grid.RowHeadersVisible = false;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.RowTemplate.Height = 27;
    }

    private static Button CreateButton(string text, EventHandler handler)
    {
        var button = new Button { Text = text, AutoSize = true };
        StyleButton(button);
        button.Click += handler;
        return button;
    }

    private static void StyleButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.BackColor = PanelColor;
        button.ForeColor = TextColor;
        button.Height = 31;
        button.Margin = new Padding(4, 1, 4, 1);
        button.Padding = new Padding(8, 0, 8, 0);
    }

    private static void ConfigureNavigationButton(Button button, string text, int left)
    {
        button.Text = text;
        button.Location = new Point(left, 1);
        button.Size = new Size(180, 32);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = BorderColor;
        button.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
    }

    private static void SetNavigationButtonSelected(Button button, bool selected)
    {
        button.BackColor = selected ? Color.FromArgb(30, 64, 175) : PanelColor;
        button.ForeColor = selected ? Color.White : MutedColor;
        button.FlatAppearance.BorderColor = selected ? BlueColor : BorderColor;
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"")
        {
            UseShellExecute = true
        });
    }

    private sealed class MetricCard : Panel
    {
        private readonly Label _value;
        private readonly Label _note;

        public MetricCard(string title, Color accent)
        {
            Dock = DockStyle.Fill;
            BackColor = PanelColor;
            Margin = new Padding(4);
            Padding = new Padding(10, 3, 6, 3);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 3, BackColor = accent });
            Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 15,
                ForeColor = MutedColor,
                Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold)
            });
            _value = new Label
            {
                Text = "—",
                Dock = DockStyle.Top,
                Height = 27,
                ForeColor = accent,
                Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold)
            };
            Controls.Add(_value);
            _value.BringToFront();
            _note = new Label
            {
                Text = "așteaptă date",
                Dock = DockStyle.Bottom,
                Height = 14,
                ForeColor = MutedColor,
                Font = new Font("Segoe UI", 8.5f)
            };
            Controls.Add(_note);
        }

        public void SetValue(string value, string note)
        {
            _value.Text = value;
            _note.Text = note;
        }
    }
}
