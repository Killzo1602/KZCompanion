using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using DroidDeck.Lib;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DroidDeck
{
    public sealed class frmWeb : Form
    {
        public static readonly string Url = $"http://127.0.0.1:{PairingInfo.Port}/";

        private WebView2 _web = new() { Dock = DockStyle.Fill, Visible = false };
        private bool _recreateWebView;
        private readonly List<DateTime> _failures = new();
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
            _catcher = new DropCatcher(this);
            _dragTimer.Interval = 30;
            _dragTimer.Tick += (_, _) => WatchExternalDrag();
            _dragTimer.Start();
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

                if (_recreateWebView)
                {
                    _recreateWebView = false;
                    Controls.Remove(_web);
                    _web.Dispose();
                    _web = new WebView2 { Dock = DockStyle.Fill, Visible = false };
                    Controls.Add(_web);
                    _status.BringToFront();
                }

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
                    _web.AllowExternalDrop = false;
                    _web.CoreWebView2.ProcessFailed += OnProcessFailed;
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

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            var now = DateTime.UtcNow;
            _failures.RemoveAll(t => now - t > TimeSpan.FromMinutes(5));
            _failures.Add(now);
            string frames = "";
            try { frames = string.Join(", ", e.FrameInfosForFailedProcess.Select(f => f.Source)); } catch { }
            Log.Error($"WebView2 ProcessFailed: kind={e.ProcessFailedKind} reason={e.Reason} exitCode={e.ExitCode} " +
                      $"process='{e.ProcessDescription}' frames=[{frames}] failuresIn5min={_failures.Count}");

            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited) _recreateWebView = true;
            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) return;

            var detail = $"({e.ProcessFailedKind}, {e.Reason}, exit code {e.ExitCode})";
            var repeated = _failures.Count >= 3
                ? "\r\n\r\nThis keeps happening. Add applications with \"Installed apps\" or Add Application instead of dragging, and check the log folder."
                : "\r\n\r\nYour saved applications and settings are not affected. Drag and drop of unsupported items (such as Start Menu apps) can cause this: use \"Installed apps\" for those.";
            BeginInvoke(() => ShowStatus("The configurator window had a problem and was stopped by Windows " + detail + "." + repeated + "\r\n\r\nPress Retry to reload it.", canRetry: true));
        }

        private readonly DropCatcher _catcher = null!;
        private readonly System.Windows.Forms.Timer _dragTimer = new();
        private bool _btnWasDown, _armed;

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out System.Drawing.Point p);

        private void WatchExternalDrag()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) { _armed = false; _catcher.Hide(); return; }
            var down = (GetAsyncKeyState(0x01) & 0x8000) != 0;
            GetCursorPos(out var pt);
            var area = _web.RectangleToScreen(_web.ClientRectangle);
            var inside = area.Contains(pt);
            if (down && !_btnWasDown) _armed = !inside;
            if (!down) { _armed = false; if (_catcher.Visible) _catcher.Hide(); }
            _btnWasDown = down;
            if (_armed && inside && !_catcher.Visible) _catcher.ShowOver(area);
            else if (_catcher.Visible && !inside) _catcher.Hide();
        }

        private sealed class DropCatcher : Form
        {
            private readonly frmWeb _owner;
            public DropCatcher(frmWeb owner)
            {
                _owner = owner;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Opacity = 0.01;
                TopMost = true;
                AllowDrop = true;
                DragEnter += owner.OnDragEnter;
                DragOver += owner.OnDragEnter;
                DragLeave += (_, _) => owner.PostDragging(false);
                DragDrop += (s, e) => { Hide(); owner.OnDragDrop(s, e); };
            }
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80; return cp; }
            }
            public void ShowOver(System.Drawing.Rectangle area)
            {
                Bounds = area;
                if (!Visible) Show(_owner);
            }
        }

        private bool _dragShown;

        private void PostDragging(bool on)
        {
            if (on == _dragShown) return;
            _dragShown = on;
            try { _web.CoreWebView2?.PostWebMessageAsJson(on ? "{\"type\":\"dragging\",\"on\":true}" : "{\"type\":\"dragging\",\"on\":false}"); } catch { }
        }

        private static bool Supported(IDataObject d)
        {
            try
            {
                return d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent("FileGroupDescriptorW") ||
                       d.GetDataPresent(DataFormats.UnicodeText) || d.GetDataPresent("Shell IDList Array");
            }
            catch { return false; }
        }

        private void OnDragEnter(object? sender, DragEventArgs e)
        {
            var ok = e.Data != null && Supported(e.Data);
            e.Effect = ok ? DragDropEffects.Copy : DragDropEffects.None;
            PostDragging(ok);
        }

        private void OnDragDrop(object? sender, DragEventArgs e)
        {
            PostDragging(false);
            var paths = new List<string>();
            var rejected = new List<string>();
            var unresolved = new List<string>();
            var unsupported = false;
            try
            {
                var d = e.Data;
                if (d == null) return;
                if (d.GetDataPresent(DataFormats.FileDrop) && d.GetData(DataFormats.FileDrop) is string[] files)
                {
                    foreach (var f in files.Take(30))
                    {
                        var ext = System.IO.Path.GetExtension(f);
                        if (!System.IO.File.Exists(f)) { rejected.Add(System.IO.Path.GetFileName(f)); continue; }
                        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                            paths.Add(f);
                        else
                            rejected.Add(System.IO.Path.GetFileName(f));
                    }
                }
                else
                {
                    var names = new List<string>();
                    if (d.GetDataPresent("FileGroupDescriptorW") && d.GetData("FileGroupDescriptorW") is System.IO.MemoryStream ms)
                        names.AddRange(DescriptorNames(ms));
                    if (names.Count == 0 && d.GetDataPresent(DataFormats.UnicodeText) && d.GetData(DataFormats.UnicodeText) is string text)
                        if (SafeName(text) is { } n) names.Add(n);
                    unresolved.AddRange(names.Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
                    unsupported = unresolved.Count == 0;
                    if (unsupported)
                        Log.Info("Arrastar e soltar sem arquivo nem nome utilizavel; formatos: " + string.Join(", ", d.GetFormats(false)));
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Arrastar e soltar: {ex.Message}");
                unsupported = true;
            }
            try
            {
                _web.CoreWebView2?.PostWebMessageAsJson(System.Text.Json.JsonSerializer.Serialize(
                    new { type = "dropped", paths, rejected, unresolved, unsupported }));
            }
            catch (Exception ex) { Log.Warning($"Arrastar e soltar (mensagem): {ex.Message}"); }
        }

        private static IEnumerable<string> DescriptorNames(System.IO.MemoryStream ms)
        {
            const int item = 592, nameOffset = 72, nameBytes = 520;
            var buf = ms.ToArray();
            if (buf.Length < 4) yield break;
            var count = BitConverter.ToUInt32(buf, 0);
            if (count == 0 || count > 50) yield break;
            for (var i = 0; i < count; i++)
            {
                var off = 4 + i * item;
                if (off + item > buf.Length) yield break;
                var name = System.Text.Encoding.Unicode.GetString(buf, off + nameOffset, nameBytes);
                var z = name.IndexOf('\0');
                if (z >= 0) name = name[..z];
                if (SafeName(name) is { } safe) yield return safe;
            }
        }

        private static string? SafeName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.Length > 260) return null;
            var t = raw.Trim().Replace('/', '\\');
            t = t[(t.LastIndexOf('\\') + 1)..];
            t = System.Text.RegularExpressions.Regex.Replace(t, @"\.(lnk|exe|url)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            return System.Text.RegularExpressions.Regex.IsMatch(t, @"^[\p{L}\p{N} ._\-()&+']{1,64}$") ? t : null;
        }

        private static bool IsOurs(string uri) =>
            Uri.TryCreate(uri, UriKind.Absolute, out var u) &&
            (u.Scheme == "about" || u.Scheme == "data" || (u.IsLoopback && u.Port == PairingInfo.Port));

        private static void OpenExternal(string uri)
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
            {
                try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true }); } catch { }
            }
        }

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
