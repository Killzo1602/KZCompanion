using DroidDeck.Lib;

namespace DroidDeck.Mixer
{
    public class MixerEntity
    {
        public MixerEntity(MixerMaster master)
        {
            Device = master ?? throw new ArgumentNullException(nameof(master));

            if (string.IsNullOrEmpty(Device.Id))
                throw new ArgumentException("O dispositivo MixerMaster deve ter um ID válido.", nameof(master));

            Id = Device.Id;

            Config = DBHelper.Retrieve(Device.Id) as MixerConfig
                     ?? new MixerConfig(true, master.Title ?? string.Empty);
        }

        public MixerMaster Device { get; private set; }
        public MixerConfig Config { get; private set; }

        public void SetOptions(MixerData data)
        {
            Device.SetOptions(Id, data);

            Volume = Device.Volume;
            Mute = Device.Mute;
        }

        public string Id { get; private set; } = string.Empty;
        public int Volume { get; private set; }
        public bool Mute { get; private set; }

        public MixerChannel? GetSession(int sessionId)
        {
            return Device.Channels?.FirstOrDefault(c => c.Id == sessionId);
        }
    }
}
