using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Security.Principal;

namespace MonitAI_Service
{
    public class MonitorLogic
    {
        private CancellationTokenSource _cts;
        private Task _monitorTask;

        // ==============================
        // 設定ファイル
        // ==============================
        // 動的に取得するため、readonlyフィールドではなくプロパティまたはメソッドで解決する
        private string GetConfigPath()
        {
            // アクティブユーザーとドメインを取得
            if (!TryGetActiveSessionUser(out string userName, out string domainName))
                return string.Empty;

            try
            {
                // SIDを取得してプロファイルパスを解決する
                string accountName = string.IsNullOrEmpty(domainName) ? userName : $"{domainName}\\{userName}";
                var account = new NTAccount(accountName);
                var sid = account.Translate(typeof(SecurityIdentifier)).Value;

                string profilePath = null;
                using (var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}"))
                {
                    if (key != null)
                    {
                        profilePath = key.GetValue("ProfileImagePath") as string;
                    }
                }

                if (!string.IsNullOrEmpty(profilePath))
                {
                    return Path.Combine(profilePath, @"AppData\Roaming\screenShot2\config.json");
                }
            }
            catch (Exception ex)
            {
                // SID解決失敗時などはログを出してフォールバックへ
                EventLog.WriteEntry("MonitAI_Service", $"Failed to resolve profile path via SID: {ex.Message}", EventLogEntryType.Warning);
            }

            // フォールバック: 簡易的に C:\Users\<User>\AppData\Roaming\... を構築
            return $@"C:\Users\{userName}\AppData\Roaming\screenShot2\config.json";
        }
        
        private DateTime _lastConfigWriteTime = DateTime.MinValue;

        // ==============================
        // 監視対象
        // ==============================
        private string _processName = "MonitAI.Agent";
        // デフォルトパス (設定ファイルにない場合のフォールバック)
        private string _processPath = string.Empty;
        
        // タスク作成確認のキャッシュ（毎回PowerShellを叩くと重いため）
        private string _lastVerifiedTaskPath = string.Empty;

        // ★Exe削除防止用のロック変数
        private FileStream _lockedFileStream;
        private string _lockedFilePath;

        // ==============================
        // 監視時間 (日付含む)
        // ==============================
        private DateTime? _startTime;
        private DateTime? _endTime;

        public MonitorLogic()
        {
            // ★証明用ログ：コード変更が反映されているか確認
            try { EventLog.WriteEntry("MonitAI_Service", "★uuuuiiiiiiiiiiuuuu★", EventLogEntryType.Information); 
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
                        await Task.Delay(1000, _cts.Token);
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
                _monitorTask?.Wait(1500);
            }
            catch { }
            UnlockAgent();
        }

        private void LockAgent(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            // 既に同じファイルをロック済みなら何もしない
            if (_lockedFileStream != null && _lockedFilePath == path) return;

            // 違うファイル、またはロックしていないなら、一旦解除して再取得
            UnlockAgent();

            try
            {
                if (File.Exists(path))
                {
                    // FileShare.Read により、他プロセスからの書き込み・削除を禁止する
                    // Service自身は読むだけなので FileAccess.Read
                    _lockedFileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _lockedFilePath = path;
                    EventLog.WriteEntry("MonitAI_Service", $"Agent Locked (Protected from deletion): {path}", EventLogEntryType.Information);
                }
            }
            catch (Exception ex)
            {
                // ロック失敗（ファイル使用中などではないはずだが、権限等でエラーの場合）
                EventLog.WriteEntry("MonitAI_Service", $"Failed to lock Agent: {ex.Message}", EventLogEntryType.Warning);
            }
        }

        private void UnlockAgent()
        {
            if (_lockedFileStream != null)
            {
                try
                {
                    _lockedFileStream.Dispose();
                }
                catch { }
                _lockedFileStream = null;
                _lockedFilePath = null;
            }
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
                UnlockAgent(); // 監視時間外はロック解除
                KillAgentRepeatedly();
                return;
            }

            // ===== 監視時間内 =====

            // パス解決（未設定の場合）
            if (string.IsNullOrEmpty(_processPath))
            {
                string serviceDir = AppDomain.CurrentDomain.BaseDirectory;
                string sameDirCandidate = Path.Combine(serviceDir, "MonitAI.Agent.exe");
                if (File.Exists(sameDirCandidate))
                {
                    _processPath = sameDirCandidate;
                }
            }

            // エージェントExeをロックして削除を防止
            LockAgent(_processPath);

            try
            {
                var processes = Process.GetProcessesByName(_processName);
                if (processes.Length == 0)
                {
                    // プロセスパスが空なら何もしない（またはログ出力）
                    if (string.IsNullOrEmpty(_processPath))
                    {
                        EventLog.WriteEntry("MonitAI_Service", $"AgentPath is not set in config and not found in service directory.", EventLogEntryType.Warning);
                        return;
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
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    string error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();

                    if (proc.ExitCode == 0)
                    {
                        EventLog.WriteEntry(
                            "MonitAI_Service",
                            $"{_processName} was not running. Triggered scheduled task '{AgentTaskName}' to restart.\nPath: {_processPath}\nOutput: {output}",
                            EventLogEntryType.Information
                        );
                    }
                    else
                    {
                        EventLog.WriteEntry(
                            "MonitAI_Service",
                            $"Failed to run scheduled task '{AgentTaskName}'. ExitCode: {proc.ExitCode}\nOutput: {output}\nError: {error}",
                            EventLogEntryType.Error
                        );
                        // 失敗した場合、次回は必ずタスク定義を確認するようにキャッシュをクリア
                        _lastVerifiedTaskPath = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MonitAI_Service", $"Failed to run scheduled task: {ex.Message}", EventLogEntryType.Error);
                // 例外時も念のためキャッシュクリア
                _lastVerifiedTaskPath = string.Empty;
            }
        }

        private void EnsureAgentTaskExists()
        {
            // キャッシュチェック: パスが変わっていなければ何もしない（高速化）
            if (!string.IsNullOrEmpty(_processPath) && _processPath == _lastVerifiedTaskPath)
            {
                return;
            }

            try
            {
                string currentUser = GetActiveUserForTask(); // ドメイン対応版
                if (string.IsNullOrEmpty(currentUser)) return; // ユーザー特定できなければスキップ

                string workDir = Path.GetDirectoryName(_processPath);
                if (string.IsNullOrEmpty(workDir)) workDir = "C:\\";

                // PowerShellスクリプト: 作業ディレクトリ付きでタスクを作成する
                // 既にタスクが存在していても、パスが異なれば更新するロジックを追加
                string psScript = $@"
$tn = '{AgentTaskName}';
$user = '{currentUser}';
$path = '{_processPath}';
$dir = '{workDir}';

$needRegister = $true;
# 既にタスクがあるか確認
$exists = Get-ScheduledTask -TaskName $tn -ErrorAction SilentlyContinue;
if ($exists) {{
    # アクションのパスを確認
    $act = $exists.Actions[0];
    if ($act.Execute -eq $path -and $act.WorkingDirectory -eq $dir) {{
         $needRegister = $false;
    }}
}}

if ($needRegister) {{
    # アクション: 実行ファイルと作業フォルダを設定
    $action = New-ScheduledTaskAction -Execute $path -WorkingDirectory $dir;

    # プリンシパル: 該当ユーザーの対話セッションで実行 (GUIアプリはInteractive必須)
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited;

    # 設定: 実行時間制限なし(0)、電源条件無視
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Seconds 0);

    # 登録 (強制上書き -Force)
    Register-ScheduledTask -TaskName $tn -Action $action -Principal $principal -Settings $settings -Force;
}}
";

                // Base64エンコードして実行
                string encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(psScript));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (proc.ExitCode != 0)
                {
                    EventLog.WriteEntry("MonitAI_Service", $"PowerShell task creation failed. ExitCode: {proc.ExitCode}\nError: {err}", EventLogEntryType.Warning);
                    _lastVerifiedTaskPath = string.Empty; // 失敗時はキャッシュしない
                }
                else
                {
                    // 成功時、このパスでタスク確認済みとする
                    _lastVerifiedTaskPath = _processPath;
                }
            }
            catch (Exception ex)
            {
                EventLog.WriteEntry("MonitAI_Service", $"Exception creating task via PowerShell: {ex.Message}", EventLogEntryType.Error);
                _lastVerifiedTaskPath = string.Empty;
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
            string configPath = GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                // ファイルがない場合、1回だけログを出す（スパム防止）
                if (_lastConfigWriteTime != DateTime.MinValue) return;
                
                EventLog.WriteEntry(
                    "MonitAI_Service",
                    $"Config file not found or user not detected. Path: {configPath}",
                    EventLogEntryType.Warning
                );
                _lastConfigWriteTime = DateTime.MinValue.AddSeconds(1); // ログ出力済みフラグ代わり
                return;
            }

            DateTime writeTime = File.GetLastWriteTimeUtc(configPath);
            if (writeTime <= _lastConfigWriteTime)
                return;

            _lastConfigWriteTime = writeTime;
            LoadConfig(configPath);
        }

        private void LoadConfig(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
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

        // =============================================================
        // Helper: Active Console User Logic
        // =============================================================
        // WTS (Windows Terminal Services) APIを使って
        // 現在物理コンソール(ID:1など)を使っているユーザー名を特定する
        
        [System.Runtime.InteropServices.DllImport("wtsapi32.dll", SetLastError = true)]
        private static extern bool WTSQuerySessionInformation(
            IntPtr hServer, 
            int sessionId, 
            WTS_INFO_CLASS wtsInfoClass, 
            out IntPtr ppBuffer, 
            out int pBytesReturned);

        [System.Runtime.InteropServices.DllImport("wtsapi32.dll")]
        private static extern void WTSFreeMemory(IntPtr pMemory);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern int WTSGetActiveConsoleSessionId();

        private enum WTS_INFO_CLASS
        {
            WTSInitialProgram,
            WTSApplicationName,
            WTSWorkingDirectory,
            WTSOEMId,
            WTSSessionId,
            WTSUserName,
            WTSWinStationName,
            WTSDomainName,
            WTSConnectState,
            WTSClientBuildNumber,
            WTSClientName,
            WTSClientDirectory,
            WTSClientProductId,
            WTSClientHardwareId,
            WTSClientAddress,
            WTSClientDisplay,
            WTSClientProtocolType,
            WTSIdleTime,
            WTSLogonTime,
            WTSIncomingBytes,
            WTSOutgoingBytes,
            WTSIncomingFrames,
            WTSOutgoingFrames,
            WTSClientInfo,
            WTSSessionInfo
        }

        private string GetActiveConsoleUserName()
        {
            if (TryGetActiveSessionUser(out string user, out _))
                return user;
            return null;
        }

        private string GetActiveUserForTask()
        {
            if (TryGetActiveSessionUser(out string user, out string domain))
            {
                if (!string.IsNullOrEmpty(domain)) return $"{domain}\\{user}";
                return user;
            }
            return null;
        }

        private bool TryGetActiveSessionUser(out string user, out string domain)
        {
            user = null;
            domain = null;
            try
            {
                int sessionId = WTSGetActiveConsoleSessionId();
                if (sessionId == -1) return false; // セッションなし

                if (QueryWTS(sessionId, WTS_INFO_CLASS.WTSUserName, out user))
                {
                    QueryWTS(sessionId, WTS_INFO_CLASS.WTSDomainName, out domain);
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private bool QueryWTS(int sessionId, WTS_INFO_CLASS infoClass, out string result)
        {
            result = null;
            IntPtr buffer = IntPtr.Zero;
            int bytesReturned;
            try
            {
                if (WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out buffer, out bytesReturned) && bytesReturned > 1)
                {
                    result = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(buffer);
                    return true;
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
            }
            return false;
        }
    }
}