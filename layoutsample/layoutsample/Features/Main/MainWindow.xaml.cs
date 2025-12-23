using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace layoutsample.Features.Main
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

        private layoutsample.Features.Setup.SetupPage? _setupPage;
        private layoutsample.Features.Settings.SettingsPage? _settingsPage;
        private layoutsample.Features.MonitoringOverlay.MonitoringOverlay? _monitoringOverlay;

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
            _setupPage = new layoutsample.Features.Setup.SetupPage(snackbarService, contentDialogService);
            _setupPage.StartMonitoringRequested += OnStartMonitoring;

            // 設定ページの作成
            _settingsPage = new layoutsample.Features.Settings.SettingsPage();

            // 監視オーバーレイの作成
            _monitoringOverlay = new layoutsample.Features.MonitoringOverlay.MonitoringOverlay();
            _monitoringOverlay.ToggleMiniModeRequested += OnToggleMiniModeClick;
            _monitoringOverlay.DragMoveRequested += OnDragMoveWindow;
            _monitoringOverlay.StopMonitoringRequested += OnStopMonitoring;
            MonitoringOverlayContainer.Content = _monitoringOverlay;

            // 初期ページとしてSetupPageに遷移
            NavigateToSetup();
        }

        private void RootNavigation_SelectionChanged(NavigationView sender, RoutedEventArgs e)
        {
            if (sender.SelectedItem is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                switch (tag)
                {
                    case "Setup":
                        NavigateToSetup();
                        break;
                    case "Settings":
                        NavigateToSettings();
                        break;
                }
            }
        }

        private void NavigationViewItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                System.Diagnostics.Debug.WriteLine($"[ナビゲーション] {item.Content} がクリックされました (Tag: {tag})");
                
                switch (tag)
                {
                    case "Setup":
                        NavigateToSetup();
                        break;
                    case "Settings":
                        NavigateToSettings();
                        break;
                }
            }
        }

        private void NavigateToSetup()
        {
            if (_setupPage != null && PageContent != null)
            {
                System.Diagnostics.Debug.WriteLine("[ナビゲーション] セットアップページに遷移");
                PageContent.Content = _setupPage;
            }
        }

        private void NavigateToSettings()
        {
            if (_settingsPage != null && PageContent != null)
            {
                System.Diagnostics.Debug.WriteLine("[ナビゲーション] 設定ページに遷移");
                PageContent.Content = _settingsPage;
            }
        }

        private void OnStartMonitoring(MonitoringSession session)
        {
            _monitoringOverlay?.Initialize(session);

            RootNavigation.Visibility = Visibility.Collapsed;
            MonitoringOverlayContainer.Visibility = Visibility.Visible;
        }

        private void OnStopMonitoring()
        {
            StopAndResetSession();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
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

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 画面幅が800px未満になったらナビゲーションメニューを自動で閉じる
            if (RootNavigation != null)
            {
                if (e.NewSize.Width < 800)
                {
                    RootNavigation.IsPaneOpen = false;
                }
            }
            // Visual Studioの出力ウィンドウに書き出す
            System.Diagnostics.Debug.WriteLine($"[画面サイズ] 幅: {e.NewSize.Width:F0} x 高さ: {e.NewSize.Height:F0}");
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
    }
}
