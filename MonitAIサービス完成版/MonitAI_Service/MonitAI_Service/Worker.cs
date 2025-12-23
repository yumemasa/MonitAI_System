using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;

namespace MonitAI_Service
{
    public class Worker : ServiceBase
    {
        private Thread workerThread;
        private bool stopping = false;

        // 監視ロジック
        private MonitorLogic monitorLogic;

        // 直前の監視状態（無駄な Start/Stop 防止）
        private bool wasMonitoringTime = false;

        protected override void OnStart(string[] args)
        {
            ConfigureAutoRestart();

            // 既存ロジック（そのまま）
            Launcher.EnsureRunning();
            WindowsTimerRegistrar.EnsureRegistered();

            EventLog.WriteEntry(
                "MonitAI_Service",
                "Service Started",
                EventLogEntryType.Information
            );

            monitorLogic = new MonitorLogic();

            workerThread = new Thread(ServiceLoop)
            {
                IsBackground = true
            };
            workerThread.Start();
        }

        private void ServiceLoop()
        {
            while (!stopping)
            {
                try
                {
                    bool isMonitoringTime = monitorLogic.IsMonitoringTime();

                    // ===== 監視時間に入った瞬間 =====
                    if (isMonitoringTime && !wasMonitoringTime)
                    {
                        EventLog.WriteEntry(
                            "MonitAI_Service",
                            "Monitoring time entered. App monitoring ON / WindowsTimer START",
                            EventLogEntryType.Information
                        );

                        WindowsTimerController.StartTimer();
                    }

                    // ===== 監視時間を抜けた瞬間 =====
                    if (!isMonitoringTime && wasMonitoringTime)
                    {
                        EventLog.WriteEntry(
                            "MonitAI_Service",
                            "Monitoring time exited. App monitoring OFF / WindowsTimer STOP",
                            EventLogEntryType.Information
                        );

                        WindowsTimerController.StopTimer();
                    }

                    // ===== 監視時間内だけアプリ監視 =====
                    if (isMonitoringTime)
                    {
                        monitorLogic.CheckAndRecoverApp();
                    }

                    wasMonitoringTime = isMonitoringTime;
                }
                catch (Exception ex)
                {
                    EventLog.WriteEntry(
                        "MonitAI_Service",
                        $"Service loop error: {ex.Message}",
                        EventLogEntryType.Error
                    );
                }

                Thread.Sleep(3000);
            }
        }

        protected override void OnStop()
        {
            // 念のため停止時は必ず止める
            WindowsTimerController.StopTimer();

            EventLog.WriteEntry(
                "MonitAI_Service",
                "Service stopping",
                EventLogEntryType.Warning
            );

            stopping = true;
            workerThread?.Join();

            // 既存挙動維持
            Environment.Exit(1);
        }

        private void ConfigureAutoRestart()
        {
            try
            {
                string serviceName = "MonitAI_Service";

                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"failure \"{serviceName}\" reset=0 actions=restart/1000/restart/1000/restart/1000",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch
            {
                // 無視（元の設計どおり）
            }
        }
    }
}
