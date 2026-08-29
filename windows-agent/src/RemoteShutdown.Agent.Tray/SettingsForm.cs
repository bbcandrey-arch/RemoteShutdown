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

    /// <param name="hideInsteadOfClose">
    /// true (по умолчанию, обычный запуск из трея) — закрытие окна крестиком его просто
    /// прячет, а не завершает процесс, т.к. трей должен продолжать работать. false — для
    /// автономного запуска (`--settings`), где закрытие окна должно завершить приложение.
    /// </param>
    public SettingsForm(AgentDatabase db, SettingsStore settings, bool hideInsteadOfClose = true)
    {
        _db = db;
        _settings = settings;
        _pairedDevices = new PairedDeviceStore(db);

        Text = "Remote Shutdown Agent — настройки";
        Width = 520;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        if (hideInsteadOfClose)
            FormClosing += (_, e) => { e.Cancel = true; Hide(); }; // трей живёт дольше окна — просто прячем его

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildPairingTab());
        tabs.TabPages.Add(BuildDevicesTab());
        tabs.TabPages.Add(BuildGeneralTab());
        Controls.Add(tabs);

        RefreshPairingTab();
        RefreshDevicesTab();
        RefreshGeneralTab();
    }

    // ---- Вкладка "Сопряжение": QR-код + IP/порт, установка PIN ----
    private TabPage BuildPairingTab()
    {
        var page = new TabPage("Сопряжение");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16) };

        _qrPictureBox = new PictureBox { Width = 220, Height = 220, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
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
        refreshQrButton.Click += (_, _) => RefreshPairingTab(regenerateQr: true);
        layout.Controls.Add(refreshQrButton);

        var pinGroup = new GroupBox { Text = "PIN для сопряжения", AutoSize = true, Width = 460, Margin = new Padding(0, 20, 0, 0) };
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
            Text = "PIN хранится только в виде хэша — после установки его нигде нельзя посмотреть " +
                   "повторно, поэтому запишите его сразу. Он нужен один раз, при первом сопряжении " +
                   "телефона.",
        };
        layout.Controls.Add(note);

        page.Controls.Add(layout);
        return page;
    }

    private bool _populatingInterfaces;

    private void RefreshPairingTab(bool regenerateQr = false)
    {
        var port = int.TryParse(_settings.Get(SettingsStore.Keys.Port), out var p) ? p : 54321;
        var interfaces = NetworkInfoService.GetAllIPv4Addresses();
        var preferredIp = _settings.Get(SettingsStore.Keys.PreferredIp);

        _populatingInterfaces = true;
        _interfaceComboBox.Items.Clear();
        foreach (var (interfaceName, address) in interfaces)
            _interfaceComboBox.Items.Add(new InterfaceItem(interfaceName, address));

        var selectedIndex = 0;
        if (preferredIp is not null)
        {
            var match = _interfaceComboBox.Items.Cast<InterfaceItem>().ToList().FindIndex(i => i.Address == preferredIp);
            if (match >= 0) selectedIndex = match;
        }
        if (_interfaceComboBox.Items.Count > 0) _interfaceComboBox.SelectedIndex = selectedIndex;
        _populatingInterfaces = false;

        var host = (_interfaceComboBox.SelectedItem as InterfaceItem)?.Address ?? NetworkInfoService.GetPrimaryIPv4Address();

        var qrDirectory = Path.GetDirectoryName(_db.DbPath) ?? AppContext.BaseDirectory;
        var qrPath = Path.Combine(qrDirectory, "pairing-qr.png");
        if (regenerateQr || !File.Exists(qrPath))
            qrPath = PairingQrService.GenerateAndSave(port, qrDirectory, host) ?? qrPath;

        if (File.Exists(qrPath))
        {
            using var stream = new FileStream(qrPath, FileMode.Open, FileAccess.Read);
            _qrPictureBox.Image = System.Drawing.Image.FromStream(stream);
        }

        _connectionInfoLabel.Text =
            $"IP-адрес: {host ?? "не определён"}\n" +
            $"Порт: {port}\n\n" +
            "Отсканируйте QR-код в приложении на телефоне или введите IP и порт вручную, " +
            "затем PIN, заданный ниже.";

        RefreshCurrentPinLabel();
    }

    /// <summary>Пользователь выбрал другой сетевой интерфейс — запоминаем выбор и перегенерируем QR.</summary>
    private void OnInterfaceSelectionChanged()
    {
        if (_populatingInterfaces) return;
        if (_interfaceComboBox.SelectedItem is not InterfaceItem item) return;

        _settings.Set(SettingsStore.Keys.PreferredIp, item.Address);
        RefreshPairingTab(regenerateQr: true);
    }

    private sealed record InterfaceItem(string InterfaceName, string Address)
    {
        public override string ToString() => $"{InterfaceName} ({Address})";
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

    private void TogglePinVisibility()
    {
        var protectedBase64 = _settings.Get(SettingsStore.Keys.PinPlainProtected);
        if (protectedBase64 is null) return;

        _pinRevealed = !_pinRevealed;
        if (_pinRevealed)
        {
            try
            {
                var pin = System.Text.Encoding.UTF8.GetString(
                    DpapiProtector.UnprotectPin(Convert.FromBase64String(protectedBase64)));
                _currentPinLabel.Text = pin;
                _togglePinVisibilityButton.Text = "Скрыть";
            }
            catch (CryptographicException)
            {
                // DPAPI-блоб защищён на уровне машины: расшифровка не удастся, если БД
                // скопирована на другой ПК (см. DpapiProtector) — это ожидаемо, не баг.
                _currentPinLabel.Text = "не удалось расшифровать";
                _pinRevealed = false;
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
        // Отдельная DPAPI-защищённая копия в открытом виде — только чтобы можно было
        // посмотреть PIN в этом окне позже, не спрашивая пользователя каждый раз заново.
        _settings.Set(SettingsStore.Keys.PinPlainProtected,
            Convert.ToBase64String(DpapiProtector.ProtectPin(System.Text.Encoding.UTF8.GetBytes(pin))));

        RefreshCurrentPinLabel();
        MessageBox.Show(this, "PIN установлен. Он также сохранён здесь — посмотреть его можно кнопкой «Показать».",
            "Remote Shutdown Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _newPinTextBox.Clear();
    }

    // ---- Вкладка "Устройства": список сопряжённых, отзыв доступа ----
    private TabPage BuildDevicesTab()
    {
        var page = new TabPage("Устройства");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Padding = new Padding(16) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _devicesListView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        _devicesListView.Columns.Add("Устройство", 160);
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

    // ---- Вкладка "Общие": порт, тестовый режим, автозагрузка ----
    private TabPage BuildGeneralTab()
    {
        var page = new TabPage("Общие");
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(16), AutoScroll = true };

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

        _autostartCheckBox = new CheckBox { AutoSize = true, Text = "Запускать при входе в Windows" };
        layout.Controls.Add(_autostartCheckBox);

        var saveButton = new Button { Text = "Сохранить", AutoSize = true, Margin = new Padding(0, 20, 0, 0) };
        saveButton.Click += (_, _) => SaveGeneralTab();
        layout.Controls.Add(saveButton);

        page.Controls.Add(layout);
        return page;
    }

    private void RefreshGeneralTab()
    {
        _portUpDown.Value = int.TryParse(_settings.Get(SettingsStore.Keys.Port), out var p) ? p : 54321;
        _testModeCheckBox.Checked = _settings.Get(SettingsStore.Keys.TestMode) != "false";
        _autostartCheckBox.Checked = AutostartService.IsEnabled();
    }

    private void SaveGeneralTab()
    {
        _settings.Set(SettingsStore.Keys.Port, ((int)_portUpDown.Value).ToString());
        _settings.Set(SettingsStore.Keys.TestMode, _testModeCheckBox.Checked ? "true" : "false");
        AutostartService.SetEnabled(_autostartCheckBox.Checked);

        MessageBox.Show(this, "Сохранено.", "Remote Shutdown Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
