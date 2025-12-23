using System;
using System.Runtime.InteropServices;

namespace LockLibrary
{
    public static class LockManager
    {
        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        public static void LockScreen()
        {
            try
            {
                LockWorkStation();
            }
            catch (Exception ex)
            {
                SystemHelper.Log($"Lock Error: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            SystemHelper.RunCommand("shutdown", "/s /f /t 0");
        }

        public static void Restart()
        {
            SystemHelper.RunCommand("shutdown", "/r /f /t 0");
        }
    }
}
