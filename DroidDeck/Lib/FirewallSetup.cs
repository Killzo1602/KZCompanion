using System;
using System.Diagnostics;

namespace DroidDeck.Lib
{
    public static class FirewallSetup
    {
        public const int DiscoveryPort = 7573;

        private static string TcpRule => $"DroidDeck (TCP {PairingInfo.Port})";
        private static string UdpRule => $"DroidDeck discovery (UDP {DiscoveryPort})";

        public static bool RulesExist() => RuleExists(TcpRule) && RuleExists(UdpRule);

        private static bool RuleExists(string name)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("netsh",
                    $"advfirewall firewall show rule name=\"{name}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                });
                if (p == null) return false;
                p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool AddRules()
        {
            var commands = string.Join(" & ",
                Delete(TcpRule), Delete(UdpRule),
                Add(TcpRule, "TCP", PairingInfo.Port),
                Add(UdpRule, "UDP", DiscoveryPort));
            return RunElevated(commands);
        }

        public static bool RemoveRules() => RunElevated(string.Join(" & ", Delete(TcpRule), Delete(UdpRule)));

        private static string Delete(string name) =>
            $"netsh advfirewall firewall delete rule name=\"{name}\" >nul 2>&1";

        private static string Add(string name, string protocol, int port) =>
            $"netsh advfirewall firewall add rule name=\"{name}\" dir=in action=allow " +
            $"protocol={protocol} localport={port} remoteip=localsubnet profile=any";

        private static bool RunElevated(string commands)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {commands}")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                if (p == null) return false;
                p.WaitForExit(30000);
                return RulesExistAfter(commands);
            }
            catch (Exception ex)
            {
                Log.Info($"Firewall: elevacao recusada ou falhou ({ex.Message})");
                return false;
            }
        }

        private static bool RulesExistAfter(string commands) =>
            commands.Contains(" add rule") ? RulesExist() : !RuleExists(TcpRule);
    }
}
