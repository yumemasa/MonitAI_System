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
                EnsureServiceRunning(); // ★追加
                await Task.Delay(1000, stoppingToken); // 1秒監視
            }
        }

        private void EnsureServiceExists()
        {
            if (ServiceExists(TargetServiceName))
                return; // 存在する（停止中でもOK）

            string exePath = ResolveMonitAIServicePath();
            if (!File.Exists(exePath))
                return; // 実体が無ければ何もしない

            // --- 削除されていた場合のみ復旧 ---
            Run("sc", $"create {TargetServiceName} binPath= \"{exePath}\" start= auto");
            Run("sc", $"failure {TargetServiceName} reset=0 actions=restart/1000/restart/1000/restart/1000");
            Run("reg", @"add HKLM\SYSTEM\CurrentControlSet\Services\MonitAI_Service /v AllowStop /t REG_DWORD /d 0 /f");
            Run("sc", $"start {TargetServiceName}");
        }

        // ==============================
        // ★ 追加：停止していたら再起動
        // ==============================
        private void EnsureServiceRunning()
        {
            try
            {
                using var sc = new ServiceController(TargetServiceName);

                if (sc.Status == ServiceControllerStatus.Stopped ||
                    sc.Status == ServiceControllerStatus.StopPending)
                {
                    sc.Start();
                }
            }
            catch
            {
                // サービス削除中などは無視（次ループで復旧される）
            }
        }

        /// <summary>
        /// WindowsSystems.exe の場所から MonitAI_Service.exe を相対解決
        /// </summary>
        private static string ResolveMonitAIServicePath()
        {
            string baseDir = AppContext.BaseDirectory;

            // repos まで戻る
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