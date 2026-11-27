namespace DroidDeck
{
    public class MixerData
    {
        public int Session { get; set; } = -1;

        public int? Volume { get; set; }

        public bool? Mute { get; set; }

        public MixerData() { }

        public MixerData(int? volume = null, bool? mute = null, int session = -1)
        {
            Volume = volume;
            Mute = mute;
            Session = session;
        }
    }
}
