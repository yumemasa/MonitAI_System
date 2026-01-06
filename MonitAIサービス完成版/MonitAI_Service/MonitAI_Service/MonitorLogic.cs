using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MonitAI_Service
{
    public class MonitorLogic
    {
        private CancellationTokenSource _cts;
        private Task _monitorTask;

        // ==============================
        // 設定ファイル
        // ==============================
        // ユーザーのAppDataにある config.json を参照する
        // ※注意: サービスは「ユーザーアカウント」で実行する必要があります (LocalSystemでは参照不可)
        private readonly string _configPath = @"C:\Users\it222104\AppData\Roaming\screenShot2\config.json";
        private DateTime _lastConfigWriteTime = DateTime.MinValue;

        // ==============================
        // 監視対象
        // ==============================
        private string _processName = "MonitAI.Agent";
        // デフォルトパス (設定ファイルにない場合のフォールバック)
        private string _processPath = string.Empty;

        // ==============================
        // 監視時間 (日付含む)
        // ==============================
        private DateTime? _startTime;
        private DateTime? _endTime;

        public MonitorLogic()
        {
            // ★証明用ログ：コード変更が反映されているか確認
            try { EventLog.WriteEntry("MonitAI_Service", "★証明用ログ：このメッセージが出たら新しいコードが動いています★", EventLogEntryType.Information); 
            } catch 
            {
                
            }
            try
            {
                if (!EventLog.SourceExists("MonitAI.Agent"))
                {
                    EventLog.CreateEventSource("MonitAI.Agent", "Application");
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MonitAI_Service", $"Failed to create event source for Agent: {ex.Message}", EventLogEntryType.Warning);
            }

            ReloadConfigIfNeeded();
        }

        // ★ ユーザー権限で Agent を起動するタスク名
        private const string AgentTaskName = "MonitAI_Agent_Launch";

        public void Start()
        {
            if (_monitorTask != null && !_monitorTask.IsCompleted) return;

            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    CheckAndRecoverApp();
                    try
                    {
                        await Task.Delay(5000, _cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
            });
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _monitorTask?.Wait(2000);
            }
            catch { }
        }

        /// <summary>
        /// ServiceLoop から呼ばれる監視処理
        /// </summary>
        public void CheckAndRecoverApp()
        {
            ReloadConfigIfNeeded();

            bool nowMonitoring = IsMonitoringTime();

            // ===== 監視時間外 =====
            if (!nowMonitoring)
            {
                KillAgentRepeatedly();
                return;
            }

            // ===== 監視時間内 =====
            try
            {
                var processes = Process.GetProcessesByName(_processName);
                if (processes.Length == 0)
                {
                    // プロセスパスが空なら何もしない（またはログ出力）
                    if (string.IsNullOrEmpty(_processPath))
                    {
                        // フォールバック: デフォルトパスを試す
                        string defaultPath = @"C:\Users\it222104\source\repos\MonitAI_System\MonitAI.Agent\bin\Release\net8.0-windows\MonitAI.Agent.exe";
                        if (File.Exists(defaultPath))
                        {
                            _processPath = defaultPath;
                        }
                        else
                        {
                            EventLog.WriteEntry("MonitAI_Service", "AgentPath is empty and default path not found. Cannot restart.", EventLogEntryType.Warning);
                            return;
                        }
                    }

                    // ★修正: タスクスケジューラを使用してユーザーセッションで起動する
                    // 友人のコードを参考に、schtasks を利用する方式に変更
                    StartAgentAsUser();
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

        private void StartAgentAsUser()
        {
            EnsureAgentTaskExists();

            try
            {
                // タスクを実行
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/run /tn \"{AgentTaskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
                
                EventLog.WriteEntry(
                    "MonitAI_Service",
                    $"{_processName} was not running. Triggered scheduled task '{AgentTaskName}' to restart.",
                    EventLogEntryType.Information
                );
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MonitAI_Service", $"Failed to run scheduled task: {ex.Message}", EventLogEntryType.Error);
            }
        }

        private void EnsureAgentTaskExists()
        {
            try
            {
                // タスクが存在するか確認
                var check = Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/query /tn \"{AgentTaskName}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });

                check.WaitForExit();
                if (check.ExitCode == 0)
                    return; // 既に存在する
            }
            catch { }

            try
            {
                // タスクを作成
                // サービス(SYSTEM)からユーザー(it222104)のタスクを作成する
                // ※パスワードなしで作成できるかはポリシー依存だが、友人の環境で動作した実績に基づき採用
                
                string currentUser = "it222104"; // サービス実行時は SYSTEM になるため、対象ユーザーを明示

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments =
                        $"/create /tn \"{AgentTaskName}\" " +
                        $"/tr \"\\\"{_processPath}\\\"\" " +
                        "/sc onlogon " +
                        $"/ru \"{currentUser}\" " +
                        "/rl limited " +
                        "/f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                var proc = Process.Start(psi);
                proc.WaitForExit();
                
                if (proc.ExitCode == 0)
                {
                    EventLog.WriteEntry("MonitAI_Service", $"Created scheduled task '{AgentTaskName}' for user '{currentUser}'.", EventLogEntryType.Information);
                }
                else
                {
                    EventLog.WriteEntry("MonitAI_Service", $"Failed to create scheduled task. ExitCode: {proc.ExitCode}", EventLogEntryType.Warning);
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MonitAI_Service", $"Exception creating task: {ex.Message}", EventLogEntryType.Error);
            }
        }

        // ==============================
        // Agent Kill 処理（監視時間外）
        // ・1秒ごと
        // ・最大10回
        // ・Process.Kill を優先
        // ==============================
        private void KillAgentRepeatedly()
        {
            for (int i = 0; i < 10; i++)
            {
                var processes = Process.GetProcessesByName(_processName);

                if (processes.Length == 0)
                    return; // 完全停止確認

                foreach (var proc in processes)
                {
                    try
                    {
                        // .NET から直接 Kill（最優先）
                        proc.Kill(true);
                        proc.WaitForExit(3000);
                    }
                    catch
                    {
                        // Kill できなかった場合の保険
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = "/c taskkill /F /T /PID " + proc.Id,
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            Process.Start(psi);
                        }
                        catch { }
                    }
                }

                Thread.Sleep(1000);
            }
        }

        // ==============================
        // 外部公開：監視中か？
        // ==============================
        public bool IsMonitoringTime()
        {
            ReloadConfigIfNeeded();

            // 設定がない場合は監視しない
            if (_startTime == null || _endTime == null)
                return false;

            var now = DateTime.Now;

            // 日付を含めて比較
            return now >= _startTime.Value && now <= _endTime.Value;
        }

        // ==============================
        // 設定ファイル再読込
        // ==============================
        private void ReloadConfigIfNeeded()
        {
            if (!File.Exists(_configPath))
            {
                // ファイルがない場合、1回だけログを出す（スパム防止）
                if (_lastConfigWriteTime != DateTime.MinValue) return;
                
                EventLog.WriteEntry(
                    "MonitAI_Service",
                    $"Config file not found at: {_configPath}",
                    EventLogEntryType.Warning
                );
                _lastConfigWriteTime = DateTime.MinValue.AddSeconds(1); // ログ出力済みフラグ代わり
                return;
            }

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
                var root = doc.RootElement;

                // StartTime / EndTime (ISO 8601 format)
                if (root.TryGetProperty("StartTime", out var stProp) &&
                    root.TryGetProperty("EndTime", out var etProp))
                {
                    if (DateTime.TryParse(stProp.GetString(), null, DateTimeStyles.RoundtripKind, out var st) &&
                        DateTime.TryParse(etProp.GetString(), null, DateTimeStyles.RoundtripKind, out var et))
                    {
                        _startTime = st;
                        _endTime = et;
                    }
                }

                // ProcessPath (Agentのパスを推測または設定から取得したいが、config.jsonには含まれていない可能性がある)
                // ここでは、config.jsonと同じ階層や既知の場所から探すロジックを入れるか、
                // あるいはAgent側でパスも書き込むように修正が必要かもしれない。
                // いったん、ハードコードされていたパスロジックを「動的探索」に置き換える。

                // 簡易的な探索: 現在のユーザーのAppDataから推測するのは難しいので、
                // 実行中のプロセスからパスを取るか、あるいは固定のインストールパスを想定するしかない。
                // 今回は「友人のPC環境に依存しない」ため、相対パス等は使えない（Serviceは別場所にある）。
                // ★解決策: Agentが起動したときに、自身のパスを config.json に書き込むのがベストだが、
                // 今は SetupPage.xaml.cs で書き込んでいない。
                // 暫定的に、config.json に "AgentPath" があればそれを使い、なければ空にする。

                if (root.TryGetProperty("AgentPath", out var pathProp))
                {
                    _processPath = pathProp.GetString() ?? string.Empty;
                }

                EventLog.WriteEntry(
                    "MonitAI_Service",
                    $"Config Loaded Successfully.\nStart: {_startTime}\nEnd: {_endTime}\nPath: {_processPath}",
                    EventLogEntryType.Information
                );
            }
            catch (Exception ex)
            {
                _startTime = null;
                _endTime = null;
                EventLog.WriteEntry(
                    "MonitAI_Service",
                    $"Failed to load config: {ex.Message}",
                    EventLogEntryType.Error
                );
            }
        }
    }
}