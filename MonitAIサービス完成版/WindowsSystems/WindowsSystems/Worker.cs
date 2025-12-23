using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsSystems
{
    public class Worker : BackgroundService
    {
        private const string TargetServiceName = "MonitAI_Service";

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                EnsureServiceExists();
                await Task.Delay(1000, stoppingToken); // 1ïbäƒéã
            }
        }

        private void EnsureServiceExists()
        {
            if (ServiceExists(TargetServiceName))
                return; // ë∂ç›Ç∑ÇÈÅií‚é~íÜÇ≈Ç‡OKÅj

            string exePath = ResolveMonitAIServicePath();
            if (!File.Exists(exePath))
                return; // é¿ëÃÇ™ñ≥ÇØÇÍÇŒâΩÇ‡ÇµÇ»Ç¢

            // --- çÌèúÇ≥ÇÍÇƒÇ¢ÇΩèÍçáÇÃÇ›ïúãå ---
            Run("sc", $"create {TargetServiceName} binPath= \"{exePath}\" start= auto");
            Run("sc", $"failure {TargetServiceName} reset=0 actions=restart/1000/restart/1000/restart/1000");
            Run("reg", @"add HKLM\SYSTEM\CurrentControlSet\Services\MonitAI_Service /v AllowStop /t REG_DWORD /d 0 /f");
            Run("sc", $"start {TargetServiceName}");
        }

        /// <summary>
        /// WindowsSystems.exe ÇÃèÍèäÇ©ÇÁ MonitAI_Service.exe ÇëäëŒâåà
        /// </summary>
        private static string ResolveMonitAIServicePath()
        {
            string baseDir = AppContext.BaseDirectory;

            // repos Ç‹Ç≈ñﬂÇÈ
            string reposDir = Path.GetFullPath(
                Path.Combine(baseDir, @"..\..\..\..\..")
            );

            return Path.Combine(
                reposDir,
                @"MonitAI_Service\MonitAI_Service\bin\Release\net8.0\MonitAI_Service.exe"
            );
        }

        private static bool ServiceExists(string serviceName)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                _ = sc.Status;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void Run(string file, string args)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
    }
}
