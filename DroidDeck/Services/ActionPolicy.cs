using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using DroidDeck.Models;

namespace DroidDeck.Services
{
    public static class ActionPolicy
    {
        private static readonly HashSet<string> PrivilegedTypes =
            new(StringComparer.OrdinalIgnoreCase) { "launchapp", "launch_app", "activatewindow", "hotkey", "key", "keyboard_key", "send_key", "http", "script", "network" };

        private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

        public static bool IsLocal(IPAddress? ip)
        {
            if (ip == null) return false;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            return IPAddress.IsLoopback(ip);
        }

        public static bool IsLocal(Microsoft.AspNetCore.Http.ConnectionInfo connection) =>
            IsLocal(connection.RemoteIpAddress) &&
            !Lib.UsbBridge.IsForwarded(connection.RemoteIpAddress, connection.RemotePort);

        public static bool IsPrivileged(DeckAction? action)
        {
            if (action == null) return false;
            if (PrivilegedTypes.Contains(action.Type ?? "")) return true;
            if (!string.Equals(action.Type, "multi", StringComparison.OrdinalIgnoreCase)) return false;

            if (!action.Parameters.TryGetValue("steps", out var stepsJson) || string.IsNullOrWhiteSpace(stepsJson))
                return false;
            try
            {
                var steps = JsonSerializer.Deserialize<List<MultiStep>>(stepsJson, JsonOpts);
                return steps != null && steps.Any(s => PrivilegedTypes.Contains(s.Type ?? ""));
            }
            catch (JsonException)
            {
                return true;
            }
        }

        public static bool SameAction(DeckAction? a, DeckAction? b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (!string.Equals(a.Type, b.Type, StringComparison.OrdinalIgnoreCase)) return false;
            if (a.Parameters.Count != b.Parameters.Count) return false;
            foreach (var (key, value) in a.Parameters)
            {
                if (!b.Parameters.TryGetValue(key, out var other) || !string.Equals(value, other, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static readonly HashSet<string> PrivilegedInfoTypes =
            new(StringComparer.OrdinalIgnoreCase) { "command", "url" };

        public static bool IsPrivilegedInfo(DeckButton button) =>
            PrivilegedInfoTypes.Contains(button.DynamicType ?? "");

        private static bool SameInfo(DeckButton a, DeckButton b) =>
            string.Equals(a.DynamicType, b.DynamicType, StringComparison.OrdinalIgnoreCase) &&
            SameAction(new DeckAction { Parameters = a.DynamicParameters ?? new() },
                       new DeckAction { Parameters = b.DynamicParameters ?? new() });

        public static DeckButton? FindRemoteViolation(DeckProfile incoming, DeckProfile? saved)
        {
            foreach (var button in incoming.Buttons ?? new List<DeckButton>())
            {
                var before = saved?.Buttons?.FirstOrDefault(b => b.Id == button.Id);
                foreach (var slot in DeckButton.ActionSlots)
                {
                    var action = button.ActionFor(slot);
                    if (!IsPrivileged(action)) continue;
                    if (before == null || !SameAction(before.ActionFor(slot), action))
                        return button;
                }
                if (IsPrivilegedInfo(button) &&
                    (before == null || !SameInfo(before, button)))
                    return button;
            }
            return null;
        }
    }
}
