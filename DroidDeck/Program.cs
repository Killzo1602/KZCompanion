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

        public static bool FirstRunAfterInstall { get; private set; }

        public static readonly AppUpdater Updater = new();

        public static Exception? BackendError { get; private set; }

        public static bool Exiting { get; set; }

        public static bool StartMinimized { get; private set; }

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

        private static string[] _args = Array.Empty<string>();
        private static CancellationToken _token;

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
            Velopack.VelopackApp.Build()
                .OnFirstRun(_ => FirstRunAfterInstall = true)
                .OnBeforeUninstallFastCallback(_ => frmPrincipal.RemoveAutoStartIfOurs())
                .Run();

            var waitArg = args.FirstOrDefault(a => a.StartsWith("--wait-pid=", StringComparison.Ordinal));
            if (waitArg != null && int.TryParse(waitArg["--wait-pid=".Length..], out var waitPid))
            {
                try { Process.GetProcessById(waitPid).WaitForExit(15000); } catch { }
            }

            if (args.Contains("--sensor-helper"))
            {
                var pp = args.FirstOrDefault(a => a.StartsWith("--parent-pid=", StringComparison.Ordinal));
                var us = args.FirstOrDefault(a => a.StartsWith("--user-sid=", StringComparison.Ordinal));
                if (pp != null && us != null && int.TryParse(pp["--parent-pid=".Length..], out var ppid))
                    DroidDeck.Services.SensorHelper.RunServer(ppid, us["--user-sid=".Length..]);
                return;
            }

            Lib.Log.DropOversizedLegacyLog();

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{Lib.PairingInfo.Port}");
            }

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

            var instanceName = @"Local\KZCompanion.Instance." +
                (Environment.GetEnvironmentVariable("DROIDDECK_DATA_DIR") ?? "default").Replace('\\', '/');
            using var instance = new Mutex(true, instanceName, out var firstInstance);
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KZCompanion.Show." + instanceName.GetHashCode());
            if (!firstInstance)
            {
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

            var cts = new CancellationTokenSource();

            var webTask = StartBackend(args, cts.Token);

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

            DroidDeck.Lib.Input.HeldKeys.Shared.ReleaseAll();

            try
            {
                cts.Cancel();
            }
            catch { }

            webTask.Wait();
        }

        private static bool IsLocalOrLanOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return false;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            if (uri.IsLoopback) return true;
            if (!System.Net.IPAddress.TryParse(uri.Host, out var ip)) return false;
            var b = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && b.Length == 4)
            {
                return b[0] == 10
                    || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                    || (b[0] == 192 && b[1] == 168)
                    || (b[0] == 169 && b[1] == 254)
                    || b[0] == 127;
            }
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal
                    || (b.Length == 16 && (b[0] & 0xFE) == 0xFC);
            return false;
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
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
                        services.AddControllers()
                                .AddJsonOptions(options =>
                                {
                                    options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                                });

                        services.AddAuthentication("ApiKey")
                                .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DroidDeck.Auth.ApiKeyAuthenticationHandler>("ApiKey", null);
                        services.AddAuthorization(options =>
                        {
                            options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                                .RequireAuthenticatedUser()
                                .Build();
                        });

                        services.AddSingleton<DroidDeck.Audio.IAudioDeviceEnumerator, DroidDeck.Audio.NAudioDeviceEnumerator>();
                        services.AddSingleton<DroidDeck.Services.MixerService>();
                        services.AddHttpClient();

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
                                policy.SetIsOriginAllowed(IsLocalOrLanOrigin)
                                      .AllowAnyHeader()
                                      .AllowAnyMethod();
                            });
                        });

                        if (System.OperatingSystem.IsWindows())
                        {
                            services.AddDeckCore()
                                    .AddActionProviders()
                                    .AddIntegrations();
                        }

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

                        app.UseLanguage();

                        app.UseWebBuildStamp(app.ApplicationServices.GetRequiredService<IWebHostEnvironment>());
                        app.UseDefaultFiles();
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            OnPrepareResponse = ctx =>
                                ctx.Context.Response.Headers.CacheControl = "no-cache",
                        });

                        app.UseRouting();
                        app.UseCors("_myAllowSpecificOrigins");
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/api/ping", () =>
                                Microsoft.AspNetCore.Http.Results.Ok(new { ok = true, name = Environment.MachineName, os = WindowsName(), features = new[] { "audio", "installed-apps", "sensor-helper" }, elevated = DroidDeck.Services.HardwareSensors.IsElevated }))
                                .AllowAnonymous();

                            endpoints.MapGet("/api/pairing/local-key", (Microsoft.AspNetCore.Http.HttpContext ctx) =>
                            {
                                if (!DroidDeck.Services.ActionPolicy.IsLocal(ctx.Connection))
                                    return Microsoft.AspNetCore.Http.Results.StatusCode(403);

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

