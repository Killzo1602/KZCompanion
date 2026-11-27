using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using DroidDeck.Lib;
using DroidDeck.Models;

namespace DroidDeck.Services
{
    public static class SensorHelper
    {
        private static string PipeName(string sid) => "KZCompanion.Sensors." + sid;

        public static void RunServer(int parentPid, string userSid)
        {
            using var single = new Mutex(true, @"Global\KZCompanion.SensorHelper." + userSid, out var first);
            if (!first) return;

            using var hs = new HardwareSensors();
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(userSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl, AccessControlType.Allow));

            while (ParentAlive(parentPid))
            {
                try
                {
                    using var pipe = NamedPipeServerStreamAcl.Create(PipeName(userSid), PipeDirection.Out, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
                    var wait = pipe.WaitForConnectionAsync();
                    while (!wait.Wait(2000))
                        if (!ParentAlive(parentPid)) return;
                    using var writer = new StreamWriter(pipe) { AutoFlush = true };
                    while (pipe.IsConnected && ParentAlive(parentPid))
                    {
                        var s = new SystemStats();
                        hs.Fill(s);
                        writer.WriteLine(JsonSerializer.Serialize(s));
                        Thread.Sleep(1000);
                    }
                }
                catch (IOException) {  }
                catch (Exception ex) { Log.Warning($"Auxiliar de sensores: {ex.Message}"); Thread.Sleep(1000); }
            }
        }

        private static bool ParentAlive(int pid)
        {
            try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
            catch { return false; }
        }

        private static readonly object Gate = new();
        private static SystemStats? _latest;
        private static DateTime _at = DateTime.MinValue;
        private static Thread? _thread;

        public static bool Connected
        {
            get { lock (Gate) return _latest != null && DateTime.UtcNow - _at < TimeSpan.FromSeconds(5); }
        }

        public static bool TryGet(out SystemStats stats)
        {
            lock (Gate)
            {
                stats = _latest!;
                return _latest != null && DateTime.UtcNow - _at < TimeSpan.FromSeconds(5);
            }
        }

        public static void StartClient()
        {
            if (_thread != null || HardwareSensors.IsElevated) return;
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            _thread = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using var pipe = new NamedPipeClientStream(".", PipeName(sid), PipeDirection.In);
                        pipe.Connect(1500);
                        using var reader = new StreamReader(pipe);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            var s = JsonSerializer.Deserialize<SystemStats>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (s == null) continue;
                            lock (Gate) { _latest = s; _at = DateTime.UtcNow; }
                        }
                    }
                    catch {  }
                    Thread.Sleep(3000);
                }
            }) { IsBackground = true, Name = "SensorHelperClient" };
            _thread.Start();
        }

        public static bool LaunchElevated()
        {
            try
            {
                var sid = WindowsIdentity.GetCurrent().User!.Value;
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!,
                    $"--sensor-helper --parent-pid={Environment.ProcessId} --user-sid={sid}")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
                return true;
            }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
    }
}
