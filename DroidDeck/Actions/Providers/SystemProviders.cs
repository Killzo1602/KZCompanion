using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DroidDeck.Lib.Input;
using DroidDeck.Services;

namespace DroidDeck.Actions.Providers
{
    public class LaunchAppProvider : IActionProvider
    {
        private readonly IAppActivator _activator;

        public LaunchAppProvider(IAppActivator activator) => _activator = activator;

        public string Type => "launch_app";
        public IReadOnlyCollection<string> Aliases => new[] { "launchapp" };

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Open program",
            Icon = "launch",
            Category = "System",
            Description = "Opens a program, file or address on the PC.",
            Fields =
            {
                ActionField.Text("path", "Path", @"C:\Windows\System32\notepad.exe",
                    "Also accepts a file or a URL.", required: true),
                ActionField.Text("arguments", "Arguments (optional)"),
            },
        };

        public Task ExecuteAsync(ActionContext c)
        {
            var path = c.Get("path");
            if (path.Length > 0) _activator.LaunchApp(path, c.Get("arguments"));
            return Task.CompletedTask;
        }
    }

    public class ActivateWindowProvider : IActionProvider
    {
        private readonly IAppActivator _activator;

        public ActivateWindowProvider(IAppActivator activator) => _activator = activator;

        public string Type => "activatewindow";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Activate window",
            Icon = "web_asset",
            Category = "System",
            Fields = { ActionField.Text("windowName", "Exact window title", required: true) },
        };

        public Task ExecuteAsync(ActionContext c)
        {
            _activator.ActivateWindow(c.Get("windowName"));
            return Task.CompletedTask;
        }
    }

    public class KeyProvider : IActionProvider, IDisposable
    {
        public const string LatchOwner = "latch";

        private readonly ButtonToggleService _toggles;
        private readonly HashSet<string> _latchedButtons = new();

        public KeyProvider(ButtonToggleService toggles)
        {
            _toggles = toggles;
            HeldKeys.Shared.OneShotReleased += OnOneShotReleased;
        }

        public string Type => "key";
        public IReadOnlyCollection<string> Aliases => new[] { "keyboard_key", "send_key" };

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Keyboard key",
            Icon = "keyboard",
            Category = "System",
            Description = "Presses a key on the PC: holds it while your finger is on the button, taps it, or keeps it latched until the next tap.",
            Fields =
            {
                new ActionField
                {
                    Key = "key",
                    Label = "Key",
                    Kind = FieldKind.Select,
                    AllowCustom = true,
                    Required = true,
                    Default = "UP",
                    Help = "Pick from the list or type another: A-Z, 0-9, F13-F24, NUMPAD0 or the code (0x41).",
                    Options = KeyOptions.Select(o => new FieldOption(o.value, o.label)).ToList(),
                },
                ActionField.Select("mode", "Send mode",
                    ("hold", "Hold while your finger is on the button"),
                    ("tap", "Quick tap"),
                    ("toggle", "Latch: one tap presses, the next releases"),
                    ("oneshot", "Latch until the next key"))
                    .WithDefault("hold"),
            },
        };

        private static readonly (string value, string label)[] KeyOptions =
        {
            ("UP", "Up arrow"), ("DOWN", "Down arrow"),
            ("LEFT", "Left arrow"), ("RIGHT", "Right arrow"),
            ("SHIFT", "Shift"), ("CTRL", "Ctrl"), ("ALT", "Alt"), ("WIN", "Windows"),
            ("ENTER", "Enter"), ("ESC", "Esc"), ("TAB", "Tab"), ("SPACE", "Space"), ("BACKSPACE", "Backspace"),
            ("INSERT", "Insert"), ("DELETE", "Delete"), ("HOME", "Home"), ("END", "End"),
            ("PGUP", "Page Up"), ("PGDN", "Page Down"),
            ("F1", "F1"), ("F2", "F2"), ("F3", "F3"), ("F4", "F4"), ("F5", "F5"), ("F6", "F6"),
            ("F7", "F7"), ("F8", "F8"), ("F9", "F9"), ("F10", "F10"), ("F11", "F11"), ("F12", "F12"),
        };

        public async Task ExecuteAsync(ActionContext c)
        {
            if (!KeyboardInput.TryResolveKey(c.Get("key"), out var vk)) return;

            var mode = c.Get("mode");
            var oneShot = string.Equals(mode, "oneshot", StringComparison.OrdinalIgnoreCase);
            if (oneShot || string.Equals(mode, "toggle", StringComparison.OrdinalIgnoreCase))
            {
                var owner = (oneShot ? HeldKeys.OneShotPrefix : LatchOwner + ":") + (c.ButtonId ?? vk.ToString());
                var on = !HeldKeys.Shared.IsHeld(owner, vk);
                if (on) HeldKeys.Shared.Down(owner, vk);
                else HeldKeys.Shared.Up(owner, vk);
                if (c.ButtonId != null)
                {
                    lock (_latchedButtons)
                    {
                        if (on) _latchedButtons.Add(c.ButtonId);
                        else _latchedButtons.Remove(c.ButtonId);
                    }
                    _toggles.Set(c.ButtonId, on);
                }
                return;
            }

            KeyboardInput.KeyDown(vk);
            await Task.Delay(KeyboardInput.HoldMs);
            KeyboardInput.KeyUp(vk);
            HeldKeys.Shared.KeyTapped(vk);
        }

        private void OnOneShotReleased(string owner)
        {
            var buttonId = owner[HeldKeys.OneShotPrefix.Length..];
            lock (_latchedButtons)
                if (!_latchedButtons.Remove(buttonId)) return;
            _toggles.Set(buttonId, false);
        }

        public void Dispose()
        {
            HeldKeys.Shared.OneShotReleased -= OnOneShotReleased;
            string[] latched;
            lock (_latchedButtons) latched = _latchedButtons.ToArray();
            foreach (var id in latched) _toggles.Set(id, false);
            HeldKeys.Shared.ReleaseAll();
        }
    }

    public class HotkeyProvider : IActionProvider
    {
        private readonly IAppActivator _activator;

        public HotkeyProvider(IAppActivator activator) => _activator = activator;

        public string Type => "hotkey";

        public ActionSchema Describe() => new()
        {
            Type = Type,
            Title = "Keyboard shortcut",
            Icon = "keyboard",
            Category = "System",
            Fields =
            {
                ActionField.Text("keys", "Keys", "^C", "^ = Ctrl, + = Shift, % = Alt, # = Win, {ENTER}, {F5}…", required: true),
            },
        };

        public Task ExecuteAsync(ActionContext c)
        {
            var keys = c.Get("keys");
            if (keys.Length == 0) return Task.CompletedTask;
            _activator.SendKeys(keys);
            HeldKeys.Shared.KeyTapped();
            return Task.CompletedTask;
        }
    }

    public class ClientOnlyProvider : IActionProvider
    {
        private readonly ActionSchema _schema;

        public ClientOnlyProvider(ActionSchema schema) => _schema = schema;

        public string Type => _schema.Type;
        public ActionSchema Describe() => _schema;
        public Task ExecuteAsync(ActionContext c) => Task.CompletedTask;

        public static IEnumerable<IActionProvider> All() => new IActionProvider[]
        {
            new ClientOnlyProvider(new ActionSchema
            {
                Type = "open_profile", Title = "Open profile", Icon = "folder", Category = "Navigation",
                Description = "Opens another profile like a folder.", Editor = "open_profile",
            }),
            new ClientOnlyProvider(new ActionSchema
            {
                Type = "back", Title = "Back", Icon = "arrow_back", Category = "Navigation",
                Description = "Goes back to the previous profile.",
            }),
        };
    }
}
