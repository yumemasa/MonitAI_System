using NAudio.CoreAudioApi;
using System;
using System.Data;
using System.Threading.Tasks;

namespace LockLibrary
{
    public static class AudioAlert
    {
        public static async Task PlayBeepAsync()
        {
            try
            {
                var deviceEnumerator = new MMDeviceEnumerator();
                var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

                float originalVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
                bool originalMute = device.AudioEndpointVolume.Mute;

                device.AudioEndpointVolume.Mute = false;
                device.AudioEndpointVolume.MasterVolumeLevelScalar = 0.5f;

                for (int i = 0; i < 3; i++)
                {
                    Console.Beep(800 + (i * 200), 200);
                    await Task.Delay(200);
                }

                device.AudioEndpointVolume.Mute = originalMute;
                device.AudioEndpointVolume.MasterVolumeLevelScalar = originalVolume;
            }
            catch (Exception ex)
            {
                throw new Exception("ビープ音に失敗しました: " + ex.Message);
            }
        }
    }
}
