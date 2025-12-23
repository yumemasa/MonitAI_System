using System;
using System.Diagnostics;

namespace LockLibrary
{
    public static class SystemHelper
    {
        public static void RunCommand(string command, string args = "")
        {
            try
            {
                Process.Start(command, args);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemHelper] コマンド実行エラー: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}
