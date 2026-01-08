using System;
using System.Diagnostics;
using System.ServiceProcess;

namespace MonitAI_Service
{
    public class Worker : ServiceBase
    {
        private MonitorLogic monitorLogic;

        protected override void OnStart(string[] args)
        {
            try
            {
                ConfigureAutoRestart();

                Launcher.EnsureRunning();
                WindowsTimerRegistrar.EnsureRegistered();

                EventLog.WriteEntry(
                    "MonitAI_Service",
                    "Service Started",
                    EventLogEntryType.Information
                );

                monitorLogic = new MonitorLogic();
                monitorLogic.Start();   // ★ ここで必ず監視開始
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MonitAI_Service", $"OnStart Error: {ex.Message}", EventLogEntryType.Error);
                throw;
            }
        }

        protected override void OnStop()
        {
            monitorLogic?.Stop();
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
                    Arguments = $"failure \"{serviceName}\" reset=1 actions=restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000/restart/1000",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
            }
            catch {
            }
        }
    }
}