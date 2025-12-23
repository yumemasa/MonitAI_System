using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace LockLibrary
{
    public static class SystemControlManager
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

        private const uint EWX_LOGOFF = 0x00000000;
        private const uint EWX_SHUTDOWN = 0x00000001;
        private const uint EWX_REBOOT = 0x00000002;
        private const uint EWX_FORCE = 0x00000004;
        private const uint EWX_POWEROFF = 0x00000008;

        /// <summary>
        /// MonitAI アプリ自体を終了する
        /// </summary>
        public static void ExitApplication()
        {
            try
            {
                HookManager.StopHooks();
                NotifyHelper.Show("終了", "MonitAI システムを終了します");
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show("終了中にエラー: " + ex.Message);
            }
        }

        /// <summary>
        /// Windowsをシャットダウン
        /// </summary>
        public static void Shutdown()
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("シャットダウンに失敗しました: " + ex.Message);
            }
        }

        /// <summary>
        /// Windowsを再起動
        /// </summary>
        public static void Restart()
        {
            try
            {
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("再起動に失敗しました: " + ex.Message);
            }
        }

        /// <summary>
        /// 現在のユーザーをログオフ
        /// </summary>
        public static void LogOff()
        {
            try
            {
                ExitWindowsEx(EWX_LOGOFF | EWX_FORCE, 0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("ログオフに失敗しました: " + ex.Message);
            }
        }
    }
}
