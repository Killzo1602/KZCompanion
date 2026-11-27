using System.Collections.Generic;
using System.Linq;
using DroidDeck;
using DroidDeck.Audio;
using Moq;
using NAudio.CoreAudioApi;
using Xunit;

namespace DroidDeck.Tests
{
    public class MixerChannelEnrichmentTests
    {
        private static Mock<IAudioSession> FakeSession(int pid, bool systemSounds = false)
        {
            var session = new Mock<IAudioSession>();
            session.SetupGet(s => s.IsSystemSoundsSession).Returns(systemSounds);
            session.Setup(s => s.GetProcessID()).Returns((uint)pid);
            session.SetupGet(s => s.Volume).Returns(0.5f);
            session.SetupGet(s => s.Mute).Returns(false);
            return session;
        }

        private static Mock<IAudioDevice> FakeRenderDevice(params IAudioSession[] sessions)
        {
            var device = new Mock<IAudioDevice>();
            device.SetupGet(d => d.Id).Returns("dispositivo-de-teste");
            device.SetupGet(d => d.FriendlyName).Returns("Alto-falantes (Teste)");
            device.SetupGet(d => d.DeviceFriendlyName).Returns("Placa de Teste");
            device.SetupGet(d => d.DataFlow).Returns(DataFlow.Render);
            device.SetupGet(d => d.MasterVolumeLevelScalar).Returns(1.0f);
            device.SetupGet(d => d.Mute).Returns(false);
            device.SetupGet(d => d.Sessions).Returns(sessions.ToList());
            return device;
        }

        [Fact]
        public void DescribeFromProcess_FillsIdAndDescription_ForLiveProcess()
        {
            var channel = new MixerChannel();
            channel.DescribeFromProcess(System.Environment.ProcessId);

            Assert.Equal(System.Environment.ProcessId, channel.Id);
            Assert.False(string.IsNullOrWhiteSpace(channel.Description));
        }

        [Fact]
        public void DescribeFromProcess_LeavesDefaults_WhenProcessIsGone()
        {
            var channel = new MixerChannel { Description = "intocado" };

            channel.DescribeFromProcess(-1);

            Assert.Equal("intocado", channel.Description);
        }

        [Fact]
        public void AbstractionPath_PopulatesChannelDescription()
        {
            var pid = System.Environment.ProcessId;
            var master = new MixerMaster(FakeRenderDevice(FakeSession(pid).Object).Object);

            var channel = Assert.Single(master.Channels!);
            Assert.Equal(pid, channel.Id);
            Assert.False(string.IsNullOrWhiteSpace(channel.Description));
        }

        [Fact]
        public void AbstractionPath_SkipsSystemSoundsSession()
        {
            var device = FakeRenderDevice(FakeSession(System.Environment.ProcessId, systemSounds: true).Object);
            var master = new MixerMaster(device.Object);

            Assert.Empty(master.Channels!);
        }

        [Fact]
        public void AbstractionPath_CopiesDeviceProperties()
        {
            var master = new MixerMaster(FakeRenderDevice().Object);

            Assert.Equal("dispositivo-de-teste", master.Id);
            Assert.Equal("Alto-falantes", master.Title);
            Assert.Equal("Placa de Teste", master.Description);
            Assert.Equal(100, master.Volume);
        }

        [Fact]
        public void AbstractionPath_IgnoresSessionsOnCaptureDevices()
        {
            var device = FakeRenderDevice(FakeSession(System.Environment.ProcessId).Object);
            device.SetupGet(d => d.DataFlow).Returns(DataFlow.Capture);

            var master = new MixerMaster(device.Object);

            Assert.Null(master.Channels);
        }
    }
}
