using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using DroidDeck.Auth;
using DroidDeck.Lib;

namespace DroidDeck
{
    public class frmPairing : Form
    {
        private readonly Label _info;
        private readonly PictureBox _pic;
        private readonly RadioButton _appMode;
        private readonly LinkLabel _url;

        public frmPairing()
        {
            Text = L.Ui("KZ Companion — Pair device");
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = AppIcon.Small;
            ClientSize = new Size(360, 528);
            BackColor = Color.FromArgb(17, 18, 23);
            ForeColor = Color.FromArgb(226, 232, 240);
            var accent = Color.FromArgb(56, 189, 248);

            var ip = NetworkInfo.GetLanIp();
            var key = ApiKeyProvider.GetKey();

            _appMode = new RadioButton
            {
                Text = L.Ui("Android app"),
                Location = new Point(40, 8),
                Size = new Size(130, 24),
                Checked = true,
            };
            var browserMode = new RadioButton
            {
                Text = L.Ui("Open in browser"),
                Location = new Point(180, 8),
                Size = new Size(150, 24),
            };

            _info = new Label
            {
                Location = new Point(10, 36),
                Size = new Size(340, 44),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            _pic = new PictureBox
            {
                Location = new Point(20, 82),
                Size = new Size(320, 320),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Padding = new Padding(8),
            };

            FormClosed += (_, _) => SetQr(null);

            _url = new LinkLabel
            {
                Location = new Point(10, 406),
                Size = new Size(340, 20),
                TextAlign = ContentAlignment.MiddleCenter,
                LinkColor = accent,
                ActiveLinkColor = Color.White,
            };
            _url.LinkClicked += (_, _) =>
            {
                try { Clipboard.SetText(PairingInfo.BuildBrowserUrl()); _url.Text = L.Ui("Address copied"); }
                catch { }
            };

            var details = new TextBox
            {
                Text = L.Ui("IP: {ip}    Key: {key}", ("ip", $"{ip}:{PairingInfo.Port}"), ("key", key)),
                Location = new Point(10, 432),
                Size = new Size(340, 44),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = BackColor,
                ForeColor = ForeColor,
                TextAlign = HorizontalAlignment.Center,
            };

            var close = new Button
            {
                Text = L.Ui("Close"),
                Location = new Point(140, 488),
                Size = new Size(80, 30),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 41, 59),
            };
            close.FlatAppearance.BorderColor = accent;
            close.Click += (_, _) => Close();
            AcceptButton = close;

            _appMode.CheckedChanged += (_, _) => ShowMode();

            Controls.Add(_appMode);
            Controls.Add(browserMode);
            Controls.Add(_info);
            Controls.Add(_pic);
            Controls.Add(_url);
            Controls.Add(details);
            Controls.Add(close);
            ShowMode();
        }

        private void ShowMode()
        {
            if (_appMode.Checked)
            {
                _info.Text = L.Ui("In the KZ Companion app, go to Settings → Pair (QR)\nand scan this code to connect securely.");
                _url.Visible = false;
                SetQr(PairingInfo.BuildUri());
            }
            else
            {
                _info.Text = L.Ui("Point your iPhone, iPad or tablet camera at this code:\nthe deck opens in the browser, nothing to install.");
                _url.Text = L.Ui("Copy the address (to open on another PC)");
                _url.Visible = true;
                SetQr(PairingInfo.BuildBrowserUrl());
            }
        }

        private void SetQr(string? content)
        {
            var old = _pic.Image;
            _pic.Image = null;
            try { old?.Dispose(); } catch { }
            if (content == null) return;
            try
            {
                using var ms = new MemoryStream(PairingInfo.BuildQrPng(content));
                using var qr = new Bitmap(ms);
                _pic.Image = new Bitmap(qr);
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao gerar QR de pareamento: {ex.Message}");
            }
        }
    }
}
