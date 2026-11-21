using System.Reflection;
using System.Diagnostics;
using System.IO;
using DroidDeck.Lib;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using NLog.Extensions.Logging;
using Microsoft.OpenApi.Models;

namespace DroidDeck
{
    public class Program
    {
        public static frmPrincipal? FrmPrincipal { get; private set; }

        /// <summary>Primeira execucao depois de instalar pelo DroidDeck-Setup.exe.</summary>
        public static bool FirstRunAfterInstall { get; private set; }

        public static readonly AppUpdater Updater = new();

        /// <summary>Erro que derrubou (ou impediu de subir) o servidor local; a janela o mostra.</summary>
        public static Exception? BackendError { get; private set; }

        /// <summary>True quando o usuario pediu para encerrar (senao fechar a janela so esconde).</summary>
        public static bool Exiting { get; set; }

        public static bool StartMinimized { get; private set; }

        /// <summary>"Windows 11" / "Windows 10" (a build 22000 em diante e o 11).</summary>
        public static string WindowsName() =>
            Environment.OSVersion.Version.Major >= 10
                ? (Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10")
                : "Windows";

        public static string DescribeBackendError(Exception ex)
        {
            if (ex is System.IO.IOException || ex is System.Net.Sockets.SocketException ||
                ex.InnerException is System.Net.Sockets.SocketException ||
                ex.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("Only one usage", StringComparison.OrdinalIgnoreCase))
                return $"KZ Companion could not start its local server because port {Lib.PairingInfo.Port} is already in use.\r\n\r\n" +
                       "Another program is using it, most likely the original DroidDeck. Close it (check the system tray) and press Retry.\r\n\r\n" + ex.Message;
            return "KZ Companion could not start its local server.\r\n\r\n" + ex.Message;
        }

        /// <summary>Sobe o servidor e guarda a falha (porta ocupada, runtime ausente...) em vez de engoli-la.</summary>
        private static string[] _args = Array.Empty<string>();
        private static CancellationToken _token;

        /// <summary>Botao Retry da janela: sobe o servidor de novo depois de uma falha (ex.: porta liberada).</summary>
        public static void RestartBackend() => StartBackend(_args, _token);

        private static Task StartBackend(string[] args, CancellationToken token)
        {
            _args = args; _token = token;
            BackendError = null;
            return Task.Run(async () =>
            {
                try { await RunWebAppAsync(args, token); }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    BackendError = ex;
                    Lib.Log.Error($"Falha no servidor: {ex}");
                }
            });
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // Precisa ser a primeira coisa: o instalador/atualizador do Velopack chama o
            // proprio exe com argumentos de hook e espera que ele saia logo. Fora de uma
            // copia instalada (zip, dotnet run) nao faz nada.
            Velopack.VelopackApp.Build()
                .OnFirstRun(_ => FirstRunAfterInstall = true)
                .OnBeforeUninstallFastCallback(_ => frmPrincipal.RemoveAutoStartIfOurs())
                .Run();

            // Reinicio como administrador: a copia nova espera a antiga sair (a trava de instancia unica).
            var waitArg = args.FirstOrDefault(a => a.StartsWith("--wait-pid=", StringComparison.Ordinal));
            if (waitArg != null && int.TryParse(waitArg["--wait-pid=".Length..], out var waitPid))
            {
                try { Process.GetProcessById(waitPid).WaitForExit(15000); } catch { }
            }

            // Auxiliar elevado de sensores (so le hardware e responde por um pipe; sem janela nem bandeja).
            if (args.Contains("--sensor-helper"))
            {
                var pp = args.FirstOrDefault(a => a.StartsWith("--parent-pid=", StringComparison.Ordinal));
                var us = args.FirstOrDefault(a => a.StartsWith("--user-sid=", StringComparison.Ordinal));
                if (pp != null && us != null && int.TryParse(pp["--parent-pid=".Length..], out var ppid))
                    DroidDeck.Services.SensorHelper.RunServer(ppid, us["--user-sid=".Length..]);
                return;
            }

            Lib.Log.DropOversizedLegacyLog();

            // Bind em todas as interfaces — senão o celular na mesma rede recebe "connection
            // refused" (Kestrel sozinho escuta só em localhost). Em dev o launchSettings já
            // define ASPNETCORE_URLS, mas o .exe publicado não lê launchSettings, então
            // garantimos aqui. O usuário ainda pode sobrescrever via variável de ambiente.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{Lib.PairingInfo.Port}");
            }

            // Modo headless: sem janela/bandeja, roda apenas o servidor web.
            // Útil para autostart silencioso, execução sem desktop ou depuração.
            // Ctrl+C encerra graciosamente via ConsoleLifetime do host.
            // Diagnóstico/headless de pareamento: imprime a URI e salva o QR em PNG, depois sai.
            if (args.Contains("--sensors-dump"))
            {
                using var hs = new DroidDeck.Services.HardwareSensors();
                var text = hs.Dump();
                var file = Path.Combine(Lib.AppPaths.LocalDir, "sensors.txt");
                File.WriteAllText(file, text);
                Console.WriteLine(file);
                return;
            }

            if (args.Contains("--print-pairing"))
            {
                var uri = Lib.PairingInfo.BuildUri();
                Console.WriteLine(uri);
                var pngPath = Path.Combine(Path.GetTempPath(), "droiddeck-pair.png");
                File.WriteAllBytes(pngPath, Lib.PairingInfo.BuildQrPng(uri));
                Console.WriteLine("QR_PNG=" + pngPath);
                return;
            }

            // Uma instancia so. A segunda nao consegue a porta 4787 mas ficava viva na bandeja,
            // e se pegasse a vez no boot disputaria o pipe do Discord e o push da Tuya com a
            // primeira. DROIDDECK_DATA_DIR separa as instancias de teste.
            var instanceName = @"Local\KZCompanion.Instance." +
                (Environment.GetEnvironmentVariable("DROIDDECK_DATA_DIR") ?? "default").Replace('\\', '/');
            using var instance = new Mutex(true, instanceName, out var firstInstance);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KZCompanion.Show." + instanceName.GetHashCode());
            if (!firstInstance)
            {
                // Ja esta rodando: so traz a janela dele para a frente.
                try { showEvent.Set(); } catch { }
                return;
            }
            StartMinimized = args.Contains("--minimized");
            DroidDeck.Services.SensorHelper.StartClient();

            if (args.Contains("--headless") || args.Contains("--no-tray"))
            {
                RunWebAppAsync(args, CancellationToken.None).GetAwaiter().GetResult();
                return;
            }

            // Cancellation token to coordinate shutdown between WinForms and the web server
            var cts = new CancellationTokenSource();

            // Inicializa e roda o servidor web em paralelo, observando o token
            var webTask = StartBackend(args, cts.Token);

            // App de bandeja: a janela fica invisível e toda a interação é pelo ícone na bandeja.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            FrmPrincipal = new frmPrincipal();
            var showThread = new Thread(() =>
            {
                while (showEvent.WaitOne())
                    try { FrmPrincipal.BeginInvoke(FrmPrincipal.ShowWindow); } catch { }
            }) { IsBackground = true };
            showThread.Start();

            Application.Run(FrmPrincipal);

            // Tecla presa pelo deck (seta segurada, Shift travado) nao fica apertada no Windows.
            DroidDeck.Lib.Input.HeldKeys.Shared.ReleaseAll();

            // Solicita encerramento gracioso do servidor web e discovery
            try
            {
                cts.Cancel();
            }
            catch { }

            // Garante que o servidor finalize junto
            webTask.Wait();
        }

        /// <summary>
        /// True se a origem (header Origin de uma requisição de navegador) é o próprio PC
        /// (loopback) ou um IP de LAN privada — de onde o configurador web legítimo roda.
        /// Hostnames públicos (ex.: evil.com) retornam false, barrando CORS de sites externos.
        /// </summary>
        private static bool IsLocalOrLanOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return false;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            if (uri.IsLoopback) return true;
            if (!System.Net.IPAddress.TryParse(uri.Host, out var ip)) return false; // hostname público → barra
            var b = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && b.Length == 4)
            {
                return b[0] == 10                                   // 10.0.0.0/8
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)    // 172.16.0.0/12
                    || (b[0] == 192 && b[1] == 168)                 // 192.168.0.0/16
                    || (b[0] == 169 && b[1] == 254)                 // 169.254.0.0/16 (link-local)
                    || b[0] == 127;                                 // 127.0.0.0/8
            }
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal
                    || (b.Length == 16 && (b[0] & 0xFE) == 0xFC);   // fc00::/7 (ULA)
            return false;
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            // .exe publicado: o wwwroot ao lado do executável. Em dev (bin\Debug) o wwwroot do fonte
            // no diretório atual tem preferência (ver Lib/WebRoot.cs).
            var contentRoot = Lib.WebRoot.ResolveContentRoot(Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
            return Host.CreateDefaultBuilder(args)
                .UseContentRoot(contentRoot)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    try
                    {
                        logging.AddNLog();
                    }
                    catch { }
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureServices(services =>
                    {
                        // Variáveis de app/versão
                        services.AddControllers()
                                .AddJsonOptions(options =>
                                {
                                    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                                });

                        // Authentication: API Key
                        services.AddAuthentication("ApiKey")
                                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DroidDeck.Auth.ApiKeyAuthenticationHandler>("ApiKey", null);
                        services.AddAuthorization(options =>
                        {
                            // Exige a API key em TODOS os endpoints, exceto os marcados [AllowAnonymous].
                            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                                .RequireAuthenticatedUser()
                                .Build();
                        });

                        services.AddSingleton<DroidDeck.Audio.IAudioDeviceEnumerator, DroidDeck.Audio.NAudioDeviceEnumerator>();
                        services.AddSingleton<DroidDeck.Services.MixerService>();
                        // Cliente HTTP p/ integrações externas (ex.: busca de sons no MyInstants).
                        services.AddHttpClient();

                        // Conditional Discovery registration (respects env/config)
                        try
                        {
                            var cfg = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                                        .AddJsonFile("appsettings.json", optional: true)
                                        .AddEnvironmentVariables()
                                        .Build();
                            var enableDiscovery = cfg["EnableDiscovery"];
                            if (!string.IsNullOrEmpty(enableDiscovery) && enableDiscovery.ToLowerInvariant() == "true")
                            {
                                services.AddHostedService<DroidDeck.Lib.DiscoveryServer>();
                                Console.WriteLine("[Discovery] Serviço registrado.");
                            }
                            else
                            {
                                Console.WriteLine("[Discovery] Serviço desativado (config).");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Discovery] Erro lendo config: {ex.Message}");
                        }

                        services.AddSwaggerGen(c =>
                        {
                            string appName = Assembly.GetExecutingAssembly().GetName().Name!;
                            string version = "v" + Assembly.GetExecutingAssembly().GetName().Version!.ToString().Replace(".", "").Replace("0", "");
                            c.SwaggerDoc("v1", new OpenApiInfo { Title = appName, Version = version });
                        });
                        services.AddCors(options =>
                        {
                            options.AddPolicy(name: "_myAllowSpecificOrigins", policy =>
                            {
                                // SEGURANÇA: não refletir qualquer origem. Só o próprio host
                                // (loopback) e IPs de LAN privada — que é de onde o configurador
                                // web legítimo roda. Sites públicos (evil.com) são barrados, o que
                                // impede um site que a vítima visite de ler respostas da API.
                                // Sem AllowCredentials: a auth é por header X-API-KEY, não cookie.
                                policy.SetIsOriginAllowed(IsLocalOrLanOrigin)
                                      .AllowAnyHeader()
                                      .AllowAnyMethod();
                            });
                        });

                        // Servicos do DroidDeck, por area (ver ServiceRegistration.cs).
                        if (System.OperatingSystem.IsWindows())
                        {
                            services.AddDeckCore()
                                    .AddActionProviders()
                                    .AddIntegrations();
                        }

                        // Keepalive de 10 s nos dois sentidos e 60 s de tolerancia: um celular que o
                        // Android/EMUI "congela" por alguns segundos nao e derrubado do hub.
                        services.AddSignalR(o =>
                        {
                            o.KeepAliveInterval = TimeSpan.FromSeconds(10);
                            o.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
                            o.HandshakeTimeout = TimeSpan.FromSeconds(15);
                        });
                    });

                    webBuilder.Configure(app =>
                    {
                        var env = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
                        string appName = Assembly.GetExecutingAssembly().GetName().Name!;
                        string version = "v" + Assembly.GetExecutingAssembly().GetName().Version!.ToString().Replace(".", "").Replace("0", "");

                        if (env.IsDevelopment())
                        {
                            app.UseDeveloperExceptionPage();
                            app.UseSwagger();
                            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", $"{appName} {version}"));
                        }

                        // Idioma da resposta (Accept-Language) para L.T.
                        app.UseLanguage();

                        // Enable static files for Web Editor
                        app.UseWebBuildStamp(app.ApplicationServices.GetRequiredService<IWebHostEnvironment>());
                        app.UseDefaultFiles();
                        // Sem Cache-Control o navegador guarda por um tempo que ele mesmo
                        // estima, e depois de uma atualizacao misturava o JS novo com a fonte
                        // de icones antiga (enxuta pelo build): icone novo saia em branco.
                        // no-cache revalida pelo ETag: em localhost custa um 304.
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            OnPrepareResponse = ctx =>
                                ctx.Context.Response.Headers.CacheControl = "no-cache",
                        });

                        app.UseRouting();
                        app.UseCors("_myAllowSpecificOrigins"); // CORS em qualquer ambiente (web client)
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            // Endpoint anônimo para o app checar conectividade (antes de ter a chave).
                            endpoints.MapGet("/api/ping", () =>
                                Microsoft.AspNetCore.Http.Results.Ok(new { ok = true, name = Environment.MachineName, os = WindowsName(), features = new[] { "audio", "installed-apps", "sensor-helper" }, elevated = DroidDeck.Services.HardwareSensors.IsElevated }))
                                .AllowAnonymous();

                            // Entrega a API key APENAS para quem acessa via loopback (o próprio PC).
                            // Deixa o configurador web (servido pelo PC) autenticar sem QR.
                            endpoints.MapGet("/api/pairing/local-key", (Microsoft.AspNetCore.Http.HttpContext ctx) =>
                            {
                                // Loopback que vem do adb e um celular pelo USB: qualquer app dele
                                // alcanca o 127.0.0.1:4787 enquanto o encaminhamento existe.
                                if (!DroidDeck.Services.ActionPolicy.IsLocal(ctx.Connection))
                                    return Microsoft.AspNetCore.Http.Results.StatusCode(403);

                                // SEGURANÇA: bloqueia leitura por site cross-site (o navegador da
                                // vítima faz fetch a partir de 127.0.0.1, mas o browser marca a
                                // requisição). Sec-Fetch-Site é um header proibido — JS não forja.
                                // Fetch de outro site vem "cross-site"; o configurador servido aqui
                                // vem "same-origin"/"none". Sem esse gate, qualquer site leria a chave.
                                var secFetchSite = ctx.Request.Headers["Sec-Fetch-Site"].ToString();
                                if (!string.IsNullOrEmpty(secFetchSite) &&
                                    secFetchSite != "same-origin" && secFetchSite != "none")
                                    return Microsoft.AspNetCore.Http.Results.StatusCode(403);
                                var origin = ctx.Request.Headers["Origin"].ToString();
                                if (!string.IsNullOrEmpty(origin) && !IsLocalOrLanOrigin(origin))
                                    return Microsoft.AspNetCore.Http.Results.StatusCode(403);

                                return Microsoft.AspNetCore.Http.Results.Ok(new { key = DroidDeck.Auth.ApiKeyProvider.GetKey() });
                            }).AllowAnonymous();

                            endpoints.MapControllers();
                            endpoints.MapHub<DroidDeck.Hubs.DeckHub>("/deckHub");
                        });
                    });
                });
        }

        private static async Task RunWebAppAsync(string[] args, CancellationToken token)
        {
            var host = CreateHostBuilder(args).Build();
            await host.RunAsync(token);
        }
    }
}

