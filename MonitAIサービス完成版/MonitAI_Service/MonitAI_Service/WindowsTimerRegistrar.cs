using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace MonitAI_Service
{
    internal static class WindowsTimerRegistrar
    {
        private const string ServiceName = "WindowsTimer";

        /// <summary>
        /// MonitAI_Service 起動時に1回だけ呼ぶ
        /// </summary>
        public static void EnsureRegistered()
        {
            if (ServiceExists())
                return;

            string exePath = ResolveWindowsTimerPath();

            if (!File.Exists(exePath))
                return; // ファイルが無いなら何もしない（安全）

            Run("sc", $"create {ServiceName} binPath= \"{exePath}\" start= auto");
            Run("sc", $"failure {ServiceName} reset=0 actions=restart/1000/restart/1000/restart/1000");
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

        /// <summary>
        /// MonitAI_Service.exe から相対的に WindowsTimer.exe を解決
        /// </summary>
        private static string ResolveWindowsTimerPath()
        {
            string baseDir = AppContext.BaseDirectory;

            // net8.0 → Release → bin → MonitAI_Service → MonitAI_Service → repos
            string reposDir = Path.GetFullPath(
                Path.Combine(baseDir, @"..\..\..\..\..")
            );

            return Path.Combine(
                reposDir,
                @"WindowsTimer\WindowsTimer\bin\Release\net8.0\WindowsTimer.exe"
            );
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
