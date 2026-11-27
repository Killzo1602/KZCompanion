using DroidDeck.Actions;
using DroidDeck.Actions.Providers;
using DroidDeck.Integrations;
using DroidDeck.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DroidDeck
{
    public static class ServiceRegistration
    {
        public static IServiceCollection AddDeckCore(this IServiceCollection services)
        {
            services.AddSingleton<IAudioControlService, AudioControlService>();
            services.AddSingleton<AppLibrary>();
            services.AddSingleton<InstalledApps>();
            services.AddSingleton<HardwareSensors>();
            services.AddHostedSingleton<AudioMixerService>();
            services.AddSingleton<IAppActivator, AppActivator>();
            services.AddSingleton<MediaControlService>();
            services.AddSingleton<StreamDeckConfigService>();
            services.AddSingleton<ActionExecutorService>();
            services.AddHostedService<SystemMonitorService>();
            services.AddSingleton<SteamLibrary>();
            services.AddSingleton<BackupService>();
            services.AddSingleton<ButtonToggleService>();
            services.AddSingleton<Conditions>();
            services.AddSingleton<Lib.Input.MacroRecorder>();
            services.AddHostedSingleton<TimerService>();
            services.AddSingleton<AiUsageService>();
            services.AddSingleton<DevStatusService>();
            services.AddHostedSingleton<InfoService>();
            services.AddHostedSingleton<ForegroundWatcher>();
            services.AddSingleton<RunningAppsService>();
            services.AddHostedSingleton<Lib.UsbBridge>();
            services.AddHostedSingleton<TriggerService>();
            return services;
        }

        public static IServiceCollection AddActionProviders(this IServiceCollection services)
        {
            services.AddSingleton<IActionProvider, KeyProvider>();
            services.AddSingleton<IActionProvider, HotkeyProvider>();
            services.AddSingleton<IActionProvider, LaunchAppProvider>();
            services.AddSingleton<IActionProvider, ActivateWindowProvider>();
            services.AddSingleton<IActionProvider, MediaProvider>();
            services.AddSingleton<IActionProvider, MixerProvider>();
            services.AddSingleton<IActionProvider, MultiProvider>();
            services.AddSingleton<IActionProvider, DiscordProvider>();
            services.AddSingleton<IActionProvider, ObsProvider>();
            services.AddSingleton<IActionProvider, SoundboardProvider>();
            services.AddSingleton<IActionProvider, TuyaProvider>();
            services.AddSingleton<IActionProvider, WindowsProvider>();
            services.AddSingleton<IActionProvider, HttpProvider>();
            services.AddSingleton<IActionProvider, NetworkProvider>();
            services.AddSingleton<IActionProvider, ScriptProvider>();
            services.AddSingleton<IActionProvider, TimerProvider>();
            services.AddSingleton<IActionProvider, SteamProvider>();
            services.AddSingleton<IActionProvider, HomeAssistantProvider>();
            services.AddSingleton<IActionProvider, SpotifyProvider>();
            services.AddSingleton<IActionProvider, TwitchProvider>();
            services.AddSingleton<IActionProvider, YouTubeProvider>();
            services.AddSingleton<IActionProvider, StreamerBotProvider>();
            services.AddSingleton<IActionProvider, VTubeStudioProvider>();
            services.AddSingleton<IActionProvider, VoicemeeterProvider>();
            services.AddSingleton<IActionProvider, HueProvider>();
            services.AddSingleton<IActionProvider, KeyLightProvider>();
            services.AddSingleton<IActionProvider, GoveeProvider>();
            services.AddSingleton<IActionProvider, MqttProvider>();
            services.AddSingleton<IActionProvider, TallyProvider>();
            services.AddSingleton<IActionProvider, VmixProvider>();
            services.AddSingleton<IActionProvider, CompanionProvider>();
            services.AddSingleton<IActionProvider, MairlistProvider>();
            services.AddSingleton<IActionProvider, RadioDjProvider>();
            foreach (var clientOnly in ClientOnlyProvider.All())
                services.AddSingleton(clientOnly);
            return services;
        }

        public static IServiceCollection AddIntegrations(this IServiceCollection services)
        {
            services.AddIntegration<DiscordRpcService>();
            services.AddHostedService<DiscordAutoConnect>();
            services.AddIntegration<ObsService>();
            services.AddHostedService<ObsAutoConnect>();
            services.AddIntegration<TuyaService>();
            services.AddHostedService<TuyaAutoConnect>();
            services.AddIntegration<SoundboardService>();

            services.AddAccountIntegration<HomeAssistantIntegration>();
            services.AddAccountIntegration<SpotifyIntegration>();
            services.AddAccountIntegration<TwitchIntegration>();
            services.AddAccountIntegration<YouTubeIntegration>();

            services.AddAccountIntegration<HueIntegration>();
            services.AddAccountIntegration<MqttIntegration>();
            services.AddAccountIntegration<StreamerBotIntegration>();
            services.AddAccountIntegration<VTubeStudioIntegration>();
            services.AddAccountIntegration<VmixIntegration>();
            services.AddAccountIntegration<CompanionIntegration>();
            services.AddAccountIntegration<MairlistIntegration>();
            services.AddAccountIntegration<RadioDjIntegration>();
            services.AddSingleton<IVoicemeeterApi, VoicemeeterRemote>();
            services.AddSingleton<MicHoldService>();
            services.AddHostedSingleton<TallyService>();
            services.AddSingleton<GoveeLan>();
            return services;
        }

        private static void AddIntegration<T>(this IServiceCollection services) where T : class, IIntegration
        {
            services.AddSingleton<T>();
            services.AddSingleton<IIntegration>(sp => sp.GetRequiredService<T>());
        }

        private static void AddAccountIntegration<T>(this IServiceCollection services) where T : AccountIntegration
        {
            services.AddIntegration<T>();
            services.AddSingleton<AccountIntegration>(sp => sp.GetRequiredService<T>());
            services.AddHostedService(sp => sp.GetRequiredService<T>());
        }

        private static void AddHostedSingleton<T>(this IServiceCollection services)
            where T : class, Microsoft.Extensions.Hosting.IHostedService
        {
            services.AddSingleton<T>();
            services.AddHostedService(sp => sp.GetRequiredService<T>());
        }
    }
}
