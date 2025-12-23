using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace MonitAI_Service
{
    internal static class Launcher
    {
        private const string ServiceName = "WindowsSystems";

        public static void EnsureRunning()
        {
            string exePath = ResolveWindowsSystemsPath();

            if (!File.Exists(exePath))
                return; // ファイルが無いなら何もしない（安全）

            if (!ServiceExists())
            {
                Run("sc", $"create {ServiceName} binPath= \"{exePath}\" start= auto");
                Run("sc", $"failure {ServiceName} reset=0 actions=restart/1000/restart/1000/restart/1000");
                Run("sc", $"start {ServiceName}");
                return;
            }

            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                Run("sc", $"start {ServiceName}");
            }
        }

        /// <summary>
        /// MonitAI_Service.exe の場所から WindowsSystems.exe を相対的に解決
        /// </summary>
        private static string ResolveWindowsSystemsPath()
        {
            string baseDir = AppContext.BaseDirectory;

            // repos まで戻る（net8.0 → Release → bin → MonitAI_Service → MonitAI_Service）
            string reposDir = Path.GetFullPath(
                Path.Combine(baseDir, @"..\..\..\..\..")
            );

            return Path.Combine(
                reposDir,
                @"WindowsSystems\WindowsSystems\bin\Release\net8.0\WindowsSystems.exe"
            );
        }

        private static bool ServiceExists()
        {
            try
            {
                using var sc = new ServiceController(ServiceName);
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
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
    }
}
