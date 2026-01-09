using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using MonitAI.Core;
using Forms = System.Windows.Forms;

namespace MonitAI.Agent
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            // コンストラクタの最初でログを出力して、プロセス起動確認を行う
            try
            {
                string tempLog = Path.Combine(Path.GetTempPath(), "monitai_startup.log");
                File.AppendAllText(tempLog, $"[{DateTime.Now}] App Constructor Called. User: {Environment.UserName}\n");
            }
            catch { }
        }

        private ScreenshotService? _screenshotService;
        private GeminiService? _geminiService;
        private InterventionService? _interventionService;

        private bool _isCapturing = false;
        private int _screenshotCount = 0;
        private int _violationPoints = 0;
        private string _saveFolderPath = string.Empty;
        private Random _random = new Random();

        private Forms.NotifyIcon? _notifyIcon;

        // ファイル保護用ストリーム
        private FileStream? _configLockStream;
        private FileStream? _statusLockStream;

        // 設定値
        private string _apiKey = "";
        private string _rules = "";
        private string _cliPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"npm\gemini.cmd");
        private string _acpScriptPath = ""; // ACPモード用スクリプトパス (空ならデフォルト)
        private string _selectedModel = "gemini-2.5-flash-lite"; // デフォルト
        private DateTime? _endTime = null; // 終了時刻
        private bool _useApi = false; // APIモードかどうか

        // ログファイルのパス
        private string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "screenShot2",
            "agent_log.txt");

        private void WriteTempLog(string msg)
        {
            try
            {
                string tempLog = Path.Combine(Path.GetTempPath(), "monitai_startup.log");
                File.AppendAllText(tempLog, $"[{DateTime.Now}] {msg}\n");
            }
            catch { }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            WriteTempLog("OnStartup Begin");
            WriteTempLog($"AppData Path: {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}");

            // グローバル例外ハンドラの設定
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                string msg = $"🔥 Unhandled Exception: {args.ExceptionObject}";
                if (args.ExceptionObject is Exception ex)
                {
                    msg += $"\nStack Trace: {ex.StackTrace}";
                }
                WriteLog(msg);
                WriteTempLog(msg); // Tempログにも書く
                WriteToEventLog(msg, EventLogEntryType.Error);
            };

            Current.DispatcherUnhandledException += (s, args) =>
            {
                string msg = $"🔥 Dispatcher Exception: {args.Exception.Message}\nStack Trace: {args.Exception.StackTrace}";
                WriteLog(msg);
                WriteTempLog(msg); // Tempログにも書く
                WriteToEventLog(msg, EventLogEntryType.Error);
                args.Handled = true; // クラッシュ防止を試みる
            };

            try
            {
                WriteTempLog("Calling base.OnStartup");
                base.OnStartup(e);
                WriteTempLog("base.OnStartup Finished");

                WriteLog("=== Agent Started ===");
                WriteTempLog("Log Initialized");

                LoadSettings(); 
                WriteTempLog("Settings Loaded");

                InitializeServices();
                WriteTempLog("Services Initialized");

                SetupTrayIcon();
                WriteTempLog("Tray Icon Setup");

                // APIモードの場合はAPIキー必須、CLIモードの場合はAPIキー不要（環境変数やgcloud認証を利用想定）
                bool isConfigValid = !string.IsNullOrWhiteSpace(_rules);
                if (_useApi)
                {
                    isConfigValid = isConfigValid && !string.IsNullOrWhiteSpace(_apiKey);
                }

                if (isConfigValid)
                {
                    // StartMonitoring(); // ここでの呼び出しは削除し、InitializeServices 完了後に移動
                    // WriteTempLog("Monitoring Started");
                }
                else
                {
                    string msg = _useApi ? "設定不足: APIキーまたはルールがありません" : "設定不足: ルールが設定されていません";
                    WriteLog(msg);
                    ShowNotification("設定不足", msg);
                }
            }
            catch (Exception ex)
            {
                WriteTempLog($"🔥 Exception in OnStartup: {ex}");
                WriteToEventLog($"Exception in OnStartup: {ex}", EventLogEntryType.Error);
            }
        }

        private void WriteLog(string message)
        {
            try
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
                Debug.WriteLine(logLine);

                string dir = Path.GetDirectoryName(LogPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath, logLine + Environment.NewLine);
            }
            catch { }
        }

        private void WriteToEventLog(string message, EventLogEntryType type)
        {
            try
            {
                // ソースが存在しない場合は書き込めない（一般ユーザー権限では作成不可のため）
                // サービス側で "MonitAI.Agent" ソースを作成しておくことを推奨
                if (EventLog.SourceExists("MonitAI.Agent"))
                {
                    EventLog.WriteEntry("MonitAI.Agent", message, type);
                }
                else
                {
                    // ソースがない場合は Application ログに .NET Runtime として出るのを期待するか、
                    // 既存のソースを借用する（非推奨だがデバッグ用）
                    // EventLog.WriteEntry("Application", "MonitAI.Agent: " + message, type);
                }
            }
            catch { }
        }

        private async void InitializeServices()
        {
            try
            {
                // 初期化開始時は「準備中」とする

                SetupCommandWatcher();
                _screenshotService = new ScreenshotService();

                // モニター情報をログ出力 (移植漏れの補完)
                var screens = Forms.Screen.AllScreens;
                WriteLog($"検出されたモニター: {screens.Length}");

                _saveFolderPath = _screenshotService.DefaultSaveFolderPath ?? Path.Combine(Path.GetTempPath(), "MonitAI_Captures");
                if (!Directory.Exists(_saveFolderPath)) Directory.CreateDirectory(_saveFolderPath);
                WriteLog($"保存先: {_saveFolderPath}");

                // ★修正: ログハンドラを渡して初期化
                _geminiService = new GeminiService((msg) => WriteLog(msg));
                _geminiService.GeminiCliCommand = _cliPath;
                _geminiService.UseGeminiCli = !_useApi; // 設定反映

                WriteLog($"CLIパス: {_cliPath}");
                WriteLog($"使用モデル: {_selectedModel}"); // ログ確認用
                WriteLog($"モード: {(_useApi ? "API" : "CLI")}");

                if (!_useApi)
                {
                    // 常駐プロセス起動 (ACP)
                    WriteLog("🚀 Gemini常駐プロセスを起動しています...");
                    if (!string.IsNullOrEmpty(_acpScriptPath)) WriteLog($"ACPスクリプトパス指定: {_acpScriptPath}");

                    bool started = await _geminiService.StartAsync(_saveFolderPath, scriptPath: _acpScriptPath);
                    if (started)
                    {
                        WriteLog("✅ 常駐プロセス起動成功。待機中。");
                    }
                    else
                    {
                        WriteLog("❌ 常駐プロセス起動失敗。npmパスなどを確認してください。");
                        // 失敗しても従来のCLIモード(One-shot)にフォールバックされるので続行
                    }
                }
                else
                {
                    // APIモードの場合は接続チェック不要（キーがあればOK）
                }

                // 初期化完了（準備OK）

                _interventionService = new InterventionService();
                _interventionService.OnLog += msg => WriteLog($"[介入] {msg}");
                _interventionService.OnNotification += (msg, title) => ShowNotification(title, msg);

                // 初期化完了後に監視を開始する（CLIモードなら起動待ち完了後、APIモードなら即時）
                // ★修正: 初回は待機なしで即時実行する
                StartMonitoring(runImmediately: true);
                WriteTempLog("Monitoring Started (Post-Init)");
            }
            catch (Exception ex)
            {
                WriteLog($"初期化エラー: {ex.Message}");
            }
        }


        private FileSystemWatcher? _commandWatcher;

        private void SetupCommandWatcher()
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);

                _commandWatcher = new FileSystemWatcher(appData, "command.json");
                _commandWatcher.Created += (s, e) => CheckForCommands();
                _commandWatcher.Changed += (s, e) => CheckForCommands();
                _commandWatcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private bool _isProcessingCommand = false;

        private async void CheckForCommands()
        {
            if (_isProcessingCommand) return;
            _isProcessingCommand = true;

            try
            {
                // ファイルロック回避 & デバウンス (短縮)
                await Task.Delay(200);

                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                string commandPath = Path.Combine(appData, "command.json");

                if (File.Exists(commandPath))
                {
                    string json = await File.ReadAllTextAsync(commandPath);
                    
                    // 読み込み直後に削除して二重実行防止
                    try { File.Delete(commandPath); } catch { }

                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Command", out var cmdProp) && cmdProp.GetString() == "AddPoints")
                    {
                        if (doc.RootElement.TryGetProperty("Value", out var valProp))
                        {
                            int points = valProp.GetInt32();
                            
                            // ★重要: フック(SetWindowsHookEx)を機能させるため、必ずUIスレッド(メッセージループを持つスレッド)で実行する
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                _violationPoints += points;
                                if (_violationPoints < 0) _violationPoints = 0; // 0未満にならないようにする

                                WriteLog($"[Command] ポイント操作: {points:+#;-#;0}pt -> 現在:{_violationPoints}pt");
                                
                                // 即座に反映
                                UpdateStatusFile(_violationPoints);
                                string goalSummary = _rules.Split('\n').FirstOrDefault() ?? "目標";
                                _ = _interventionService?.ApplyLevelAsync(_violationPoints, goalSummary);
                                // ShowNotification("デバッグ", $"ポイント追加: +{points}pt");
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"コマンド処理エラー: {ex.Message}");
            }
            finally
            {
                _isProcessingCommand = false;
            }
        }

        private void SetupTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Shield,
                Visible = true,
                Text = "MonitAI Agent"
            };
            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("終了", null, (s, e) => Shutdown());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void StartMonitoring(bool runImmediately = false)
        {
            if (_isCapturing) return;

            _screenshotCount = 0;
            _isCapturing = true;
            WriteLog("🚀 監視・撮影サイクルを開始しました");

            _ = StartCaptureLoop(runImmediately);
        }

        private async Task StartCaptureLoop(bool runImmediately)
        {
            const int cycleMs = 16000;
            bool isFirstRun = true;

            while (_isCapturing)
            {
                try
                {
                    int delay;
                    
                    if (isFirstRun && runImmediately)
                    {
                        // 初回かつ即時実行フラグがある場合は、待機時間を短くする
                        delay = 200; // 少しだけ待つ
                        WriteLog($"初回即時実行: {delay / 1000.0:F1}秒...");
                    }
                    else
                    {
                        delay = _random.Next(1000, cycleMs - 1000);
                        WriteLog($"待機: {delay / 1000.0:F1}秒...");
                    }

                    await Task.Delay(delay);

                    if (!_isCapturing) break;

                    await PerformCaptureAndAnalysis();
                    
                    isFirstRun = false;

                    int remaining = cycleMs - delay;
                    if (remaining > 0)
                    {
                        // 初回即時実行だった場合は、残りのサイクル時間を消化する（連射防止）
                        // ただし初回は少し早めに次の判定に行ってもいいかもしれないので、通常のサイクル維持
                        await Task.Delay(remaining);
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"ループエラー: {ex.Message}");
                    await Task.Delay(5000);
                }
            }
        }

        private async Task PerformCaptureAndAnalysis()
        {
            if (_screenshotService == null || _geminiService == null || _interventionService == null) return;

            var files = _screenshotService.CaptureAllScreens(_saveFolderPath);

            if (files.Count == 0) return;

            _screenshotCount += files.Count;
            WriteLog($"📸 撮影成功 ({files.Count}枚) 合計:{_screenshotCount}枚 -> Gemini送信...");

            // ★修正: 固定文字列ではなく、設定から読み込んだモデルを使用
            var result = await _geminiService.AnalyzeAsync(files, _rules, _apiKey, _selectedModel);

            // ログに全文を出力するように変更
            WriteLog($"Gemini応答 ({result.Source}): {result.RawText}");

            HandleAnalysisResult(result);

            await _screenshotService.DeleteFilesAsync(files);
        }

        private void HandleAnalysisResult(GeminiAnalysisResult result)
        {
            if (_interventionService == null) return;

            if (result.IsViolation)
            {
                // ★修正: 1回の違反で +15 ポイント
                _violationPoints += 15;
                WriteLog($"⚠️ 違反判定! (+15pt) 現在:{_violationPoints}pt");

                string msg = $"ポイント: {_violationPoints}";
                if (!string.IsNullOrWhiteSpace(result.Reason))
                {
                    msg += $"\n理由: {result.Reason}";
                }
                ShowNotification("違反検知", msg);
            }
            else
            {
                // 正常時は -5 ポイント
                _violationPoints = Math.Max(0, _violationPoints - 5);
                WriteLog($"✅ 正常判定 (-5pt) 現在:{_violationPoints}pt");

                if (_violationPoints == 0) _interventionService.ResetAllInterventions();
            }

            // UI連携用ステータスファイルの更新
            UpdateStatusFile(_violationPoints);

            string goalSummary = _rules.Split('\n').FirstOrDefault() ?? "目標";
            _ = _interventionService.ApplyLevelAsync(_violationPoints, goalSummary);
        }

        private void UpdateStatusFile(int points)
        {
            try
            {
                // 初期化されていない場合のみパス解決・ストリームオープンを行う
                if (_statusLockStream == null)
                {
                    string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                    if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
                    string statusPath = Path.Combine(appData, "status.json");

                    // ReadWriteで開き、Read共有のみ許可（外部からの書き込み・削除を禁止）
                    _statusLockStream = new FileStream(statusPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                }

                var status = new { Points = points, LastUpdated = DateTime.Now };
                string json = JsonSerializer.Serialize(status);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

                if (_statusLockStream != null)
                {
                    _statusLockStream.Seek(0, SeekOrigin.Begin);
                    _statusLockStream.Write(bytes, 0, bytes.Length);
                    _statusLockStream.SetLength(bytes.Length); // 短くなった場合のために切り詰める
                    _statusLockStream.Flush();
                }
            }
            catch (Exception ex)
            {
                WriteLog($"ステータス更新エラー: {ex.Message}");
            }
        }

        private void ShowNotification(string title, string message)
        {
            _notifyIcon?.ShowBalloonTip(3000, title, message, Forms.ToolTipIcon.Info);
        }

        private void LoadSettings()
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                string configPath = Path.Combine(appData, "config.json");

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (settings != null)
                    {
                        if (settings.TryGetValue("ApiKey", out var key)) _apiKey = key;
                        if (settings.TryGetValue("Rules", out var rules)) _rules = rules;
                        if (settings.TryGetValue("CliPath", out var path) && !string.IsNullOrWhiteSpace(path)) _cliPath = path;

                        // ACP Script Path
                        if (settings.TryGetValue("AcpScriptPath", out var acpPath)) _acpScriptPath = acpPath;

                        // ★追加: UIで保存したモデル設定を読み込む
                        if (settings.TryGetValue("Model", out var model)) _selectedModel = model;

                        // ★追加: APIモード設定
                        if (settings.TryGetValue("UseApi", out var useApiStr) && bool.TryParse(useApiStr, out var useApi))
                        {
                            _useApi = useApi;
                        }

                        // ★追加: 終了時刻を読み込む
                        if (settings.TryGetValue("EndTime", out var endTimeStr) && DateTime.TryParse(endTimeStr, out var et))
                        {
                            _endTime = et;
                            WriteLog($"終了予定時刻: {_endTime:HH:mm:ss}");
                        }

                        WriteLog("設定読み込み完了");
                    }

                    // 読み込み後はファイルをロックして、外部からの変更・削除を防止する
                    if (_configLockStream == null)
                    {
                        // Readのみ許可（Write不可）で開き続ける
                        _configLockStream = new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        WriteLog($"設定ファイルをロックしました: {configPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"設定読み込みエラー: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _isCapturing = false;
            WriteLog("=== Agent Stopped ===");
            _notifyIcon?.Dispose();
            _interventionService?.Dispose();
            _geminiService?.Dispose();

            // ロック解放
            _configLockStream?.Close();
            _statusLockStream?.Close();

            base.OnExit(e);
        }
    }
}