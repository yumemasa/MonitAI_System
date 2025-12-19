using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace MonitAI.UI.Features.Main
{
    /// <summary>
    /// メインウィンドウ。すべてのロジックをコードビハインドに統合。
    /// </summary>
    public partial class MainWindow : FluentWindow
    {
        private double _restoredWidth;
        private double _restoredHeight;
        private double _restoredTop;
        private double _restoredLeft;
        private WindowState _restoredState;
        private ResizeMode _restoredResizeMode;
        private WindowStyle _restoredWindowStyle;

        private MonitAI.UI.Features.Setup.SetupPage? _setupPage;
        private MonitAI.UI.Features.Settings.SettingsPage? _settingsPage;
        private MonitAI.UI.Features.MonitoringOverlay.MonitoringOverlay? _monitoringOverlay;

        /// <summary>
        /// MainWindowのコンストラクタ。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// ウィンドウ読み込み時の初期化処理。
        /// </summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {

            // Snackbarサービスの設定
            var snackbarService = new Wpf.Ui.SnackbarService();
            snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);

            // ContentDialogサービスの設定
            var contentDialogService = new Wpf.Ui.ContentDialogService();
            contentDialogService.SetDialogHost(RootContentDialogPresenter);

            // セットアップページの作成と初期化
            _setupPage = new MonitAI.UI.Features.Setup.SetupPage(snackbarService, contentDialogService);
            _setupPage.StartMonitoringRequested += OnStartMonitoring;

            // 設定ページの作成
            _settingsPage = new MonitAI.UI.Features.Settings.SettingsPage();

            // 監視オーバーレイの作成
            _monitoringOverlay = new MonitAI.UI.Features.MonitoringOverlay.MonitoringOverlay();
            _monitoringOverlay.ToggleMiniModeRequested += OnToggleMiniModeClick;
            _monitoringOverlay.DragMoveRequested += OnDragMoveWindow;
            _monitoringOverlay.StopMonitoringRequested += OnStopMonitoring;
            MonitoringOverlayContainer.Content = _monitoringOverlay;

            // 初期ページとしてSetupPageに遷移
            NavigateToSetup();
        }

        private void RootNavigation_ItemInvoked(object sender, RoutedEventArgs e)
        {
            // クリックされた要素(OriginalSource)から親を辿って NavigationViewItem を探す
            // SelectedItem は更新前の可能性があるため使用しない
            var item = GetParentNavigationViewItem(e.OriginalSource as DependencyObject);

            if (item == null)
            {
                return;
            }

            var tag = item.Tag?.ToString();

            switch (tag)
            {
                case "Settings":
                    NavigateToSettings();
                    break;
                case "Setup":
                    NavigateToSetup();
                    break;
            }
        }

        private NavigationViewItem? GetParentNavigationViewItem(DependencyObject? child)
        {
            DependencyObject? parent = child;
            while (parent != null)
            {
                if (parent is NavigationViewItem item)
                {
                    return item;
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void NavigateToSetup()
        {
            if (_setupPage != null && PageContent != null)
            {
                PageContent.Content = _setupPage;
            }
        }

        private void NavigateToSettings()
        {
            if (_settingsPage != null && PageContent != null)
            {
                PageContent.Content = _settingsPage;
            }
        }

        private void OnStartMonitoring(MonitoringSession session)
        {
            _monitoringOverlay?.Initialize(session);

            RootNavigation.Visibility = Visibility.Collapsed;
            MonitoringOverlayContainer.Visibility = Visibility.Visible;

            StartAgentProcess(session);
        }

        private void StartAgentProcess(MonitoringSession session)
        {
            try
            {
                // 1. 設定ファイルの更新 (Agentが読み込む config.json)
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                if (!Directory.Exists(appData))
                {
                    Directory.CreateDirectory(appData);
                }
                string configPath = Path.Combine(appData, "config.json");

                var settings = new Dictionary<string, string>();
                if (File.Exists(configPath))
                {
                    try
                    {
                        string json = File.ReadAllText(configPath);
                        var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (existing != null) settings = existing;
                    }
                    catch { /* 無視して新規作成 */ }
                }

                // セッション情報をルールとして書き込む
                string rules = $"目標: {session.Goal}\nNG行動: {session.NgItem}";
                settings["Rules"] = rules;

                File.WriteAllText(configPath, JsonSerializer.Serialize(settings));

                // 2. Agentプロセスの起動
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string agentPath = Path.Combine(currentDir, "MonitAI.Agent.exe");

                if (File.Exists(agentPath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = agentPath,
                        UseShellExecute = true // 実行ファイルとして起動
                    });
                }
                else
                {
                    System.Windows.MessageBox.Show($"Agentが見つかりません: {agentPath}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Agent起動エラー: {ex.Message}", "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void StopAgentProcess()
        {
            try
            {
                // 名前でプロセスを探して終了させる (簡易実装)
                foreach (var process in Process.GetProcessesByName("MonitAI.Agent"))
                {
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Agent停止エラー: {ex.Message}");
            }
        }

        private void OnStopMonitoring()
        {
            StopAgentProcess();
            StopAndResetSession();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopAgentProcess();
            _setupPage?.SaveUserData();
        }

        private void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            OnToggleThemeClick();
        }

        private void OnToggleThemeClick()
        {
            var currentTheme = ApplicationThemeManager.GetAppTheme();
            ApplicationThemeManager.Apply(
                currentTheme == ApplicationTheme.Light ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }

        private void StopAndResetSession()
        {
            _monitoringOverlay?.StopSession();

            RootNavigation.Visibility = Visibility.Visible;
            MonitoringOverlayContainer.Visibility = Visibility.Collapsed;

            if (_monitoringOverlay?.IsMiniMode == true)
            {
                RestoreWindow();
                _monitoringOverlay?.ToggleMode();
            }
        }

        private void OnDragMoveWindow(object? sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnToggleMiniModeClick(object? sender, EventArgs e)
        {
            if (_monitoringOverlay == null) return;

            _monitoringOverlay.ToggleMode();

            if (_monitoringOverlay.IsMiniMode)
            {
                SaveWindowState();

                if (WindowState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                Topmost = true;
                ExtendsContentIntoTitleBar = false;
                MinWidth = 0;
                MinHeight = 0;
                ShowInTaskbar = true;

                // DPIスケールを考慮してサイズと位置を計算
                var dpiScale = GetDpiScale();
                double miniWidth = 180;
                double miniHeight = 180;

                Width = miniWidth;
                Height = miniHeight;
                Background = Brushes.Transparent;

                // DPIスケールを考慮してワークエリア内に配置
                var area = SystemParameters.WorkArea;
                Left = area.Right - miniWidth - (20 / dpiScale.X);
                Top = area.Bottom - miniHeight - (20 / dpiScale.Y);

                MainTitleBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                RestoreWindow();
            }
        }

        /// <summary>
        /// 現在のDPIスケールを取得します。
        /// </summary>
        /// <returns>DPIスケール（X, Y）</returns>
        private (double X, double Y) GetDpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var matrix = source.CompositionTarget.TransformToDevice;
                return (matrix.M11, matrix.M22);
            }
            return (1.0, 1.0);
        }

        private void SaveWindowState()
        {
            _restoredWidth = Width;
            _restoredHeight = Height;
            _restoredTop = Top;
            _restoredLeft = Left;
            _restoredState = WindowState;
            _restoredResizeMode = ResizeMode;
            _restoredWindowStyle = WindowStyle;
        }

        private void RestoreWindow()
        {
            WindowStyle = _restoredWindowStyle;
            ResizeMode = _restoredResizeMode;
            ExtendsContentIntoTitleBar = true;
            Topmost = false;
            Width = _restoredWidth;
            Height = _restoredHeight;
            Top = _restoredTop;
            Left = _restoredLeft;
            WindowState = _restoredState;
            Background = (Brush)FindResource("ApplicationBackgroundBrush");
            MainTitleBar.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 監視セッションの状態データ。
    /// </summary>
    public class MonitoringSession
    {
        public bool IsActive { get; set; }
        public DateTime StartTime { get; set; }
        public double DurationMinutes { get; set; }
        public int CurrentPenaltyLevel { get; set; }
        public string Goal { get; set; } = string.Empty;
        public string NgItem { get; set; } = string.Empty;
        public DateTime EndTime => StartTime.AddMinutes(DurationMinutes);
        public double RemainingSeconds
        {
            get
            {
                var remaining = (EndTime - DateTime.Now).TotalSeconds;
                return remaining > 0 ? remaining : 0;
            }
        }
        private void RootNavigation_ItemInvoked(object sender, RoutedEventArgs e)
        {
        }
    }
}
