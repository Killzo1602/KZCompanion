using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DroidDeck.Lib.Input
{
    public sealed class HeldKeys : IDisposable
    {
        public static HeldKeys Shared { get; } = new(KeyboardInput.KeyDown, KeyboardInput.KeyUp);

        private static readonly HashSet<ushort> Modifiers = new()
        {
            0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x14, 0x90, 0x91,
        };

        private readonly Action<ushort> _down;
        private readonly Action<ushort> _up;
        private readonly Dictionary<string, HashSet<ushort>> _owners = new();
        private readonly object _sync = new();
        private readonly System.Threading.Timer _repeatTimer;
        private ushort _repeatKey;
        private ushort _oneShotTrigger;
        private readonly List<string> _released = new();

        public const string OneShotPrefix = "oneshot:";

        public event Action<string>? OneShotReleased;

        public TimeSpan RepeatDelay { get; set; } = SystemRepeatDelay();
        public TimeSpan RepeatInterval { get; set; } = SystemRepeatInterval();

        public HeldKeys(Action<ushort> down, Action<ushort> up)
        {
            _down = down;
            _up = up;
            _repeatTimer = new System.Threading.Timer(_ => Repeat());
        }

        public IReadOnlyCollection<ushort> Keys
        {
            get { lock (_sync) return _owners.Values.SelectMany(s => s).ToHashSet(); }
        }

        public bool IsHeld(string owner, ushort vk)
        {
            lock (_sync) return _owners.TryGetValue(owner, out var keys) && keys.Contains(vk);
        }

        public void Down(string owner, ushort vk)
        {
            lock (_sync)
            {
                if (!_owners.TryGetValue(owner, out var keys))
                    _owners[owner] = keys = new HashSet<ushort>();
                if (!keys.Add(vk)) return;
                _down(vk);
                if (!Modifiers.Contains(vk))
                {
                    _repeatKey = vk;
                    _repeatTimer.Change(RepeatDelay, RepeatInterval);
                    if (!owner.StartsWith(OneShotPrefix, StringComparison.Ordinal)) _oneShotTrigger = vk;
                }
            }
        }

        public void Up(string owner, ushort vk)
        {
            lock (_sync)
            {
                if (!_owners.TryGetValue(owner, out var keys) || !keys.Remove(vk)) return;
                if (keys.Count == 0) _owners.Remove(owner);
                ReleaseIfFree(vk);
            }
            FireReleased();
        }

        public void ReleaseOwner(string owner)
        {
            lock (_sync)
            {
                if (!_owners.Remove(owner, out var keys)) return;
                foreach (var vk in keys) ReleaseIfFree(vk);
            }
            FireReleased();
        }

        public void KeyTapped(ushort? vk = null)
        {
            if (vk is ushort k && Modifiers.Contains(k)) return;
            lock (_sync) ReleaseOneShots();
            FireReleased();
        }

        public void ReleaseAll()
        {
            lock (_sync)
            {
                var all = _owners.Values.SelectMany(s => s).ToHashSet();
                _owners.Clear();
                _oneShotTrigger = 0;
                StopRepeat();
                foreach (var vk in all) _up(vk);
            }
        }

        private void ReleaseIfFree(ushort vk)
        {
            if (_owners.Values.Any(s => s.Contains(vk))) return;
            if (_repeatKey == vk) StopRepeat();
            _up(vk);
            if (vk == _oneShotTrigger) ReleaseOneShots();
        }

        private void ReleaseOneShots()
        {
            _oneShotTrigger = 0;
            foreach (var owner in _owners.Keys.Where(o => o.StartsWith(OneShotPrefix, StringComparison.Ordinal)).ToList())
            {
                _owners.Remove(owner, out var keys);
                foreach (var vk in keys!) ReleaseIfFree(vk);
                _released.Add(owner);
            }
        }

        private void FireReleased()
        {
            string[] owners;
            lock (_sync)
            {
                if (_released.Count == 0) return;
                owners = _released.ToArray();
                _released.Clear();
            }
            foreach (var owner in owners) OneShotReleased?.Invoke(owner);
        }

        private void StopRepeat()
        {
            _repeatKey = 0;
            _repeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void Repeat()
        {
            lock (_sync)
            {
                if (_repeatKey == 0) return;
                _down(_repeatKey);
            }
        }

        private static TimeSpan SystemRepeatDelay()
        {
            try { return TimeSpan.FromMilliseconds(250 * (1 + System.Windows.Forms.SystemInformation.KeyboardDelay)); }
            catch { return TimeSpan.FromMilliseconds(500); }
        }

        private static TimeSpan SystemRepeatInterval()
        {
            try
            {
                var perSecond = 2.5 + System.Windows.Forms.SystemInformation.KeyboardSpeed * (27.5 / 31);
                return TimeSpan.FromMilliseconds(1000 / perSecond);
            }
            catch { return TimeSpan.FromMilliseconds(33); }
        }

        public void Dispose() => _repeatTimer.Dispose();
    }
}
