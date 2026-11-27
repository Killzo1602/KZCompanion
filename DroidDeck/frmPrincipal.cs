using System;
using System.Diagnostics;
using System.Windows.Forms;
using Microsoft.Win32;
using DroidDeck.Lib;

namespace DroidDeck
{
    public partial class frmPrincipal : Form
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppRunName = "KZCompanion";
        

        private ToolStripMenuItem _autoStartItem = null!;
        private ToolStripMenuItem _firewallItem = null!;
        private ToolStripMenuItem _usbItem = null!;
        private bool _usbHooked;
        private ToolStripMenuItem _updateItem = null!;

        public frmPrincipal()
        {
            InitializeComponent();

            ShowInTaskbar = false;
            ntiTray.Icon = AppIcon.Small;
            Icon = AppIcon.Large;
            BuildTrayMenu();
            ntiTray.Visible = true;
        }

        protected override void SetVisibleCore(bool value)
        {
            if (!IsHandleCreated) CreateHandle();
            base.SetVisibleCore(false);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            if (!Program.StartMinimized || Program.FirstRunAfterInstall) BeginInvoke(ShowWindow);
            else TrayHint("KZ Companion is running in the tray.");

            if (Program.FirstRunAfterInstall)
                BeginInvoke(FirstRunSetup);

            Program.Updater.UpdateReady += version => BeginInvoke(() => OnUpdateReady(version));
            Program.Updater.StartBackgroundChecks(_updateCts.Token);
        }

        private readonly System.Threading.CancellationTokenSource _updateCts = new();

        private void FirstRunSetup()
        {
            SetAutoStart(true);
            _autoStartItem.Checked = true;

            if (Lib.FirewallSetup.RulesExist()) return;
            var answer = MessageBox.Show(
                L.Ui("For the phone to find and connect to KZ Companion, Windows Firewall needs to allow ports {tcp} (TCP) and {udp} (UDP), only for your local network.\n\nAllow now? Windows will ask for administrator permission.",
                    ("tcp", Lib.PairingInfo.Port), ("udp", Lib.FirewallSetup.DiscoveryPort)),
                "KZ Companion", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) AllowFirewall();
        }

        private void AllowFirewall()
        {
            if (Lib.FirewallSetup.AddRules())
                ntiTray.ShowBalloonTip(3000, "KZ Companion", L.Ui("Firewall allowed for the local network."), ToolTipIcon.Info);
            else
                ntiTray.ShowBalloonTip(5000, "KZ Companion",
                    L.Ui("The firewall was not changed. You can try again from the tray menu."), ToolTipIcon.Warning);
            _firewallItem.Checked = Lib.FirewallSetup.RulesExist();
        }

        private async void ToggleUsb()
        {
            if (Lib.UsbBridge.Enabled)
            {
                Lib.UsbBridge.Enabled = false;
                UpdateUsbItem();
                return;
            }
            if (Lib.UsbBridge.FindAdb() == null)
            {
                var answer = MessageBox.Show(
                    L.Ui("To connect over the cable, KZ Companion uses adb, Google's official tool for talking to Android phones (about 7 MB).\n\nDownload it now from Google's site?\n\nOn the phone, turn on \"USB debugging\" in the developer options."),
                    L.Ui("KZ Companion — USB connection"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
                _usbItem.Enabled = false;
                _usbItem.Text = L.Ui("Downloading adb…");
                try
                {
                    await Lib.UsbBridge.DownloadAdbAsync();
                }
                catch (Exception ex)
                {
                    Lib.Log.Error($"Falha ao baixar o adb: {ex.Message}");
                    ntiTray.ShowBalloonTip(5000, "KZ Companion", L.Ui("Could not download adb. Try again later."), ToolTipIcon.Warning);
                    _usbItem.Enabled = true;
                    UpdateUsbItem();
                    return;
                }
                _usbItem.Enabled = true;
            }
            Lib.UsbBridge.Enabled = true;
            UpdateUsbItem();
            ntiTray.ShowBalloonTip(5000, "KZ Companion",
                L.Ui("USB connection on. Turn on USB debugging on the phone and accept the prompt that appears on it."), ToolTipIcon.Info);
        }

        private void UpdateUsbItem()
        {
            if (InvokeRequired) { BeginInvoke(UpdateUsbItem); return; }
            var count = Lib.UsbBridge.Instance?.Devices.Count ?? 0;
            _usbItem.Checked = Lib.UsbBridge.Enabled;
            _usbItem.Text = !Lib.UsbBridge.Enabled ? L.Ui("USB connection (cable)")
                : count == 0 ? L.Ui("USB connection (cable): waiting for phone")
                : count == 1 ? L.Ui("USB connection (cable): 1 phone connected")
                : L.Ui("USB connection (cable): {n} phones connected", ("n", count));
        }

        private void OnUpdateReady(string version)
        {
            _updateItem.Text = L.Ui("Restart to update (v{version})", ("version", version));
            ntiTray.ShowBalloonTip(5000, "KZ Companion",
                L.Ui("Version {version} was downloaded and takes effect the next time KZ Companion opens. To update now, use the tray menu.", ("version", version)), ToolTipIcon.Info);
        }

        private async void OnUpdateClick()
        {
            var updater = Program.Updater;
            if (!updater.IsInstalled)
            {
                OpenUrl(Lib.AppUpdater.RepoUrl + "/releases/latest");
                return;
            }
            if (updater.PendingVersion != null)
            {
                updater.ApplyAndRestart();
                return;
            }

            _updateItem.Enabled = false;
            _updateItem.Text = L.Ui("Checking for updates…");
            var version = await updater.CheckAndDownloadAsync();
            _updateItem.Enabled = true;
            if (version == null)
            {
                _updateItem.Text = L.Ui("Check for updates");
                ntiTray.ShowBalloonTip(3000, "KZ Companion",
                    L.Ui("You have the latest version ({version}).", ("version", updater.CurrentVersion)), ToolTipIcon.Info);
            }
        }

        private void BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem(L.Ui("Open KZ Companion"));
            openItem.Click += (_, _) => OpenWebUi();

            var pairItem = new ToolStripMenuItem(L.Ui("Pair device (QR)…"));
            pairItem.Click += (_, _) =>
            {
                using var f = new frmPairing();
                f.ShowDialog();
            };

            _autoStartItem = new ToolStripMenuItem(L.Ui("Start with Windows"))
            {
                CheckOnClick = true,
                Checked = IsAutoStartEnabled(),
            };
            _autoStartItem.Click += (_, _) => SetAutoStart(_autoStartItem.Checked);

            _firewallItem = new ToolStripMenuItem(L.Ui("Allow through firewall (local network)"))
            {
                Checked = Lib.FirewallSetup.RulesExist(),
            };
            _firewallItem.Click += (_, _) => AllowFirewall();

            _usbItem = new ToolStripMenuItem(L.Ui("USB connection (cable)"));
            _usbItem.Click += (_, _) => ToggleUsb();
            menu.Opening += (_, _) =>
            {
                if (Lib.UsbBridge.Instance is { } usb && !_usbHooked)
                {
                    usb.Changed += UpdateUsbItem;
                    _usbHooked = true;
                }
                UpdateUsbItem();
            };

            var version = Program.Updater.CurrentVersion;
            var versionItem = new ToolStripMenuItem(version != null ? $"KZ Companion v{version}" : L.Ui("KZ Companion (no installer)"))
            {
                Enabled = false,
            };
            _updateItem = new ToolStripMenuItem(Program.Updater.IsInstalled ? L.Ui("Check for updates") : L.Ui("Download the latest version…"));
            _updateItem.Click += (_, _) => OnUpdateClick();

            var adminItem = new ToolStripMenuItem("Start sensor helper (administrator)…");
            adminItem.Click += (_, _) => StartSensorHelper();
            menu.Opening += (_, _) =>
            {
                adminItem.Text = DroidDeck.Services.SensorHelper.Connected
                    ? "Sensor helper: running"
                    : (DroidDeck.Services.HardwareSensors.IsElevated ? "Sensors: this app runs as administrator" : "Start sensor helper (administrator)…");
                adminItem.Enabled = !DroidDeck.Services.SensorHelper.Connected && !DroidDeck.Services.HardwareSensors.IsElevated;
            };

            var driverItem = new ToolStripMenuItem(DroidDeck.Services.HardwareSensors.PawnIoInstalled
                ? "CPU sensor driver (PawnIO): installed"
                : "Install CPU sensor driver (PawnIO)…")
            { Enabled = !DroidDeck.Services.HardwareSensors.PawnIoInstalled };
            driverItem.Click += (_, _) => InstallPawnIo();

            var exitItem = new ToolStripMenuItem(L.Ui("Quit"));
            exitItem.Click += (_, _) => { Program.Exiting = true; Application.Exit(); };

            menu.Items.Add(openItem);
            menu.Items.Add(pairItem);
            menu.Items.Add(_autoStartItem);
            menu.Items.Add(_firewallItem);
            menu.Items.Add(_usbItem);
            menu.Items.Add(adminItem);
            menu.Items.Add(driverItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(versionItem);
            menu.Items.Add(_updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);

            ntiTray.ContextMenuStrip = menu;
            ntiTray.DoubleClick += (_, _) => OpenWebUi();
        }

        private void StartSensorHelper()
        {
            var answer = MessageBox.Show(
                "CPU temperature/power, SSD temperature and fan speeds can only be read with administrator rights.\n\n" +
                "KZ Companion starts a small helper for that. It only reads sensors, accepts no commands, and the main app (with drag and drop) stays non-elevated.\n\n" +
                "Start the helper now? Windows will ask for permission.",
                "KZ Companion", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            if (!DroidDeck.Services.SensorHelper.LaunchElevated())
                TrayHint("Permission was not granted. The sensor helper did not start.");
        }

        private void InstallPawnIo()
        {
            var answer = MessageBox.Show(
                "CPU temperature and power need the free PawnIO driver (signed, used by LibreHardwareMonitor).\n\n" +
                "Install it now with winget (package namazso.PawnIO)? Windows will ask for administrator permission. " +
                "After it finishes, start the sensor helper from this menu.",
                "KZ Companion", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe",
                    "/c winget install --id namazso.PawnIO -e --accept-source-agreements --accept-package-agreements & pause")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                });
            }
            catch (System.ComponentModel.Win32Exception) {  }
        }

        private frmWeb? _window;

        public void ShowWindow()
        {
            if (_window == null || _window.IsDisposed) _window = new frmWeb();
            _window.Bring();
        }

        public void TrayHint(string text) =>
            ntiTray.ShowBalloonTip(3000, "KZ Companion", text, ToolTipIcon.Info);

        private void OpenWebUi() => ShowWindow();

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Lib.Log.Error($"Falha ao abrir a Web UI: {ex.Message}");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            ntiTray.Visible = false;
            _updateCts.Cancel();
            base.OnFormClosing(e);
        }

        private static bool IsAutoStartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppRunName) != null;
        }

        internal static void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                    ?? Registry.CurrentUser.CreateSubKey(RunKey);
                if (key == null) return;

                if (enable)
                    key.SetValue(AppRunName, $"\"{Application.ExecutablePath}\" --minimized");
                else
                    key.DeleteValue(AppRunName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Lib.Log.Error($"Falha ao configurar autostart: {ex.Message}");
            }
        }

        internal static void RemoveAutoStartIfOurs()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
                var current = key?.GetValue(AppRunName) as string;
                if (current != null &&
                    current.Replace(" --minimized", "").Trim('"').Equals(Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    key!.DeleteValue(AppRunName, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                Lib.Log.Error($"Falha ao remover autostart: {ex.Message}");
            }
        }
    }
}
