using NAudio.CoreAudioApi;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace DroidDeck
{
    public class MixerChannel
    {
        public MixerChannel() { }

        public MixerChannel(AudioSessionControl session, float masterVolume)
        {
            Id = 0;
            Description = "Windows";

            if (!session.IsSystemSoundsSession)
            {
                DescribeFromProcess((int)session.GetProcessID);
            }

            Volume = (int)(session.SimpleAudioVolume.Volume * masterVolume * 100);
            Mute = session.SimpleAudioVolume.Mute;
        }

        public void DescribeFromProcess(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                Id = pid;
                Description = (process.ProcessName == "Spotify" ? process.ProcessName + ": " : "") + (!string.IsNullOrEmpty(process.MainWindowTitle) ? process.MainWindowTitle : process.ProcessName);

                var moduleFile = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(moduleFile))
                {
                    using var icon = System.Drawing.Icon.ExtractAssociatedIcon(moduleFile!);
                    if (icon != null)
                    {
                        using var bmp = icon.ToBitmap();
                        using var ms = new MemoryStream();
                        bmp.Save(ms, ImageFormat.Png);
                        Icon = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch
            {
            }
        }

        public static void Apply(AudioSessionControl session, MixerData data, float masterVolume)
        {
            float volume = (data.Volume ?? -1.0f) / 100.0f;
            var newVolume = volume / masterVolume;
            bool mute = data.Mute == true;

            if (mute && session.SimpleAudioVolume.Volume >= newVolume)
                session.SimpleAudioVolume.Mute = mute;
            else
                if (newVolume > 0)
            {
                if (newVolume <= 1)
                    session.SimpleAudioVolume.Volume = newVolume;
                else
                    session.SimpleAudioVolume.Volume = 1;
                session.SimpleAudioVolume.Mute = false;
            }
            else
                session.SimpleAudioVolume.Mute = mute;
        }

        public int Id { get; set; }
        public string? Description { get; set; }
        public int Volume { get; set; }
        public string? Icon { get; set; }
        public bool Mute { get; set; }
    }
}
