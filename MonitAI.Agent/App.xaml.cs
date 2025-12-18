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
        private ScreenshotService? _screenshotService;
        private GeminiService? _geminiService;
        private InterventionService? _interventionService;

        private bool _isCapturing = false;
        private int _screenshotCount = 0;
        private int _violationPoints = 0;
        private string _saveFolderPath = string.Empty;
        private Random _random = new Random();

        private Forms.NotifyIcon? _notifyIcon;
        private string _apiKey = "";
        private string _rules = "";
        private string _cliPath = @"C:\nvm4w\nodejs\gemini.cmd";

        // ★ログファイルのパス (AppDataに保存)
        private string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "screenShot2",
            "agent_log.txt");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 起動時にログをクリア（または区切りを入れる）
            WriteLog("=== Agent Started ===");

            LoadSettings();
            InitializeServices();
            SetupTrayIcon();

            if (!string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_rules))
            {
                StartMonitoring();
            }
            else
            {
                string msg = "設定不足: APIキーまたはルールがありません";
                WriteLog(msg);
                ShowNotification("設定不足", msg);
            }
        }

        // ★ログ書き込みメソッド（ここが重要）
        private void WriteLog(string message)
        {
            try
            {
                string logLine = $"[{DateTime.Now:HH:mm:ss}] {message}";
                // デバッグ出力にも出す
                Debug.WriteLine(logLine);

                // ファイルに追記 (UIがこれを読み取る)
                string dir = Path.GetDirectoryName(LogPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath, logLine + Environment.NewLine);
            }
            catch { }
        }

        private async void InitializeServices()
        {
            try
            {
                _screenshotService = new ScreenshotService();

                _saveFolderPath = _screenshotService.DefaultSaveFolderPath ?? Path.Combine(Path.GetTempPath(), "MonitAI_Captures");
                if (!Directory.Exists(_saveFolderPath)) Directory.CreateDirectory(_saveFolderPath);
                WriteLog($"保存先: {_saveFolderPath}");

                _geminiService = new GeminiService();
                _geminiService.GeminiCliCommand = _cliPath;

                // ★CLI接続チェック
                WriteLog($"CLIパス: {_cliPath}");
                WriteLog("CLI接続チェック中...");
                bool cliOk = await _geminiService.CheckCliConnectionAsync();
                WriteLog(cliOk ? "✅ CLI接続OK" : "❌ CLI接続失敗 (設定を確認してください)");

                _interventionService = new InterventionService();
                _interventionService.OnLog += msg => WriteLog($"[介入] {msg}");
                _interventionService.OnNotification += (msg, title) => ShowNotification(title, msg);
            }
            catch (Exception ex)
            {
                WriteLog($"初期化エラー: {ex.Message}");
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

        private void StartMonitoring()
        {
            if (_isCapturing) return;

            _screenshotCount = 0;
            _isCapturing = true;
            WriteLog("🚀 監視・撮影サイクルを開始しました (ランダム)");

            _ = StartCaptureLoop();
        }

        private async Task StartCaptureLoop()
        {
            const int cycleMs = 20000;

            while (_isCapturing)
            {
                try
                {
                    int delay = _random.Next(1000, cycleMs - 1000);
                    WriteLog($"待機: {delay / 1000.0:F1}秒...");

                    await Task.Delay(delay);

                    if (!_isCapturing) break;

                    await PerformCaptureAndAnalysis();

                    int remaining = cycleMs - delay;
                    if (remaining > 0) await Task.Delay(remaining);
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
            // ログには枚数を表示
            WriteLog($"📸 撮影成功 ({files.Count}枚) 合計:{_screenshotCount}枚 -> Gemini送信...");

            string modelName = "gemini-2.5-flash-lite";

            var result = await _geminiService.AnalyzeAsync(files, _rules, _apiKey, modelName);

            WriteLog($"Gemini応答: {result.RawText.Replace("\n", " ").Substring(0, Math.Min(50, result.RawText.Length))}...");

            HandleAnalysisResult(result);

            foreach (var f in files) { try { File.Delete(f); } catch { } }
        }

        private void HandleAnalysisResult(GeminiAnalysisResult result)
        {
            if (_interventionService == null) return;

            if (result.IsViolation)
            {
                _violationPoints += 30;
                WriteLog($"⚠️ 違反判定! (+30pt) 現在:{_violationPoints}pt");
                ShowNotification("違反検知", $"ポイント: {_violationPoints}");
            }
            else
            {
                _violationPoints = Math.Max(0, _violationPoints - 5);
                WriteLog($"✅ 正常判定 (-5pt) 現在:{_violationPoints}pt");

                if (_violationPoints == 0) _interventionService.ResetAllInterventions();
            }

            string goalSummary = _rules.Split('\n').FirstOrDefault() ?? "目標";
            _ = _interventionService.ApplyLevelAsync(_violationPoints, goalSummary);
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
                        if (settings.TryGetValue("CliPath", out var path)) _cliPath = path;
                        WriteLog("設定読み込み完了");
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
            base.OnExit(e);
        }
    }
}