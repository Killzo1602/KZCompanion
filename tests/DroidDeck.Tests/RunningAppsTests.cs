using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DroidDeck.Auth;
using DroidDeck.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace DroidDeck.Tests
{
    /// <summary>Agrupamento e filtros: logica pura, sem janela nenhuma.</summary>
    public class RunningAppsGroupingTests
    {
        private static RawWindow W(long hwnd, uint pid, string title, string? path, string process = "app",
            bool min = false, bool fg = false, uint? frame = null, bool elevated = false) =>
            new(hwnd, frame ?? pid, pid, title, min, fg, path, process, elevated);

        private static string Name(string? path, string process) => process;

        [Fact]
        public void VariosProcessosDoMesmoExe_ViramUmAppSo()
        {
            // Firefox: um processo de janela e filhos, todos do mesmo firefox.exe.
            var apps = RunningAppsGrouper.Group(new[]
            {
                W(1, 100, "Mozilla Firefox", @"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox"),
                W(2, 101, "Outra janela", @"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox"),
            }, Name);
            var app = Assert.Single(apps);
            Assert.Equal(2, app.Windows.Count);
            Assert.Equal(new uint[] { 100, 101 }, app.Pids);
        }

        [Fact]
        public void CaminhoComCaixaDiferente_NaoDuplica()
        {
            var apps = RunningAppsGrouper.Group(new[]
            {
                W(1, 1, "a", @"C:\Apps\Discord\Discord.exe", "Discord"),
                W(2, 2, "b", @"c:\apps\discord\discord.exe", "Discord"),
            }, Name);
            Assert.Single(apps);
        }

        [Fact]
        public void ExesDiferentes_SaoAppsDiferentes()
        {
            var apps = RunningAppsGrouper.Group(new[]
            {
                W(1, 1, "a", @"C:\a.exe", "a"), W(2, 2, "b", @"C:\b.exe", "b"),
            }, Name);
            Assert.Equal(2, apps.Count);
        }

        [Fact]
        public void IdDoApp_BateComOGroupKeyDoMixerDeAudio()
        {
            const string path = @"C:\Program Files\Spotify\Spotify.exe";
            var app = Assert.Single(RunningAppsGrouper.Group(new[] { W(1, 5, "Spotify", path, "Spotify") }, Name));
            Assert.Equal(AudioMixerService.ShortHash(path.ToLowerInvariant()), app.Id);
        }

        [Fact]
        public void SemCaminho_CadaPidEOSeuProprioApp()
        {
            var apps = RunningAppsGrouper.Group(new[] { W(1, 7, "x", null, "prot"), W(2, 8, "y", null, "prot") }, Name);
            Assert.Equal(new[] { "pid7", "pid8" }, apps.Select(a => a.Id).OrderBy(x => x));
        }

        [Fact]
        public void MesmoHwndNaoEntraDuasVezes()
        {
            var apps = RunningAppsGrouper.Group(new[] { W(1, 1, "a", @"C:\a.exe"), W(1, 1, "a", @"C:\a.exe") }, Name);
            Assert.Single(Assert.Single(apps).Windows);
        }

        [Fact]
        public void JanelaPrincipal_EAEmFoco_Depois_AsVisiveis_Depois_AsMinimizadas()
        {
            var app = Assert.Single(RunningAppsGrouper.Group(new[]
            {
                W(1, 1, "minimizada", @"C:\a.exe", min: true),
                W(2, 1, "visivel", @"C:\a.exe"),
                W(3, 1, "foco", @"C:\a.exe", fg: true),
            }, Name));
            Assert.Equal(new[] { "foco", "visivel", "minimizada" }, app.Windows.Select(w => w.Title));
            Assert.True(app.Foreground);
            Assert.False(app.Minimized);
        }

        [Fact]
        public void AppMinimizado_SoQuandoTodasAsJanelasEstao()
        {
            var app = Assert.Single(RunningAppsGrouper.Group(new[]
            {
                W(1, 1, "a", @"C:\a.exe", min: true), W(2, 1, "b", @"C:\a.exe", min: true),
            }, Name));
            Assert.True(app.Minimized);
        }

        [Fact]
        public void AppDaLoja_AgrupaPeloProcessoReal_EGuardaOPidDoFrame()
        {
            // Janela no ApplicationFrameHost (pid 50); o app de verdade e o 60.
            var app = Assert.Single(RunningAppsGrouper.Group(new[]
            {
                W(9, 60, "Calculadora", @"C:\Program Files\WindowsApps\Calc\Calculator.exe", "CalculatorApp", frame: 50),
            }, Name));
            Assert.Equal(new uint[] { 60 }, app.Pids);
            Assert.Equal(50u, app.Windows[0].FramePid);
        }

        [Fact]
        public void ListaEstavel_OrdemPorNome_NaoPorFoco()
        {
            var a = RunningAppsGrouper.Group(new[]
            {
                W(1, 1, "z", @"C:\z.exe", "zeta", fg: true), W(2, 2, "a", @"C:\a.exe", "alpha"),
            }, Name);
            Assert.Equal(new[] { "alpha", "zeta" }, a.Select(x => x.Name));
        }

        [Fact]
        public void LimiteDeJanelasPorApp()
        {
            var many = Enumerable.Range(1, 30).Select(i => W(i, 1, "j" + i, @"C:\a.exe"));
            var app = Assert.Single(RunningAppsGrouper.Group(many, Name));
            Assert.Equal(RunningAppsGrouper.MaxWindowsPerApp, app.Windows.Count);
        }

        [Theory]
        [InlineData("TextInputHost", "Windows.UI.Core.CoreWindow", false)]
        [InlineData("SearchHost", "X", false)]
        [InlineData("StartMenuExperienceHost", "X", false)]
        [InlineData("explorer", "Shell_TrayWnd", false)]
        [InlineData("explorer", "Progman", false)]
        [InlineData("explorer", "CabinetWClass", true)]
        [InlineData("firefox", "MozillaWindowClass", true)]
        [InlineData("Discord", "Chrome_WidgetWin_1", true)]
        [InlineData("", "X", false)]
        public void SoAplicativosDoUsuario(string process, string cls, bool expected) =>
            Assert.Equal(expected, RunningAppsGrouper.IsUserFacing(process, cls));

        private const string Win = @"C:\Windows";

        [Theory]
        // Steam: a janela e do steamwebhelper, que mora dentro da pasta do steam.exe.
        [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", @"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe", true)]
        [InlineData(@"c:\program files (x86)\steam\STEAM.EXE", @"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe", true)]
        // Programa iniciado pelo Explorer nunca vira "Windows Explorer".
        [InlineData(@"C:\Windows\explorer.exe", @"C:\Windows\System32\notepad.exe", false)]
        [InlineData(@"C:\Windows\explorer.exe", @"C:\Program Files\Some App\app.exe", false)]
        // Pastas irmas e nomes parecidos nao contam como "dentro".
        [InlineData(@"C:\Apps\Steam\steam.exe", @"C:\Apps\SteamHelper\helper.exe", false)]
        [InlineData(@"C:\Apps\One\one.exe", @"C:\Apps\Two\two.exe", false)]
        // Mesmo executavel: o agrupamento por caminho ja resolve.
        [InlineData(@"C:\Apps\One\one.exe", @"C:\Apps\One\one.exe", false)]
        // Pai na raiz do drive seria dono de tudo.
        [InlineData(@"C:\launcher.exe", @"C:\Games\Foo\foo.exe", false)]
        [InlineData(null, @"C:\Apps\One\one.exe", false)]
        [InlineData(@"C:\Apps\One\one.exe", null, false)]
        public void DonoDoAplicativo_SoQuandoOFilhoMoraDentroDaPastaDoPai(string? parent, string? child, bool expected) =>
            Assert.Equal(expected, RunningAppsGrouper.IsOwnerOf(parent, child, Win));

        [Theory]
        [InlineData(0x1A2B3Cu, 4321u)]
        [InlineData(1u, 1u)]
        public void IdDeJanela_IdaEVolta(uint hwnd, uint pid)
        {
            var id = RunningAppsGrouper.WindowId(hwnd, pid);
            Assert.True(RunningAppsGrouper.TryParseWindowId(id, out var h, out var p));
            Assert.Equal((long)hwnd, h);
            Assert.Equal(pid, p);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData(".5")]
        [InlineData("5.")]
        [InlineData("zz.5")]
        [InlineData("5.-1")]
        public void IdDeJanelaInvalido_Recusado(string? id) =>
            Assert.False(RunningAppsGrouper.TryParseWindowId(id, out _, out _));
    }

    /// <summary>Janelas de verdade (WinForms no proprio processo de teste): foco, minimizar, fechar.</summary>
    [Trait("Category", "RequiresDesktop")]
    public class RunningAppsLiveTests : IDisposable
    {
        private readonly List<TestWindow> _windows = new();
        private readonly RunningAppsService _service = new(null) { ExcludePid = 0 };

        private sealed class TestWindow
        {
            public Form Form = null!;
            public Thread Thread = null!;
            public volatile bool CancelClose;
            public volatile bool Closed;
        }

        private static string NewTitle() => "KZ-test-" + Guid.NewGuid().ToString("N")[..6];

        private TestWindow Open(string title)
        {
            var w = new TestWindow();
            var ready = new ManualResetEventSlim();
            w.Thread = new Thread(() =>
            {
                w.Form = new Form { Text = title, Width = 320, Height = 200, StartPosition = FormStartPosition.Manual, Left = 40, Top = 40 };
                w.Form.FormClosing += (_, e) => { if (w.CancelClose) e.Cancel = true; };
                w.Form.FormClosed += (_, _) => w.Closed = true;
                w.Form.Shown += (_, _) => ready.Set();
                Application.Run(w.Form);
            });
            w.Thread.SetApartmentState(ApartmentState.STA);
            w.Thread.IsBackground = true;
            w.Thread.Start();
            Assert.True(ready.Wait(5000), "janela de teste nao abriu");
            _windows.Add(w);
            return w;
        }

        private RunningApp? Mine(string title) =>
            _service.List().FirstOrDefault(a => a.Windows.Any(w => w.Title == title));

        public void Dispose()
        {
            foreach (var w in _windows)
            {
                try
                {
                    w.CancelClose = false;
                    if (!w.Closed) w.Form.BeginInvoke(new Action(() => w.Form.Close()));
                }
                catch { }
            }
            _service.Dispose();
        }

        [Fact]
        public void JanelaAbertaApareceComTituloEGrupoUnico()
        {
            var t1 = NewTitle();
            var t2 = NewTitle();
            Open(t1);
            Open(t2);
            var app = Assert.Single(_service.List().Where(a => a.Windows.Any(w => w.Title == t1)));
            // As duas janelas do mesmo processo de teste caem no mesmo app.
            Assert.Contains(app.Windows, w => w.Title == t2);
            Assert.Equal(Environment.ProcessId, (int)app.Pids.Single());
            Assert.NotNull(app.ExePath);
        }

        [Fact]
        public void ProcessoExcluido_NaoApareceNaLista()
        {
            var t = NewTitle();
            Open(t);
            _service.ExcludePid = (uint)Environment.ProcessId;
            Assert.Null(Mine(t));
        }

        [Fact]
        public async Task MinimizarEFocar_RestauraETrazParaAFrente()
        {
            var t = NewTitle();
            Open(t);
            var app = Mine(t)!;
            var win = app.Windows.First(w => w.Title == t);

            Assert.True(_service.Minimize(app.Id, win.Id).Ok);
            await Task.Delay(400);
            Assert.True(Mine(t)!.Windows.First(w => w.Title == t).Minimized);

            var focus = await _service.FocusAsync(app.Id, win.Id);
            Assert.True(focus.Ok, focus.Outcome.ToString());
            var after = Mine(t)!.Windows.First(w => w.Title == t);
            Assert.False(after.Minimized);
            Assert.True(after.Foreground);
        }

        [Fact]
        public async Task FecharGracioso_AppQueRecusa_ContinuaAberto_ENaoEMorto()
        {
            var t = NewTitle();
            var w = Open(t);
            w.CancelClose = true; // simula o "salvar alteracoes?"
            var app = Mine(t)!;
            var result = await _service.CloseAsync(app.Id, app.Windows.First(x => x.Title == t).Id);
            Assert.Equal(AppActionOutcome.StillOpen, result.Outcome);
            Assert.False(w.Closed);
            Assert.NotNull(Mine(t)); // a janela segue viva: nada foi forcado
        }

        [Fact]
        public async Task FecharGracioso_AppQueAceita_FechaAJanela()
        {
            var t = NewTitle();
            var w = Open(t);
            var app = Mine(t)!;
            var result = await _service.CloseAsync(app.Id, app.Windows.First(x => x.Title == t).Id);
            Assert.True(result.Ok, result.Outcome.ToString());
            Assert.True(SpinWait.SpinUntil(() => w.Closed, 2000));
            Assert.Null(Mine(t));
        }

        [Fact]
        public async Task AppQueFechouEntreListarEAgir_NaoAcha_ENaoFazNada()
        {
            var t = NewTitle();
            var w = Open(t);
            var app = Mine(t)!;
            var winId = app.Windows.First(x => x.Title == t).Id;
            w.Form.Invoke(new Action(() => w.Form.Close()));
            Assert.True(SpinWait.SpinUntil(() => w.Closed, 2000));
            await Task.Delay(100);

            var gone = new[] { AppActionOutcome.AppGone, AppActionOutcome.WindowGone };
            var focus = await _service.FocusAsync(app.Id, winId);
            Assert.Contains(focus.Outcome, gone);
            Assert.Contains((await _service.CloseAsync(app.Id, winId)).Outcome, gone);
            Assert.Contains(_service.Minimize(app.Id, winId).Outcome, gone);
        }

        [Fact]
        public async Task JanelaInventadaOuAppDesconhecido_NaoEAceito()
        {
            var t = NewTitle();
            Open(t);
            var app = Mine(t)!;
            // HWND que nao e janela deste app, mas com o PID certo: recusado.
            var forged = RunningAppsGrouper.WindowId(0x10010, (uint)Environment.ProcessId);
            Assert.Equal(AppActionOutcome.WindowGone, (await _service.FocusAsync(app.Id, forged)).Outcome);
            Assert.Equal(AppActionOutcome.AppGone, (await _service.FocusAsync("0123456789ab")).Outcome);
            Assert.Equal(AppActionOutcome.WindowGone, (await _service.FocusAsync(app.Id, "xyz")).Outcome);
        }

        [Fact]
        public void SemObservador_NaoHaThreadNemHookNemTimer()
        {
            Assert.False(_service.IsPumping);
            Assert.Equal(0, _service.WatcherCount);
            _service.Watch("c1");
            Assert.True(_service.IsPumping);
            _service.Watch("c2");
            _service.Unwatch("c1");
            Assert.True(_service.IsPumping); // ainda ha quem observe
            _service.Unwatch("c2");
            Assert.False(_service.IsPumping);
            Assert.Equal(0, _service.WatcherCount);
        }

        [Fact]
        public void IconeDoApp_ComoPng()
        {
            var t = NewTitle();
            Open(t);
            var png = _service.IconFor(Mine(t)!.Id);
            Assert.NotNull(png);
            Assert.Equal(0x89, png![0]); // assinatura PNG
        }
    }

    /// <summary>API HTTP: autenticacao, validacao de entrada e confirmacao do Close.</summary>
    public class RunningAppsEndpointsTests : IClassFixture<WebApplicationFactory<DroidDeck.Program>>
    {
        private readonly WebApplicationFactory<DroidDeck.Program> _factory;

        public RunningAppsEndpointsTests(WebApplicationFactory<DroidDeck.Program> factory) => _factory = factory;

        private HttpClient Authed()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-API-KEY", ApiKeyProvider.GetKey());
            return client;
        }

        [Theory]
        [InlineData("GET", "/api/running-apps")]
        [InlineData("GET", "/api/running-apps/0123456789ab/icon")]
        [InlineData("POST", "/api/running-apps/0123456789ab/focus")]
        [InlineData("POST", "/api/running-apps/0123456789ab/minimize")]
        [InlineData("POST", "/api/running-apps/0123456789ab/close?confirm=true")]
        public async Task ExigeAChave(string method, string path)
        {
            var response = await _factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Lista_Responde200ComApps_ENaoExpoeCaminho()
        {
            var response = await Authed().GetAsync("/api/running-apps");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"apps\"", body);
            Assert.DoesNotContain("exePath", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(":\\\\", body); // nenhum caminho de arquivo no JSON
        }

        [Theory]
        [InlineData("/api/running-apps/NOT-AN-ID/minimize")]
        [InlineData("/api/running-apps/0123456789ab/focus?windowId=bad")]
        public async Task IdInvalido_400(string path)
        {
            var r = await Authed().PostAsync(path, null);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        [Fact]
        public async Task AppInexistente_404()
        {
            var c = Authed();
            Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/running-apps/0123456789ab/focus", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/running-apps/0123456789ab/minimize", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsync("/api/running-apps/0123456789ab/close?confirm=true", null)).StatusCode);
        }

        [Fact]
        public async Task Fechar_SemConfirmacao_E400()
        {
            var c = Authed();
            var r = await c.PostAsync("/api/running-apps/0123456789ab/close", null);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("confirmation_required", await r.Content.ReadAsStringAsync());
            r = await c.PostAsync("/api/running-apps/0123456789ab/close?confirm=false", null);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        }

        [Fact]
        public async Task RotaAntigaDeAppsAbertos_ContinuaIgual()
        {
            var r = await Authed().GetAsync("/api/system/apps");
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
    }
}
