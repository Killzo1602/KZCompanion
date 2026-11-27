using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;

namespace DroidDeck.Audio
{
    public interface IAudioSession : IDisposable
    {
        bool IsSystemSoundsSession { get; }
        uint GetProcessID();
        float Volume { get; }
        bool Mute { get; set; }
    }

    public interface IAudioDevice : IDisposable
    {
        string Id { get; }
        string FriendlyName { get; }
        string DeviceFriendlyName { get; }
        DataFlow DataFlow { get; }
        float MasterVolumeLevelScalar { get; }
        bool Mute { get; }
        IEnumerable<IAudioSession> Sessions { get; }
    }
}
