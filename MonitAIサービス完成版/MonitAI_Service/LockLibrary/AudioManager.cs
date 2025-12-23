using System;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace LockLibrary
{
    public static class AudioManager
    {
        public static async Task PlayBeepAsync()
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            float originalVolume = device.AudioEndpointVolume.MasterVolumeLevelScalar;
            bool originalMute = device.AudioEndpointVolume.Mute;

            try
            {
                // 音量を一時的に50%に設定
                device.AudioEndpointVolume.Mute = false;
                device.AudioEndpointVolume.MasterVolumeLevelScalar = 0.5f;

                // 🔔 3回ビープ音
                int[] freqs = { 800, 1000, 1200 };
                int[] durations = { 300, 300, 400 };

                for (int i = 0; i < freqs.Length; i++)
                {
                    Console.Beep(freqs[i], durations[i]);
                    await Task.Delay(200); // 少し間隔を空ける
                }
            }
            catch (Exception ex)
            {
                // UI依存なしでログ出力（サービス用）
                Console.WriteLine("[AudioManager] ビープ音再生エラー: " + ex.Message);
            }
            finally
            {
                // 元の音量・ミュート状態に戻す
                try
                {
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = originalVolume;
                    device.AudioEndpointVolume.Mute = originalMute;
                }
                catch
                {
                    // 復元失敗は無視
                }
            }
        }
    }
}
