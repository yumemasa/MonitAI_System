using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace MonitAI_Service
{
    internal static class WindowsTimerController
    {
        private const string ServiceName = "WindowsTimer";

        /// <summary>
        /// MonitAI_Service 起動時に呼ぶ
        /// </summary>
        public static void StartTimer()
        {
            try
            {
                using var sc = new ServiceController(ServiceName);

                if (sc.Status == ServiceControllerStatus.Stopped ||
                    sc.Status == ServiceControllerStatus.StopPending)
                {
                    sc.Start();
                }
            }
            catch
            {
                // WindowsTimer が存在しない・未登録なら何もしない
            }
        }

        /// <summary>
        /// MonitAI_Service 停止時に呼ぶ
        /// </summary>
        public static void StopTimer()
        {
            try
            {
                using var sc = new ServiceController(ServiceName);

                if (sc.Status == ServiceControllerStatus.Running ||
                    sc.Status == ServiceControllerStatus.StartPending)
                {
                    sc.Stop();
                }
            }
            catch
            {
                // 何もしない（安全）
            }
        }
    }
}
