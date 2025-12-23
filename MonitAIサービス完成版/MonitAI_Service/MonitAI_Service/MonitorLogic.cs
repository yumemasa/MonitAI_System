using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace MonitAI_Service
{
    public class MonitorLogic
    {
        // ==============================
        // 設定ファイル
        // ==============================
        private readonly string _configPath =
            Path.Combine(AppContext.BaseDirectory, "C:\\Users\\it222187\\Desktop\\settings.json");

        private DateTime _lastConfigWriteTime = DateTime.MinValue;

        // ==============================
        // 監視対象
        // ==============================
        private string _processName = "MonitAI_App";
        private string _processPath = @"C:\path\to\MonitAI_App.exe";

        // ==============================
        // 監視日付
        // ==============================
        private DateTime? _startDate;
        private DateTime? _endDate;

        // ==============================
        // 監視時間
        // ==============================
        private TimeSpan _startTime = TimeSpan.Zero;
        private TimeSpan _endTime = TimeSpan.Zero;

        public MonitorLogic()
        {
            ReloadConfigIfNeeded();
        }

        /// <summary>
        /// ServiceLoop から呼ばれる監視処理
        /// </summary>
        public void CheckAndRecoverApp()
        {
            ReloadConfigIfNeeded();

            if (!IsMonitoringTime())
                return;

            try
            {
                var processes = Process.GetProcessesByName(_processName);
                if (processes.Length == 0)
                {
                    Process.Start(_processPath);

                    EventLog.WriteEntry(
                        "MonitAI_Service",
                        $"{_processName} was not running. Restarted.",
                        EventLogEntryType.Warning
                    );
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry(
                    "MonitAI_Service",
                    $"Monitor error: {ex.Message}",
                    EventLogEntryType.Error
                );
            }
        }

        // ==============================
        // 外部公開：監視中か？
        // ==============================
        public bool IsMonitoringTime()
        {
            ReloadConfigIfNeeded();

            // 日付チェック
            if (_startDate.HasValue && _endDate.HasValue)
            {
                var today = DateTime.Today;
                if (today < _startDate.Value || today > _endDate.Value)
                    return false;
            }

            if (_startTime == TimeSpan.Zero && _endTime == TimeSpan.Zero)
                return false;

            var now = DateTime.Now.TimeOfDay;

            // 通常
            if (_startTime < _endTime)
                return now >= _startTime && now <= _endTime;

            // 日跨ぎ
            return now >= _startTime || now <= _endTime;
        }

        // ==============================
        // 設定ファイル再読込
        // ==============================
        private void ReloadConfigIfNeeded()
        {
            if (!File.Exists(_configPath))
                return;

            DateTime writeTime = File.GetLastWriteTimeUtc(_configPath);
            if (writeTime <= _lastConfigWriteTime)
                return;

            _lastConfigWriteTime = writeTime;
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                string json = File.ReadAllText(_configPath);
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement.GetProperty("Monitoring");

                _startTime = TimeSpan.Parse(root.GetProperty("StartTime").GetString());
                _endTime = TimeSpan.Parse(root.GetProperty("EndTime").GetString());

                if (root.TryGetProperty("StartDate", out var sd))
                    _startDate = DateTime.Parse(sd.GetString());

                if (root.TryGetProperty("EndDate", out var ed))
                    _endDate = DateTime.Parse(ed.GetString());

                if (root.TryGetProperty("ProcessName", out var name))
                    _processName = name.GetString();

                if (root.TryGetProperty("ProcessPath", out var path))
                    _processPath = path.GetString();
            }
            catch
            {
                _startDate = null;
                _endDate = null;
                _startTime = TimeSpan.Zero;
                _endTime = TimeSpan.Zero;
            }
        }
    }
}
