using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace DroidDeck.Audio
{
    public static class DefaultAudioEndpoint
    {
        public static void Set(string deviceId)
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                    Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(deviceId, role));
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }

        public static string? GetDefault(bool input)
        {
            try
            {
                using var en = new MMDeviceEnumerator();
                using var device = en.GetDefaultAudioEndpoint(input ? DataFlow.Capture : DataFlow.Render, Role.Multimedia);
                return device.ID;
            }
            catch
            {
                return null;
            }
        }

        public static List<(string Id, string Name)> List(bool input)
        {
            var result = new List<(string, string)>();
            using var en = new MMDeviceEnumerator();
            var devices = en.EnumerateAudioEndPoints(input ? DataFlow.Capture : DataFlow.Render, DeviceState.Active);
            foreach (var d in devices)
            {
                using (d) result.Add((d.ID, d.FriendlyName));
            }
            return result;
        }

        private enum ERole
        {
            Console = 0,
            Multimedia = 1,
            Communications = 2,
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        private class PolicyConfigClient { }

        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(string deviceId, IntPtr format);
            [PreserveSig] int GetDeviceFormat(string deviceId, bool defaultFormat, IntPtr format);
            [PreserveSig] int ResetDeviceFormat(string deviceId);
            [PreserveSig] int SetDeviceFormat(string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod(string deviceId, bool defaultPeriod, IntPtr defaultPeriodOut, IntPtr minimumPeriod);
            [PreserveSig] int SetProcessingPeriod(string deviceId, IntPtr period);
            [PreserveSig] int GetShareMode(string deviceId, IntPtr mode);
            [PreserveSig] int SetShareMode(string deviceId, IntPtr mode);
            [PreserveSig] int GetPropertyValue(string deviceId, bool fxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue(string deviceId, bool fxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
            [PreserveSig] int SetEndpointVisibility(string deviceId, bool visible);
        }
    }
}
