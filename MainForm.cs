namespace NightRunDimmer;

internal sealed class MainForm : Form
{
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_TOGGLE_DIM = 1001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;

    private readonly OverlayManager overlays = new();
    private readonly MonitorBrightnessService hardware = new();
    private readonly AppSettings settings = AppSettings.Load();
    private readonly Label dimValueLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label monitorLabel = new();
    private readonly TrackBar dimSlider = new();
    private readonly CheckBox overlayCheck = new();
    private readonly CheckBox hardwareCheck = new();
    private readonly CheckBox preventSleepCheck = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly System.Windows.Forms.Timer hardwareApplyTimer = new();
    private readonly System.Windows.Forms.Timer settingsSaveTimer = new();
    private readonly Icon appIcon;
    private string hardwareStatus = "Hardware DDC/CI is off.";
    private bool hardwareApplyRunning;
    private int pendingHardwareBrightness;
    private bool allowClose;

    public MainForm()
    {
        appIcon = LoadAppIcon();
        Text = "Night Run Dimmer";
        Icon = appIcon;
        MinimumSize = new Size(420, 310);
        Size = new Size(460, 340);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(24, 27, 31);
        ForeColor = Color.FromArgb(235, 239, 245);
        Font = new Font("Segoe UI", 9F);
        TopMost = true;

        BuildUi();
        BuildTray();

        refreshTimer.Interval = 15000;
        refreshTimer.Tick += (_, _) => RefreshMonitorStatus();
        refreshTimer.Start();

        hardwareApplyTimer.Interval = 800;
        hardwareApplyTimer.Tick += (_, _) =>
        {
            hardwareApplyTimer.Stop();
            BeginHardwareApply();
        };

        settingsSaveTimer.Interval = 500;
        settingsSaveTimer.Tick += (_, _) =>
        {
            settingsSaveTimer.Stop();
            settings.Save();
        };

        RefreshMonitorStatus();
        ApplyDim();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotKey(Handle, HOTKEY_TOGGLE_DIM, MOD_CONTROL | MOD_SHIFT, (uint)Keys.D);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            Hide();
            trayIcon.ShowBalloonTip(1800, "Night Run Dimmer", "仍在托盘运行。双击托盘图标可打开。", ToolTipIcon.Info);
            return;
        }

        UnregisterHotKey(Handle, HOTKEY_TOGGLE_DIM);
        settings.Save();
        PowerManager.SetPreventSleep(false);
        overlays.Dispose();
        appIcon.Dispose();
        trayIcon.Dispose();
        refreshTimer.Dispose();
        hardwareApplyTimer.Dispose();
        settingsSaveTimer.Dispose();
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DISPLAYCHANGE)
        {
            RefreshMonitorStatus();
        }
        else if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_TOGGLE_DIM)
        {
            overlayCheck.Checked = !overlayCheck.Checked;
            ApplyDim();
        }

        base.WndProc(ref m);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(16),
            BackColor = BackColor,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(root);

        var header = new Label
        {
            Text = "Night Run Dimmer",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 15F),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        root.Controls.Add(header, 0, 0);

        var sliderPanel = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
        dimValueLabel.Text = "Dim 65%";
        dimValueLabel.Dock = DockStyle.Top;
        dimValueLabel.Height = 26;
        dimValueLabel.Font = new Font("Segoe UI Semibold", 11F);
        dimValueLabel.ForeColor = Color.FromArgb(160, 201, 255);
        dimSlider.Dock = DockStyle.Fill;
        dimSlider.Minimum = 0;
        dimSlider.Maximum = 90;
        dimSlider.TickFrequency = 10;
        dimSlider.Value = 65;
        dimSlider.Scroll += (_, _) => ApplyDim();
        sliderPanel.Controls.Add(dimSlider);
        sliderPanel.Controls.Add(dimValueLabel);
        root.Controls.Add(sliderPanel, 0, 1);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = BackColor,
        };
        overlayCheck.Text = "All-screen visual dim";
        overlayCheck.Checked = true;
        overlayCheck.AutoSize = true;
        overlayCheck.Margin = new Padding(0, 8, 22, 0);
        overlayCheck.CheckedChanged += (_, _) => ApplyDim();
        hardwareCheck.Text = "Hardware DDC/CI";
        hardwareCheck.AutoSize = true;
        hardwareCheck.Margin = new Padding(0, 8, 22, 0);
        hardwareCheck.CheckedChanged += (_, _) => ApplyDim();
        preventSleepCheck.Text = "Keep system awake";
        preventSleepCheck.AutoSize = true;
        preventSleepCheck.Margin = new Padding(0, 8, 0, 0);
        preventSleepCheck.CheckedChanged += (_, _) => ApplyDim();
        options.Controls.Add(overlayCheck);
        options.Controls.Add(hardwareCheck);
        options.Controls.Add(preventSleepCheck);
        root.Controls.Add(options, 0, 2);

        var infoPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(31, 35, 41),
            Padding = new Padding(12),
        };
        monitorLabel.Dock = DockStyle.Top;
        monitorLabel.Height = 42;
        monitorLabel.ForeColor = Color.FromArgb(210, 218, 229);
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = Color.FromArgb(142, 154, 171);
        statusLabel.Text = "Ready.";
        infoPanel.Controls.Add(statusLabel);
        infoPanel.Controls.Add(monitorLabel);
        root.Controls.Add(infoPanel, 0, 3);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = BackColor,
        };
        commands.Controls.Add(MakeButton("Exit", (_, _) =>
        {
            allowClose = true;
            Close();
        }));
        commands.Controls.Add(MakeButton("Restore", (_, _) => RestoreAll()));
        commands.Controls.Add(MakeButton("Night 75%", (_, _) =>
        {
            dimSlider.Value = 75;
            overlayCheck.Checked = true;
            preventSleepCheck.Checked = true;
            ApplyDim();
        }));
        root.Controls.Add(commands, 0, 4);

        dimSlider.Value = Math.Clamp(settings.DimPercent, dimSlider.Minimum, dimSlider.Maximum);
        overlayCheck.Checked = settings.VisualDimEnabled;
        hardwareCheck.Checked = settings.HardwareBrightnessEnabled;
        preventSleepCheck.Checked = settings.PreventSleepEnabled;
    }

    private Button MakeButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Width = 88,
            Height = 30,
            Margin = new Padding(8, 4, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(39, 45, 53),
            ForeColor = Color.FromArgb(238, 242, 248),
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 78, 90);
        button.Click += click;
        return button;
    }

    private void BuildTray()
    {
        trayIcon.Text = "Night Run Dimmer";
        trayIcon.Icon = appIcon;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => ShowPanel();
        trayIcon.ContextMenuStrip = new ContextMenuStrip();
        trayIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => ShowPanel());
        trayIcon.ContextMenuStrip.Items.Add("Toggle dim  Ctrl+Shift+D", null, (_, _) =>
        {
            overlayCheck.Checked = !overlayCheck.Checked;
            ApplyDim();
            ShowPanel();
        });
        trayIcon.ContextMenuStrip.Items.Add("Night 75%", null, (_, _) =>
        {
            dimSlider.Value = 75;
            overlayCheck.Checked = true;
            preventSleepCheck.Checked = true;
            ApplyDim();
        });
        trayIcon.ContextMenuStrip.Items.Add("Restore", null, (_, _) => RestoreAll());
        trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) =>
        {
            allowClose = true;
            Close();
        });
    }

    private void ShowPanel()
    {
        Show();
        WindowState = FormWindowState.Normal;
        TopMost = true;
        BringToFront();
        Activate();
    }

    private void ApplyDim()
    {
        var dim = dimSlider.Value;
        dimValueLabel.Text = $"Dim {dim}%";

        overlays.SetEnabled(overlayCheck.Checked, dim, this);
        overlays.SetDim(dim, this);
        PowerManager.SetPreventSleep(preventSleepCheck.Checked);

        if (hardwareCheck.Checked)
        {
            pendingHardwareBrightness = Math.Clamp(100 - dim, 5, 100);
            hardwareStatus = $"Hardware queued: {pendingHardwareBrightness}%.";
            hardwareApplyTimer.Stop();
            hardwareApplyTimer.Start();
        }
        else
        {
            hardwareApplyTimer.Stop();
            hardwareStatus = "Hardware DDC/CI is off.";
        }

        statusLabel.Text = overlayCheck.Checked
            ? $"Visual dim applies to {Screen.AllScreens.Length} screen(s). {hardwareStatus}"
            : $"Visual dim off. {hardwareStatus}";

        settings.DimPercent = dim;
        settings.VisualDimEnabled = overlayCheck.Checked;
        settings.HardwareBrightnessEnabled = hardwareCheck.Checked;
        settings.PreventSleepEnabled = preventSleepCheck.Checked;
        settingsSaveTimer.Stop();
        settingsSaveTimer.Start();
    }

    private void RestoreAll()
    {
        overlayCheck.Checked = false;
        overlays.SetEnabled(false, dimSlider.Value, this);
        PowerManager.SetPreventSleep(false);
        hardwareApplyTimer.Stop();
        statusLabel.Text = "Restoring visual dim. Hardware restore is running in the background.";
        Task.Run(hardware.Restore).ContinueWith(task =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (task.IsFaulted)
                {
                    hardwareStatus = $"Hardware restore failed: {task.Exception?.GetBaseException().Message}";
                }
                else
                {
                    var result = task.Result;
                    hardwareStatus = result.Attempted == 0
                        ? "No hardware brightness snapshot was active."
                        : $"Restored hardware brightness on {result.Changed}/{result.Attempted} monitor(s).";
                }
                statusLabel.Text = $"Visual dim off. {hardwareStatus}";
            });
        });
    }

    private void RefreshMonitorStatus()
    {
        overlays.Refresh(this);

        monitorLabel.Text = $"{Screen.AllScreens.Length} Windows screen(s). DDC/CI is opt-in and runs in background.";
    }

    private void BeginHardwareApply()
    {
        if (!hardwareCheck.Checked || hardwareApplyRunning) return;

        var brightness = pendingHardwareBrightness;
        hardwareApplyRunning = true;
        hardwareStatus = $"Hardware applying {brightness}%...";
        statusLabel.Text = overlayCheck.Checked
            ? $"Visual dim applies to {Screen.AllScreens.Length} screen(s). {hardwareStatus}"
            : $"Visual dim off. {hardwareStatus}";

        Task.Run(() => hardware.SetBrightnessPercent(brightness)).ContinueWith(task =>
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                hardwareApplyRunning = false;
                if (task.IsFaulted)
                {
                    hardwareStatus = $"Hardware failed: {task.Exception?.GetBaseException().Message}";
                }
                else
                {
                    var result = task.Result;
                    hardwareStatus = $"Hardware target {brightness}%: {result.Changed}/{result.Attempted} changed, {result.Failed} failed.";
                }

                statusLabel.Text = overlayCheck.Checked
                    ? $"Visual dim applies to {Screen.AllScreens.Length} screen(s). {hardwareStatus}"
                    : $"Visual dim off. {hardwareStatus}";

                if (hardwareCheck.Checked && pendingHardwareBrightness != brightness)
                {
                    hardwareApplyTimer.Start();
                }
            });
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);

    private static Icon LoadAppIcon()
    {
        try
        {
            var extracted = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted is not null) return extracted;
        }
        catch
        {
            // Fall back below.
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
