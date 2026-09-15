using System.Security.Cryptography;
using RemoteShutdown.Agent.Core.Network;
using RemoteShutdown.Agent.Core.Pairing;
using RemoteShutdown.Agent.Core.Security;
using RemoteShutdown.Agent.Core.Storage;

namespace RemoteShutdown.Agent.Tray;

/// <summary>
/// Единственный UI агента, которого не было раньше (пейринг настраивался прямой правкой
/// SQLite): QR-код + все параметры подключения, список сопряжённых устройств с отзывом,
/// тестовый режим и автозагрузка. См. docs/roadmap.md, "UX опасных команд и управление
/// доступом".
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AgentDatabase _db;
    private readonly SettingsStore _settings;
    private readonly PairedDeviceStore _pairedDevices;
    private readonly TaskLogStore _taskLog;

    private TabControl _tabs = null!;
    private ListView _taskLogListView = null!;
    private PictureBox _qrPictureBox = null!;
    private Label _connectionInfoLabel = null!;
    private ComboBox _interfaceComboBox = null!;
    private Label _currentPinLabel = null!;
    private Button _togglePinVisibilityButton = null!;
    private bool _pinRevealed;
    private TextBox _newPinTextBox = null!;
    private ListView _devicesListView = null!;
    private NumericUpDown _portUpDown = null!;
    private CheckBox _testModeCheckBox = null!;
    private CheckBox _autostartCheckBox = null!;
    private TextBox _deviceNameTextBox = null!;
    private Label _firewallStatusLabel = null!;
    private bool _allowRealClose;

    /// <summary>
    /// Вызывается перед Application.Exit() (см. TrayApplicationContext.ExitApplication) —
    /// без этого FormClosing отменял бы закрытие этого окна и тем самым гасил весь выход
    /// из приложения, если окно настроек было открыто в момент "Выход" из трея.
    /// </summary>
    public void AllowRealClose() => _allowRealClose = true;

    /// <summary>См. docs/user-guide.md — та же ссылка используется и в мобильном
    /// приложении (AppInfo.repoUrl/userGuideUrl).</summary>
    private const string RepoUrl = "https://github.com/bbcandrey-arch/RemoteShutdown";
    private const string UserGuideUrl = RepoUrl + "/blob/main/docs/user-guide.md";

    /// <summary>
    /// Версия из Directory.Build.props (Version/Authors) — та же информация, что видна в
    /// свойствах exe в Проводнике (Подробно), но и прямо в UI, чтобы не искать. Читаем из
    /// FileVersionInfo (ProductVersion), а не AssemblyVersion — тот всегда дополняется до
    /// четырёх чисел ("1.1.0.0"), тогда как ProductVersion отражает Version как есть.
    /// Берём путь из Environment.ProcessPath (не Assembly.Location) — при публикации как
    /// single-file (см. distrib/) Assembly.Location всегда пустая строка.
    /// </summary>
    private static string AppVersion
    {
        get
        {
            if (Environment.ProcessPath is not { } path) return "?";
            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "?";
            // .NET SDK дописывает к ProductVersion хэш коммита ("1.1.0+abcdef...") —
            // это для трассировки сборки, пользователю в UI он не нужен.
            var plusIndex = version.IndexOf('+');
            return plusIndex >= 0 ? version[..plusIndex] : version;
        }
    }

    /// <param name="hideInsteadOfClose">
    /// true (по умолчанию, обычный запуск из трея) — закрытие окна крестиком его просто
    /// прячет, а не завершает процесс, т.к. трей должен продолжать работать. false — для
    /// автономного запуска (`--settings`), где закрытие окна должно завершить приложение.
    /// </param>
    public SettingsForm(AgentDatabase db, SettingsStore settings, bool hideInsteadOfClose = true, TaskLogStore? taskLog = null)
    {
        _db = db;
        _settings = settings;
        _pairedDevices = new PairedDeviceStore(db);
        _taskLog = taskLog ?? new TaskLogStore(db);

        Text = $"Remote Shutdown Agent — настройки (v{AppVersion})";
        Icon = AppIcon.Load(); // без этого WinForms подставляет generic-иконку формы в заголовке окна
        StartPosition = FormStartPosition.CenterScreen;
        if (hideInsteadOfClose)
        {
            // трей живёт дольше окна — просто прячем его при закрытии крестиком. НО:
            // это же самое e.Cancel=true срабатывает и на попытку Application.Exit()
            // закрыть это окно при выходе из трея через контекстное меню — если
            // окно настроек было открыто в этот момент, отмена закрытия гасила ВЕСЬ
            // Application.Exit() целиком (сам процесс не завершался, хотя иконка в
            // трее уже пропадала — см. TrayApplicationContext.ExitApplication). Флаг
            // _allowRealClose снимает это исключение именно для выхода из приложения.
            FormClosing += (_, e) =>
            {
                if (_allowRealClose) return;
                e.Cancel = true;
                Hide();
            };
        }

        _tabs = new TabControl { Dock = DockStyle.Fill };
        _tabs.TabPages.Add(BuildPairingTab());
        _tabs.TabPages.Add(BuildDevicesTab());
        _tabs.TabPages.Add(BuildTaskLogTab());
        _tabs.TabPages.Add(BuildGeneralTab());
        _tabs.TabPages.Add(BuildHelpTab());
        Controls.Add(_tabs);

        RefreshPairingTab();
        RefreshDevicesTab();
        RefreshTaskLogTab();
        RefreshGeneralTab();

        // Разумный размер по умолчанию для самого первого кадра — PreferredSize контролов
        // до создания хэндла окна и первого прохода layout считается ненадёжно (заниженно),
        // поэтому точная подгонка происходит в SizeToFitContent() по событию Shown, когда
        // реальные метрики шрифтов/DPI уже применены. Без этой начальной оценки окно на
        // мгновение показалось бы маленьким/дефолтным до первого Shown.
        ClientSize = new System.Drawing.Size(620, 820);
        Shown += (_, _) => SizeToFitContent();
    }

    /// <summary>
    /// Подбирает размер окна под реальное содержимое вкладки "Сопряжение" (самая
    /// высокая — QR-код + группа PIN), а не держит фиксированные Width/Height "на
    /// глаз": раньше это либо обрезало контент (нужен был AutoScroll как костыль), либо
    /// оставляло лишнее пустое место. Ширина/высота других вкладок (списки устройств и
    /// журнала) подстраиваются под то же окно через Dock=Fill — компактно и без полос
    /// прокрутки при обычном разрешении экрана.
    /// Вызывается по Shown (не из конструктора) — до появления хэндла окна WinForms
    /// считает PreferredSize по неточным метрикам (без реального прохода layout/DPI),
    /// из-за чего окно раньше получалось у́же нужного при первом расчёте в конструкторе.
    /// AutoScroll на вкладках остаётся как отдельная защита на случай маленького экрана
    /// (см. ограничение по Screen.WorkingArea ниже), а не основной механизм.
    /// </summary>
    private void SizeToFitContent()
    {
        var pairingContent = _tabs.TabPages[0].Controls[0]; // TableLayoutPanel, AutoSize=true — см. BuildPairingTab
        var preferred = pairingContent.PreferredSize;

        var width = Math.Max(600, preferred.Width + 40);
        var height = Math.Max(560, preferred.Height + _tabs.ItemSize.Height + 50);

        var workingArea = Screen.FromControl(this).WorkingArea;
        width = Math.Min(width, workingArea.Width - 40);
        height = Math.Min(height, workingArea.Height - 40);

        ClientSize = new System.Drawing.Size(width, height);
        MinimumSize = new System.Drawing.Size(Math.Min(width, 560), Math.Min(height, 520));
    }

    // ---- Вкладка "Сопряжение": QR-код + IP/порт, установка PIN ----
    private TabPage BuildPairingTab()
    {
        // AutoScroll на самой вкладке + AutoSize (не Dock=Fill) на layout: если контент не
        // влезает по высоте, появляется скроллбар вместо того, чтобы нижние кнопки/текст
        // просто обрезались, пока пользователь вручную не растянет окно.
        var page = new TabPage("Сопряжение") { AutoScroll = true };
        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(16) };

        // Первое, что видит человек в настройках, — эта вкладка (открывается по умолчанию),
        // поэтому ссылка на инструкцию тут, а не только на отдельной вкладке "Помощь" в
        // конце списка, до которой ещё нужно догадаться долистать.
        var helpLink = new LinkLabel { AutoSize = true, Text = "Как это работает? (инструкция)", Margin = new Padding(0, 0, 0, 8) };
        helpLink.LinkClicked += (_, _) => OpenUrl(UserGuideUrl);
        layout.Controls.Add(helpLink);

        _qrPictureBox = new PictureBox { Width = 300, Height = 300, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
        layout.Controls.Add(_qrPictureBox);

        var interfacePanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        interfacePanel.Controls.Add(new Label { Text = "Сетевой интерфейс:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        _interfaceComboBox = new ComboBox { Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
        _interfaceComboBox.SelectedIndexChanged += (_, _) => OnInterfaceSelectionChanged();
        interfacePanel.Controls.Add(_interfaceComboBox);
        layout.Controls.Add(interfacePanel);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = System.Drawing.SystemColors.GrayText,
            Text = "Если на ПК несколько сетей (Wi-Fi, Ethernet, VPN) — выберите ту, что в одной " +
                   "Wi-Fi сети с телефоном.",
            Margin = new Padding(0, 2, 0, 8),
        });

        _connectionInfoLabel = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(460, 0), Margin = new Padding(0, 4, 0, 12) };
        layout.Controls.Add(_connectionInfoLabel);

        var refreshQrButton = new Button { Text = "Обновить QR-код", AutoSize = true };
        refreshQrButton.Click += (_, _) => RefreshPairingTab();
        layout.Controls.Add(refreshQrButton);

        var pinGroup = new GroupBox { Text = "PIN для сопряжения", AutoSize = true, Width = 460, Margin = new Padding(0, 12, 0, 0) };
        var pinGroupLayout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };

        var currentPinPanel = new FlowLayoutPanel { AutoSize = true };
        currentPinPanel.Controls.Add(new Label { Text = "Текущий PIN:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        _currentPinLabel = new Label { AutoSize = true, Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold), Margin = new Padding(0, 6, 8, 0) };
        currentPinPanel.Controls.Add(_currentPinLabel);
        _togglePinVisibilityButton = new Button { Text = "Показать", AutoSize = true };
        _togglePinVisibilityButton.Click += (_, _) => TogglePinVisibility();
        currentPinPanel.Controls.Add(_togglePinVisibilityButton);
        pinGroupLayout.Controls.Add(currentPinPanel);

        var newPinPanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        newPinPanel.Controls.Add(new Label { Text = "Новый PIN:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        _newPinTextBox = new TextBox { Width = 140, PlaceholderText = "например 483920" };
        var setPinButton = new Button { Text = "Установить", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        setPinButton.Click += (_, _) => SetNewPin();
        newPinPanel.Controls.Add(_newPinTextBox);
        newPinPanel.Controls.Add(setPinButton);
        pinGroupLayout.Controls.Add(newPinPanel);

        pinGroup.Controls.Add(pinGroupLayout);
        layout.Controls.Add(pinGroup);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = System.Drawing.SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 0),
            Text = "PIN нужен один раз, при первом сопряжении телефона. Посмотреть его снова можно " +
                   "кнопкой «Показать» выше — но только если он был установлен через это окно. " +
                   "Если PIN был задан другим способом (например, в более старой версии " +
                   "приложения), это окно про него не знает — он всё ещё действует, просто здесь " +
                   "не отображается; установите новый, если забыли старый.",
        };
        layout.Controls.Add(note);

        page.Controls.Add(layout);
        return page;
    }

    private bool _populatingInterfaces;

    private void RefreshPairingTab()
    {
        var port = int.TryParse(_settings.Get(SettingsStore.Keys.Port), out var p) ? p : 55765;
        var interfaces = NetworkInfoService.GetAllIPv4Addresses();
        var preferredIp = _settings.Get(SettingsStore.Keys.PreferredIp);

        _populatingInterfaces = true;
        _interfaceComboBox.Items.Clear();
        foreach (var (interfaceName, address) in interfaces)
            _interfaceComboBox.Items.Add(new InterfaceItem(interfaceName, address));
        ResizeInterfaceComboBoxToFitItems();

        var selectedIndex = 0;
        if (preferredIp is not null)
        {
            var match = _interfaceComboBox.Items.Cast<InterfaceItem>().ToList().FindIndex(i => i.Address == preferredIp);
            if (match >= 0) selectedIndex = match;
        }
        if (_interfaceComboBox.Items.Count > 0) _interfaceComboBox.SelectedIndex = selectedIndex;
        _populatingInterfaces = false;

        var host = (_interfaceComboBox.SelectedItem as InterfaceItem)?.Address ?? NetworkInfoService.GetPrimaryIPv4Address();

        // QR всегда перегенерируется (не только по кнопке или при отсутствии файла) — он
        // должен отражать актуальный PIN сразу после SetNewPin(), а не только host/port.
        var qrDirectory = Path.GetDirectoryName(_db.DbPath) ?? AppContext.BaseDirectory;
        var qrPath = Path.Combine(qrDirectory, "pairing-qr.png");
        var deviceName = _settings.Get(SettingsStore.Keys.DeviceName) ?? Environment.MachineName;
        qrPath = PairingQrService.GenerateAndSave(port, qrDirectory, host, TryGetCurrentPlainPin(), deviceName) ?? qrPath;

        if (File.Exists(qrPath))
        {
            // Читаем файл в память и грузим Bitmap из MemoryStream (а не через Image.FromStream
            // поверх FileStream) — иначе GDI+ держит файл открытым до Dispose картинки, и
            // повторная генерация QR (File.WriteAllBytes в PairingQrService) падает с
            // IOException «файл занят другим процессом». Это и было причиной нерабочей
            // кнопки «Обновить QR-код».
            var bytes = File.ReadAllBytes(qrPath);
            using var memoryStream = new MemoryStream(bytes);
            var newImage = System.Drawing.Image.FromStream(memoryStream);
            var oldImage = _qrPictureBox.Image;
            _qrPictureBox.Image = newImage;
            oldImage?.Dispose();
        }

        var pinKnown = TryGetCurrentPlainPin() is not null;
        _connectionInfoLabel.Text =
            $"IP-адрес: {host ?? "не определён"}\n" +
            $"Порт: {port}\n\n" +
            (pinKnown
                ? "QR-код уже содержит IP, порт и PIN — отсканируйте его в приложении, вводить " +
                  "ничего не придётся."
                : "PIN не зашит в QR (задайте его ниже) — отсканируйте IP и порт, затем введите PIN " +
                  "вручную, либо введите всё вручную.");

        RefreshCurrentPinLabel();
    }

    /// <summary>Пользователь выбрал другой сетевой интерфейс — запоминаем выбор и перегенерируем QR.</summary>
    private void OnInterfaceSelectionChanged()
    {
        if (_populatingInterfaces) return;
        if (_interfaceComboBox.SelectedItem is not InterfaceItem item) return;

        _settings.Set(SettingsStore.Keys.PreferredIp, item.Address);
        RefreshPairingTab();
    }

    private sealed record InterfaceItem(string InterfaceName, string Address)
    {
        public override string ToString() => $"{InterfaceName} ({Address})";
    }

    /// <summary>
    /// DropDownList не переносит и не сокращает текст многоточием — при длинных именах
    /// интерфейсов (Wi-Fi адаптеры, VPN-клиенты и т.п.) IP-адрес в скобках просто не
    /// помещался в поле фиксированной ширины и был не виден (issue #1). Подбираем ширину
    /// под самый длинный пункт списка вместо фиксированного значения "на глаз".
    /// </summary>
    private void ResizeInterfaceComboBoxToFitItems()
    {
        var maxTextWidth = _interfaceComboBox.Items.Cast<object>()
            .Select(item => TextRenderer.MeasureText(item.ToString(), _interfaceComboBox.Font).Width)
            .DefaultIfEmpty(0)
            .Max();

        // + стрелка раскрытия списка и внутренние отступы поля
        _interfaceComboBox.Width = Math.Max(320, maxTextWidth + SystemInformation.VerticalScrollBarWidth + 24);
    }

    private void RefreshCurrentPinLabel()
    {
        _pinRevealed = false;
        _togglePinVisibilityButton.Text = "Показать";

        var protectedBase64 = _settings.Get(SettingsStore.Keys.PinPlainProtected);
        if (protectedBase64 is null)
        {
            _currentPinLabel.Text = "не установлен";
            _togglePinVisibilityButton.Enabled = false;
            return;
        }

        _togglePinVisibilityButton.Enabled = true;
        _currentPinLabel.Text = "••••••";
    }

    /// <summary>
    /// Расшифровывает сохранённую DPAPI-копию PIN, если она есть — используется и для
    /// показа в UI (TogglePinVisibility), и чтобы зашить PIN прямо в QR-код
    /// (RefreshPairingTab), раз он всё равно известен агенту в открытом виде.
    /// </summary>
    private string? TryGetCurrentPlainPin()
    {
        var protectedBase64 = _settings.Get(SettingsStore.Keys.PinPlainProtected);
        if (protectedBase64 is null) return null;

        try
        {
            return System.Text.Encoding.UTF8.GetString(
                DpapiProtector.UnprotectPin(Convert.FromBase64String(protectedBase64)));
        }
        catch (CryptographicException)
        {
            // DPAPI-блоб защищён на уровне машины: расшифровка не удастся, если БД
            // скопирована на другой ПК (см. DpapiProtector) — это ожидаемо, не баг.
            return null;
        }
    }

    private void TogglePinVisibility()
    {
        if (_settings.Get(SettingsStore.Keys.PinPlainProtected) is null) return;

        _pinRevealed = !_pinRevealed;
        if (_pinRevealed)
        {
            var pin = TryGetCurrentPlainPin();
            if (pin is null)
            {
                _currentPinLabel.Text = "не удалось расшифровать";
                _pinRevealed = false;
            }
            else
            {
                _currentPinLabel.Text = pin;
                _togglePinVisibilityButton.Text = "Скрыть";
            }
        }
        else
        {
            _currentPinLabel.Text = "••••••";
            _togglePinVisibilityButton.Text = "Показать";
        }
    }

    private void SetNewPin()
    {
        var pin = _newPinTextBox.Text.Trim();
        if (pin.Length < 4)
        {
            MessageBox.Show(this, "PIN должен быть не короче 4 символов.", "Remote Shutdown Agent",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.Set(SettingsStore.Keys.PinHash, PinHasher.Hash(pin));
        // Отдельная DPAPI-защищённая копия в открытом виде — чтобы можно было посмотреть
        // PIN в этом окне позже и зашить его в QR-код (RefreshPairingTab), не спрашивая
        // пользователя каждый раз заново.
        _settings.Set(SettingsStore.Keys.PinPlainProtected,
            Convert.ToBase64String(DpapiProtector.ProtectPin(System.Text.Encoding.UTF8.GetBytes(pin))));

        RefreshPairingTab(); // перегенерирует QR уже с новым PIN внутри
        MessageBox.Show(this, "PIN установлен и зашит в QR-код выше. Посмотреть его снова можно кнопкой «Показать».",
            "Remote Shutdown Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _newPinTextBox.Clear();
    }

    // ---- Вкладка "Устройства": список сопряжённых, отзыв доступа ----
    private TabPage BuildDevicesTab()
    {
        var page = new TabPage("Устройства") { AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _devicesListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        _devicesListView.Columns.Add("Устройство", 150);
        _devicesListView.Columns.Add("Платформа", 90);
        _devicesListView.Columns.Add("Версия приложения", 130);
        _devicesListView.Columns.Add("Сопряжено", 110);
        _devicesListView.Columns.Add("Последний раз онлайн", 130);
        _devicesListView.Columns.Add("Статус", 80);
        layout.Controls.Add(_devicesListView, 0, 0);

        var buttonsPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var refreshButton = new Button { Text = "Обновить список", AutoSize = true };
        refreshButton.Click += (_, _) => RefreshDevicesTab();
        var revokeButton = new Button { Text = "Отозвать доступ", AutoSize = true, Margin = new Padding(8, 0, 0, 0) };
        revokeButton.Click += (_, _) => RevokeSelectedDevice();
        buttonsPanel.Controls.Add(refreshButton);
        buttonsPanel.Controls.Add(revokeButton);
        layout.Controls.Add(buttonsPanel, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private void RefreshDevicesTab()
    {
        _devicesListView.Items.Clear();
        foreach (var device in _pairedDevices.ListAll())
        {
            var item = new ListViewItem(device.DeviceName) { Tag = device.ClientId };
            item.SubItems.Add(device.Platform ?? "—");
            item.SubItems.Add(device.AppVersion ?? "—");
            item.SubItems.Add(device.PairedAtUtc.ToLocalTime().ToString("dd.MM.yy HH:mm"));
            item.SubItems.Add(device.LastSeenUtc?.ToLocalTime().ToString("dd.MM.yy HH:mm") ?? "—");
            item.SubItems.Add(device.Revoked ? "Отозван" : "Активен");
            _devicesListView.Items.Add(item);
        }
    }

    private void RevokeSelectedDevice()
    {
        if (_devicesListView.SelectedItems.Count == 0) return;
        var clientId = (string)_devicesListView.SelectedItems[0].Tag!;
        var deviceName = _devicesListView.SelectedItems[0].Text;

        var confirm = MessageBox.Show(this, $"Отозвать доступ у устройства «{deviceName}»?", "Remote Shutdown Agent",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _pairedDevices.Revoke(clientId);
        RefreshDevicesTab();
    }

    // ---- Вкладка "Журнал": список задач, прилетевших с телефона (docs/roadmap.md,
    // "Уведомления и журнал задач") — те же события, о которых трей показывает
    // баллон-уведомления (TrayApplicationContext.PollTaskLog), но с историей. ----
    private TabPage BuildTaskLogTab()
    {
        var page = new TabPage("Журнал") { AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _taskLogListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        _taskLogListView.Columns.Add("Время", 130);
        _taskLogListView.Columns.Add("Описание", 340);
        _taskLogListView.Columns.Add("IP клиента", 110);
        layout.Controls.Add(_taskLogListView, 0, 0);

        var refreshButton = new Button { Text = "Обновить", AutoSize = true };
        refreshButton.Click += (_, _) => RefreshTaskLogTab();
        layout.Controls.Add(refreshButton, 0, 1);

        page.Controls.Add(layout);
        return page;
    }

    private void RefreshTaskLogTab()
    {
        _taskLogListView.Items.Clear();
        foreach (var entry in _taskLog.ListRecent(200))
        {
            var item = new ListViewItem(entry.OccurredAtUtc.ToLocalTime().ToString("dd.MM.yy HH:mm:ss"));
            item.SubItems.Add(entry.Description);
            item.SubItems.Add(entry.ClientIp ?? "—");
            if (entry.Kind == "error") item.ForeColor = System.Drawing.Color.DarkRed;
            _taskLogListView.Items.Add(item);
        }
    }

    /// <summary>Вызывается треем (TrayApplicationContext.PollTaskLog) при появлении новых
    /// записей — обновляет список, только если окно сейчас открыто на вкладке "Журнал",
    /// чтобы не дёргать UI впустую, если пользователь смотрит другую вкладку/окно скрыто.</summary>
    public void RefreshTaskLogIfVisible()
    {
        if (Visible && _tabs.SelectedTab?.Text == "Журнал")
            RefreshTaskLogTab();
    }

    // ---- Вкладка "Общие": порт, тестовый режим, автозагрузка ----
    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("Общие");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), AutoScroll = true };

        var namePanel = new FlowLayoutPanel { AutoSize = true };
        namePanel.Controls.Add(new Label { Text = "Имя ПК:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        _deviceNameTextBox = new TextBox { Width = 220 };
        namePanel.Controls.Add(_deviceNameTextBox);
        layout.Controls.Add(namePanel);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = System.Drawing.SystemColors.GrayText,
            Text = "Под этим именем ПК виден в приложении на телефоне (заголовок экрана, список ПК, " +
                   "если их сопряжено несколько) и в QR-коде на вкладке «Сопряжение». По умолчанию — " +
                   "сетевое имя этого компьютера.",
            Margin = new Padding(0, 0, 0, 16),
        });

        var portPanel = new FlowLayoutPanel { AutoSize = true };
        portPanel.Controls.Add(new Label { Text = "Порт агента:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
        _portUpDown = new NumericUpDown { Minimum = 1024, Maximum = 65535, Width = 90 };
        portPanel.Controls.Add(_portUpDown);
        layout.Controls.Add(portPanel);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = System.Drawing.SystemColors.GrayText,
            Text = "Изменение порта применится после перезапуска агента (пункт «Перезапустить агент» в меню трея).",
            Margin = new Padding(0, 0, 0, 16),
        });

        _testModeCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Тестовый режим (вместо выключения/перезагрузки/сна — запуск калькулятора)",
        };
        layout.Controls.Add(_testModeCheckBox);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = System.Drawing.Color.DarkRed,
            Text = "Выключайте тестовый режим только на одноразовой тестовой машине/ВМ — " +
                   "иначе команды из приложения реально выключат/перезагрузят этот ПК. " +
                   "См. docs/security.md.",
            Margin = new Padding(0, 0, 0, 16),
        });

        _autostartCheckBox = new CheckBox { AutoSize = true, Text = "Запускать при входе в Windows", Margin = new Padding(0, 0, 0, 16) };
        layout.Controls.Add(_autostartCheckBox);

        var firewallGroup = new GroupBox { Text = "Брандмауэр Windows", AutoSize = true, Width = 460 };
        var firewallLayout = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(8) };
        _firewallStatusLabel = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        firewallLayout.Controls.Add(_firewallStatusLabel);
        var firewallButton = new Button { Text = "Добавить правило в брандмауэр", AutoSize = true };
        firewallButton.Click += (_, _) => AddFirewallRule();
        firewallLayout.Controls.Add(firewallButton);
        firewallLayout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(430, 0),
            ForeColor = System.Drawing.SystemColors.GrayText,
            Margin = new Padding(0, 8, 0, 0),
            Text = "Разрешает входящие подключения на порт агента — без этого правила Windows " +
                   "может молча блокировать телефон в той же Wi-Fi сети. Потребует подтверждения " +
                   "прав администратора (UAC).",
        });
        firewallGroup.Controls.Add(firewallLayout);
        layout.Controls.Add(firewallGroup);

        var saveButton = new Button { Text = "Сохранить", AutoSize = true, Margin = new Padding(0, 20, 0, 0) };
        saveButton.Click += (_, _) => SaveGeneralTab();
        layout.Controls.Add(saveButton);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
            Margin = new Padding(0, 24, 0, 0),
            Text = $"Remote Shutdown Agent v{AppVersion} · vol.and",
        });

        page.Controls.Add(layout);
        return page;
    }

    /// <summary>
    /// Краткая справка прямо в приложении + ссылка на полную инструкцию на GitHub —
    /// раньше единственная помощь была в комментариях кода, для пользователя (не
    /// разработчика) этого недостаточно. См. docs/user-guide.md.
    /// </summary>
    private TabPage BuildHelpTab()
    {
        var page = new TabPage("Помощь");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), AutoScroll = true };

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
            Text = "Как подключить телефон",
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            Margin = new Padding(0, 0, 0, 16),
            Text = "1. Установите приложение на телефон (см. ссылку ниже).\n" +
                   "2. В приложении — «Сканировать QR-код» и наведите камеру на QR-код на " +
                   "вкладке «Сопряжение» этого окна.\n" +
                   "3. Либо введите IP и порт этого ПК вручную и PIN оттуда же (кнопка «Показать»).\n" +
                   "4. Телефон и ПК должны быть в одной Wi-Fi сети.",
        });

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
            Text = "Не получается подключиться",
            Margin = new Padding(0, 0, 0, 8),
        });
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            Margin = new Padding(0, 0, 0, 16),
            Text = "• Проверьте, что телефон и ПК в одной сети (не гостевой Wi-Fi).\n" +
                   "• Нажмите «Добавить правило в брандмауэр» на вкладке «Общие».\n" +
                   "• Убедитесь, что служба агента запущена (значок в трее).",
        });

        var guideLink = new LinkLabel { AutoSize = true, Text = "Полная инструкция на GitHub", Margin = new Padding(0, 0, 0, 4) };
        guideLink.LinkClicked += (_, _) => OpenUrl(UserGuideUrl);
        layout.Controls.Add(guideLink);

        var repoLink = new LinkLabel { AutoSize = true, Text = "Репозиторий проекта / скачать приложения", Margin = new Padding(0, 0, 0, 16) };
        repoLink.LinkClicked += (_, _) => OpenUrl(RepoUrl);
        layout.Controls.Add(repoLink);

        layout.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = System.Drawing.SystemColors.GrayText,
            Text = $"Remote Shutdown Agent v{AppVersion} · vol.and",
        });

        page.Controls.Add(layout);
        return page;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Нет браузера по умолчанию/что-то пошло не так — не критично, ссылка всё
            // равно видна текстом в LinkLabel.
        }
    }

    private void AddFirewallRule()
    {
        var port = (int)_portUpDown.Value;
        var ok = FirewallRuleService.AddOrUpdateRule(port, out var message);
        MessageBox.Show(this, message, "Remote Shutdown Agent",
            MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        RefreshFirewallStatus();
    }

    private void RefreshFirewallStatus()
    {
        _firewallStatusLabel.Text = FirewallRuleService.RuleExists()
            ? "Правило уже добавлено."
            : "Правило ещё не добавлено.";
    }

    private void RefreshGeneralTab()
    {
        _deviceNameTextBox.Text = _settings.Get(SettingsStore.Keys.DeviceName) ?? Environment.MachineName;
        _portUpDown.Value = int.TryParse(_settings.Get(SettingsStore.Keys.Port), out var p) ? p : 55765;
        _testModeCheckBox.Checked = _settings.Get(SettingsStore.Keys.TestMode) != "false";
        _autostartCheckBox.Checked = AutostartService.IsEnabled();
        RefreshFirewallStatus();
    }

    private void SaveGeneralTab()
    {
        var deviceName = _deviceNameTextBox.Text.Trim();
        _settings.Set(SettingsStore.Keys.DeviceName, deviceName.Length > 0 ? deviceName : Environment.MachineName);
        _settings.Set(SettingsStore.Keys.Port, ((int)_portUpDown.Value).ToString());
        _settings.Set(SettingsStore.Keys.TestMode, _testModeCheckBox.Checked ? "true" : "false");
        AutostartService.SetEnabled(_autostartCheckBox.Checked);

        RefreshGeneralTab();
        RefreshPairingTab(); // имя ПК зашито в QR — перегенерировать, чтобы новый QR был актуален сразу

        MessageBox.Show(this, "Сохранено.", "Remote Shutdown Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
