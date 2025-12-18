using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading; // ★これが必要です

// クラス名の衝突を避けるための設定
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;

namespace MonitAI.UI
{
    public partial class MainWindow : Window
    {
        // === メンバ変数の定義 ===

        // ログファイルのパス
        private string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "screenShot2",
            "agent_log.txt");

        // 設定ファイルのパス
        private string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "screenShot2",
            "config.json");

        private const string AgentProcessName = "MonitAI.Agent";
        private const string AgentExeName = "MonitAI.Agent.exe";

        // ★ログ更新用のタイマー
        private DispatcherTimer _logTimer;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApp();
        }

        private void InitializeApp()
        {
            // デフォルト設定
            if (string.IsNullOrWhiteSpace(FolderPathTextBox.Text))
            {
                FolderPathTextBox.Text = Path.Combine(Path.GetTempPath(), "MonitAI_Captures");
            }

            LoadSettings();
            CheckAgentStatus();

            // ★ログ監視タイマーの初期化と開始
            _logTimer = new DispatcherTimer();
            _logTimer.Interval = TimeSpan.FromSeconds(1);
            _logTimer.Tick += (s, e) => UpdateLogView();
            _logTimer.Start();
        }

        // ログファイルを読み込んで画面に表示する
        private void UpdateLogView()
        {
            try
            {
                if (File.Exists(LogPath))
                {
                    // Agentが書き込み中でも読めるように FileShare.ReadWrite を指定
                    using (var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        string content = sr.ReadToEnd();
                        if (LogTextBox.Text != content)
                        {
                            LogTextBox.Text = content;
                            LogTextBox.ScrollToEnd();
                        }
                    }
                }
                else
                {
                    LogTextBox.Text = "ログファイル待機中... (Agentが起動すると表示されます)";
                }
            }
            catch { }
        }

        private void CheckAgentStatus()
        {
            var processes = Process.GetProcessesByName(AgentProcessName);
            bool isRunning = processes.Length > 0;

            if (isRunning)
            {
                StatusTextBlock.Text = "Agent稼働中 (バックグラウンド)";
                StatusTextBlock.Foreground = Brushes.Green;
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
                BrowseButton.IsEnabled = false;
            }
            else
            {
                StatusTextBlock.Text = "Agent停止中";
                StatusTextBlock.Foreground = Brushes.Gray;
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                BrowseButton.IsEnabled = true;
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();

            try
            {
                // Agent.exe のパスを探す
                string agentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AgentExeName);

                // 見つからない場合は開発用フォルダも探す
                if (!File.Exists(agentPath))
                {
                    string devPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\MonitAI.Agent\bin\Debug\net8.0-windows", AgentExeName));
                    if (File.Exists(devPath)) agentPath = devPath;
                }

                if (File.Exists(agentPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = agentPath,
                        UseShellExecute = true
                    });

                    MessageBox.Show("監視エージェントを起動しました。", "起動成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    CheckAgentStatus();
                }
                else
                {
                    MessageBox.Show($"Agentが見つかりません。\n{agentPath}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"起動エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var processes = Process.GetProcessesByName(AgentProcessName);
                foreach (var p in processes)
                {
                    p.Kill();
                }
                CheckAgentStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止エラー: {ex.Message}");
            }
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "スクリーンショットの保存先",
                SelectedPath = FolderPathTextBox.Text
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FolderPathTextBox.Text = dialog.SelectedPath;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new Dictionary<string, string>
                {
                    { "ApiKey", ApiKeyPasswordBox.Password },
                    { "Rules", RulesTextBox.Text },
                    { "CliPath", CliPathTextBox.Text },
                    { "Model", ((ComboBoxItem)ModelComboBox.SelectedItem)?.Content.ToString() ?? "gemini-2.5-flash-lite" },
                    { "SavePath", FolderPathTextBox.Text }
                };

                string dir = Path.GetDirectoryName(ConfigPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定保存エラー: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (settings != null)
                    {
                        if (settings.TryGetValue("ApiKey", out var key)) ApiKeyPasswordBox.Password = key;
                        if (settings.TryGetValue("Rules", out var rules)) RulesTextBox.Text = rules;
                        if (settings.TryGetValue("CliPath", out var cli)) CliPathTextBox.Text = cli;
                        if (settings.TryGetValue("SavePath", out var path)) FolderPathTextBox.Text = path;
                    }
                }
            }
            catch { }
        }
    }
}