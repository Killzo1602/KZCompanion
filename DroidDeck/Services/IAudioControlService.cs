namespace DroidDeck.Services
{
    public interface IAudioControlService
    {
        int MuteByProcessName(string processName, bool mute);

        bool ToggleMuteByProcessName(string processName);

        bool? GetMuteStateByProcessName(string processName);
    }
}
