using System;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using DroidDeck.Lib;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DroidDeck
{
    /// <summary>
    /// A janela do KZ Companion: o configurador (servido pelo backend local) dentro de um
    /// WebView2, sem navegador externo. Fechar ou minimizar esconde na bandeja; quem
    /// encerra o app e o item "Encerrar" da bandeja.
    /// </summary>
    public sealed class frmWeb : Form
    {
        public static readonly string Url = $"http://127.0.0.1:{PairingInfo.Port}/";

        private readonly WebView2 _web = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly Panel _status = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(14, 16, 19) };
        private readonly Label _message = new()
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 220,
            ForeColor = Color.FromArgb(226, 232, 240),
            Font = new Font("Segoe UI", 11f),
            Padding = new Padding(40, 60, 40, 0),
        };
        private readonly Button _retry = new() { Text = "Retry", Width = 120, Height = 36, FlatStyle = FlatStyle.Flat, ForeColor = Color.FromArgb(45, 212, 255) };
        private readonly Button _logs = new() { Text = "Open log folder", Width = 140, Height = 36, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        private bool _starting;
        private bool _hintShown;

        public frmWeb()
        {
            Text = "KZ Companion";
            Icon = AppIcon.Large;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 800);
            MinimumSize = new Size(900, 600);
            BackColor = Color.FromArgb(14, 16, 19);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 60, Padding = new Padding(40, 0, 0, 0), BackColor = Color.Transparent };
            buttons.Controls.Add(_retry);
            buttons.Controls.Add(_logs);
            _status.Controls.Add(buttons);
            _status.Controls.Add(_message);
            Controls.Add(_web);
            Controls.Add(_status);

            _retry.Click += async (_, _) => await StartAsync();
            _logs.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)) { UseShellExecute = true }); } catch { }
            };
            Shown += async (_, _) => await StartAsync();
        }

        private void ShowStatus(string text, bool canRetry)
        {
            _message.Text = text;
            _retry.Visible = canRetry;
            _logs.Visible = canRetry;
            _status.Visible = true;
            _web.Visible = false;
            _status.BringToFront();
        }

        private async Task StartAsync()
        {
            if (_starting) return;
            _starting = true;
            try
            {
                ShowStatus("Starting KZ Companion…", canRetry: false);
                if (Program.BackendError != null) Program.RestartBackend();

                if (!await WaitForBackendAsync()) return;

                if (_web.CoreWebView2 == null)
                {
                    try
                    {
                        var env = await CoreWebView2Environment.CreateAsync(
                            userDataFolder: System.IO.Path.Combine(AppPaths.LocalDir, "webview2"));
                        await _web.EnsureCoreWebView2Async(env);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"WebView2: {ex}");
                        ShowStatus(
                            "KZ Companion could not start its window component (Microsoft Edge WebView2 Runtime).\r\n\r\n" +
                            "Install the Evergreen runtime from https://developer.microsoft.com/microsoft-edge/webview2/ and press Retry.\r\n\r\n" + ex.Message,
                            canRetry: true);
                        return;
                    }
                    _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    _web.CoreWebView2.NewWindowRequested += (_, e) =>
                    {
                        e.Handled = true;
                        OpenExternal(e.Uri);
                    };
                    _web.CoreWebView2.NavigationStarting += (_, e) =>
                    {
                        if (!IsOurs(e.Uri)) { e.Cancel = true; OpenExternal(e.Uri); }
                    };
                    _web.CoreWebView2.DocumentTitleChanged += (_, _) => Text = "KZ Companion";
                    _web.CoreWebView2.NavigationCompleted += (_, e) =>
                    {
                        if (e.IsSuccess) { _status.Visible = false; _web.Visible = true; }
                        else ShowStatus($"The configurator could not be loaded ({e.WebErrorStatus}).", canRetry: true);
                    };
                }
                _web.CoreWebView2.Navigate(Url);
            }
            finally { _starting = false; }
        }

        private static bool IsOurs(string uri) =>
            Uri.TryCreate(uri, UriKind.Absolute, out var u) &&
            (u.Scheme == "about" || u.Scheme == "data" || (u.IsLoopback && u.Port == PairingInfo.Port));

        private static void OpenExternal(string uri)
        {
            // So http(s): nada de file:, ms-* ou outros esquemas vindos de uma pagina.
            if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            {
                try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
            }
        }

        /// <summary>Espera o backend responder; se ele caiu ou nao subiu, mostra o motivo na janela.</summary>
        private async Task<bool> WaitForBackendAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (var i = 0; i < 60; i++)
            {
                if (Program.BackendError is { } err)
                {
                    ShowStatus(Program.DescribeBackendError(err), canRetry: true);
                    return false;
                }
                try
                {
                    var r = await http.GetAsync(Url + "api/ping");
                    if (r.IsSuccessStatusCode)
                    {
                        // O index.html faltando (build incompleto) viraria um 401 mudo.
                        var idx = await http.GetAsync(Url);
                        if (idx.IsSuccessStatusCode) return true;
                        ShowStatus($"The backend is running but the configurator files are missing (HTTP {(int)idx.StatusCode}). Reinstall KZ Companion.", canRetry: true);
                        return false;
                    }
                }
                catch { }
                await Task.Delay(500);
            }
            ShowStatus(Program.BackendError != null
                ? Program.DescribeBackendError(Program.BackendError)
                : $"The KZ Companion backend did not answer on port {PairingInfo.Port}. Check whether another program (for example the original DroidDeck) is using that port.", canRetry: true);
            return false;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) HideToTray();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !Program.Exiting)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            base.OnFormClosing(e);
        }

        private void HideToTray()
        {
            Hide();
            if (!_hintShown && Program.FrmPrincipal != null)
            {
                _hintShown = true;
                Program.FrmPrincipal.TrayHint("KZ Companion is still running in the tray.");
            }
        }

        public void Bring()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }
    }
}
