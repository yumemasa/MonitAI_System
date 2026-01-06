using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using MonitAI.UI.Features.Main;

namespace MonitAI.UI.Features.MonitoringOverlay
{
    /// <summary>
    /// 監視オーバーレイのUserControl。すべてのロジックをコードビハインドに統合。
    /// </summary>
    public partial class MonitoringOverlay : UserControl
    {
        private DispatcherTimer? _timer;
        private DispatcherTimer? _statusCheckTimer; // Agentの状態監視用タイマー
        private MonitoringSession? _currentSession;
        private bool _isMiniMode;
        private int _currentPenaltyLevel = 1;
        private int _currentPoints = 0;
        private DebugLogWindow? _debugLogWindow;
        private string _latestLogContent = string.Empty;
        private DispatcherTimer? _chaosTimer; // 高速色変化用（カオスモード）
        private readonly Random _random = new();

        private const int PointsPerLevel = 45; // ユーザー要望により45に変更
        private const double RingRadius = 190;
        private const double CenterX = 200;
        private const double CenterY = 200;

        private const string SessionFileName = "current_session.json";

        private static readonly BrushConverter BrushConverterInstance = new();

        /// <summary>
        /// セッションファイルのフルパスを取得します。
        /// </summary>
        private static string SessionFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "screenShot2",
            SessionFileName);

        /// <summary>
        /// ミニモード切り替えリクエストイベント。
        /// </summary>
        public event EventHandler? ToggleMiniModeRequested;

        /// <summary>
        /// ドラッグ移動リクエストイベント。
        /// </summary>
        public event EventHandler<MouseButtonEventArgs>? DragMoveRequested;

        /// <summary>
        /// 監視停止リクエストイベント。
        /// </summary>
        public event Action? StopMonitoringRequested;

        /// <summary>
        /// テーマ切り替えリクエストイベント。
        /// </summary>
        public event Action? RequestThemeToggle;

        /// <summary>
        /// 液体レベル変更イベント（ミニウィンドウ同期用）
        /// </summary>
        public event Action<double>? LiquidLevelChanged;

        /// <summary>
        /// ペナルティレベル変更イベント（ミニウィンドウ同期用）
        /// </summary>
        public event Action<int>? PenaltyLevelChanged;

        /// <summary>
        /// シェイクアニメーション発火イベント（ミニウィンドウ同期用）
        /// </summary>
        public event Action? ShakeRequested;

        /// <summary>
        /// ミニモードかどうか。
        /// </summary>
        public bool IsMiniMode => _isMiniMode;

        /// <summary>
        /// 現在のセッションを取得します。
        /// </summary>
        public MonitoringSession? CurrentSession => _currentSession;

        /// <summary>
        /// 現在のペナルティレベルを取得します。
        /// </summary>
        public int CurrentPenaltyLevel => _currentPenaltyLevel;

        /// <summary>
        /// 現在のポイント数から液体レベル（0.0～1.0）を取得します。
        /// </summary>
        public double LiquidScale => (double)_currentPoints / PointsPerLevel;

        /// <summary>
        /// MonitoringOverlayのコンストラクタ。
        /// </summary>
        public MonitoringOverlay()
        {
            InitializeComponent();
            this.Loaded += (s, e) => this.Focus(); // フォーカスを設定してキー入力を受け付ける
            this.PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl + L -> open log window
            if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ShowDebugLogWindow();
                e.Handled = true;
                return;
            }

            // Ctrl + Up -> +15pt
            if (e.Key == Key.Up && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _ = DebugAddPoints();
                e.Handled = true;
            }
            // Ctrl + Down -> -15pt
            else if (e.Key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _ = DebugSubtractPoints();
                e.Handled = true;
            }
            // Ctrl + Q -> 終了
            else if (e.Key == Key.Q && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                DebugFinishSession();
                e.Handled = true;
            }
        }


        /// <summary>
        /// 保存されたセッションファイルを読み込みます。
        /// 終了時刻が既に過ぎている場合はnullを返します。
        /// </summary>
        public static MonitoringSession? TryLoadSession()
        {
            try
            {
                if (!File.Exists(SessionFilePath))
                    return null;

                string json = File.ReadAllText(SessionFilePath);
                var session = JsonSerializer.Deserialize<MonitoringSession>(json);

                // セッションが存在し、まだ終了していない場合のみ返す
                if (session != null && session.RemainingSeconds > 0)
                {
                    return session;
                }

                // 既に終了しているセッションは削除
                File.Delete(SessionFilePath);
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TryLoadSession Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 保存されたセッションから復元して監視を再開します。
        /// Agent側のstatus.jsonからポイント状態も復元します。
        /// </summary>
        public void RestoreSession(MonitoringSession session)
        {
            if (session == null) return;

            _currentSession = session;

            // Agent側のstatus.jsonからポイント状態を読み込む
            RestorePointsFromAgent();

            // UI上のポイント表示を反映
            double ratio = (double)_currentPoints / PointsPerLevel;
            AnimateLiquid(ratio);

            if (DebugLogText != null) DebugLogText.Text = "";
            _latestLogContent = string.Empty;
            _debugLogWindow?.SetText(string.Empty);

            UpdateGoalDisplay();
            UpdatePenaltyDisplay();
            StartTimer();
        }

        /// <summary>
        /// Agentのstatus.jsonからポイント状態を復元します。
        /// </summary>
        private void RestorePointsFromAgent()
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                string statusPath = Path.Combine(appData, "status.json");

                if (File.Exists(statusPath))
                {
                    string json = File.ReadAllText(statusPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Points", out var pointsProp))
                    {
                        int totalPoints = pointsProp.GetInt32();

                        // ポイントからレベルを計算
                        if (totalPoints < 45)
                        {
                            _currentPenaltyLevel = 1;
                            _currentPoints = totalPoints;
                        }
                        else
                        {
                            _currentPenaltyLevel = (totalPoints / PointsPerLevel) + 1;
                            _currentPoints = totalPoints % PointsPerLevel;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RestorePointsFromAgent Error: {ex.Message}");
                _currentPenaltyLevel = 1;
                _currentPoints = 0;
            }
        }

        /// <summary>
        /// セッションを初期化します。
        /// </summary>
        public void Initialize(MonitoringSession? session)
        {
            if (session == null) return;

            _currentSession = session;
            _currentPenaltyLevel = 1;
            _currentPoints = 0;

            // UI上のポイント表示もリセット
            AnimateLiquid(0.0);
            if (DebugLogText != null) DebugLogText.Text = "";
            _latestLogContent = string.Empty;
            _debugLogWindow?.SetText(string.Empty);

            UpdateGoalDisplay();
            UpdatePenaltyDisplay();
            StartTimer();
        }

        /// <summary>
        /// セッションを停止します。
        /// </summary>
        public void StopSession()
        {
            _timer?.Stop();
            _statusCheckTimer?.Stop();
            StopChaosWater();
            EndSession();
        }

        /// <summary>
        /// ミニモードを切り替えます。
        /// </summary>
        public void ToggleMode()
        {
            _isMiniMode = !_isMiniMode;

            if (_isMiniMode)
            {
                NormalMonitorLayout.Visibility = Visibility.Collapsed;
                MiniMonitorLayout.Visibility = Visibility.Visible;
                if (DebugLogContainer != null) DebugLogContainer.Visibility = Visibility.Collapsed;
            }
            else
            {
                NormalMonitorLayout.Visibility = Visibility.Visible;
                MiniMonitorLayout.Visibility = Visibility.Collapsed;
                if (DebugLogContainer != null) DebugLogContainer.Visibility = Visibility.Visible;
            }
        }

        private void UpdateGoalDisplay()
        {
            if (_currentSession == null) return;

            if (GoalText != null)
            {
                GoalText.Text = TruncateText(_currentSession.Goal, 5, 25);
                GoalText.ToolTip = _currentSession.Goal;
            }

            if (NgText != null && !string.IsNullOrEmpty(_currentSession.NgItem))
            {
                NgText.Text = TruncateText(_currentSession.NgItem, 5, 25);
                NgText.ToolTip = _currentSession.NgItem;
                if (NgBorder != null)
                {
                    NgBorder.Visibility = Visibility.Visible;
                }
            }
            else if (NgBorder != null)
            {
                NgBorder.Visibility = Visibility.Collapsed;
            }
        }

        private string TruncateText(string text, int maxLines, int charsPerLine)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int currentLineCount = 0;
            var result = new System.Text.StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                int visualLines = (int)Math.Ceiling((double)Math.Max(line.Length, 1) / charsPerLine);

                if (currentLineCount + visualLines > maxLines)
                {
                    int remainingLines = maxLines - currentLineCount;
                    int charsToTake = remainingLines * charsPerLine;

                    if (charsToTake > 0 && line.Length >= charsToTake)
                    {
                        result.Append(line.Substring(0, Math.Max(0, charsToTake - 1)));
                    }
                    else
                    {
                        result.Append(line);
                    }

                    result.Append('…');
                    return result.ToString();
                }

                result.Append(line);
                currentLineCount += visualLines;

                if (i < lines.Length - 1)
                {
                    if (currentLineCount >= maxLines)
                    {
                        result.Append('…');
                        return result.ToString();
                    }
                    result.AppendLine();
                }
            }

            return result.ToString();
        }

        private void UpdatePenaltyDisplay()
        {
            if (CurrentLevelText != null)
            {
                CurrentLevelText.Text = $"Lv.{_currentPenaltyLevel}";
            }

            var (icon, name, color) = GetPenaltyInfo(_currentPenaltyLevel);

            if (NextPenaltyIconControl != null)
            {
                NextPenaltyIconControl.Symbol = icon;
            }
            if (NextPenaltyNameText != null)
            {
                NextPenaltyNameText.Text = name;
            }
            if (MiniNextPenaltyIcon != null)
            {
                MiniNextPenaltyIcon.Symbol = icon;
            }

            // Update liquid and status colors
            var brush = (SolidColorBrush)BrushConverterInstance.ConvertFrom(color)!;
            if (LiquidWater != null)
            {
                LiquidWater.Background = brush;
            }
            if (MiniLiquidWater != null)
            {
                MiniLiquidWater.Background = brush;
            }
            // カオスモード（高速色切替）: 本番は Lv6 で発火
            if (_currentPenaltyLevel == 6)
            {
                StartChaosWater();
            }
            else
            {
                StopChaosWater();
            }
            // StatusText removed from UI — no-op here to avoid null refs
        }

        private (SymbolRegular icon, string name, string color) GetPenaltyInfo(int level)
        {
            // "きれいな水" から "汚染・危険の蓄積" へイメージを変更
            return level switch
            {
                // Lv1: 濁った黄緑 (酸・不快感)
                1 => (SymbolRegular.Alert24, "通知 (警告)", "#CDDC39"),

                // Lv2: アンバー (警告・汚染)
                2 => (SymbolRegular.Color24, "グレースケール化", "#FFC107"),

                // Lv3: 赤茶/ディープオレンジ (錆・加熱)
                3 => (SymbolRegular.Warning24, "操作妨害", "#FF5722"),

                // Lv4: 赤 (危険・血)
                4 => (SymbolRegular.Speaker224, "ビープ音", "#D32F2F"),

                // Lv5: 紫 (毒・侵食)
                5 => (SymbolRegular.LockClosed24, "画面ロック", "#7B1FA2"),

                // Lv6: ダークスレート (停止・泥・絶望)
                6 => (SymbolRegular.Power24, "シャットダウン", "#37474F"),

                _ => (SymbolRegular.Checkmark24, "不明", "#808080")
            };
        }
        private void StartTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Agentの状態監視用タイマー (1秒ごとにチェック)
            _statusCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statusCheckTimer.Tick += StatusCheckTimer_Tick;
            _statusCheckTimer.Start();

            UpdateTimerState();
        }

        #region Chaos water (高速色ランダム切替)

        private void StartChaosWater()
        {
            _chaosTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _chaosTimer.Tick -= OnChaosTick;
            _chaosTimer.Tick += OnChaosTick;
            _chaosTimer.Start();
        }

        private void StopChaosWater()
        {
            if (_chaosTimer != null)
            {
                _chaosTimer.Stop();
            }
        }

        private void OnChaosTick(object? sender, EventArgs e)
        {
            Color NextColor() => Color.FromRgb((byte)_random.Next(32, 256), (byte)_random.Next(32, 256), (byte)_random.Next(32, 256));

            var brush = new SolidColorBrush(NextColor());
            brush.Freeze();

            if (LiquidWater != null)
            {
                LiquidWater.Background = brush;
            }
            if (MiniLiquidWater != null)
            {
                MiniLiquidWater.Background = brush;
            }
        }

        #endregion

        private void StatusCheckTimer_Tick(object? sender, EventArgs e)
        {
            CheckAgentStatus();
        }

        private void CheckAgentStatus()
        {
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                string statusPath = Path.Combine(appData, "status.json");

                if (File.Exists(statusPath))
                {
                    string json = File.ReadAllText(statusPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("Points", out var pointsProp))
                    {
                        int totalPoints = pointsProp.GetInt32();
                        UpdatePointsFromAgent(totalPoints);
                    }
                }

                // ログ読み込み
                string logPath = Path.Combine(appData, "agent_log.txt");
                if (File.Exists(logPath))
                {
                    using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var sr = new StreamReader(fs))
                    {
                        // 読み込みサイズを拡大 (4KB -> 64KB)
                        if (fs.Length > 65536)
                        {
                            fs.Seek(-65536, SeekOrigin.End);
                        }
                        string content = sr.ReadToEnd();

                        // 全文表示 (スクロール可能になったため行数制限を撤廃)
                        _latestLogContent = content;
                        if (DebugLogText != null)
                        {
                            DebugLogText.Text = content;
                            DebugLogText.ScrollToEnd();
                        }
                        _debugLogWindow?.SetText(content);
                    }
                }
                else
                {
                    if (DebugLogText != null && string.IsNullOrEmpty(DebugLogText.Text))
                    {
                        DebugLogText.Text = "Waiting for agent log...";
                        _latestLogContent = DebugLogText.Text;
                        _debugLogWindow?.SetText(_latestLogContent);
                    }
                }
            }
            catch { }
        }

        private void UpdatePointsFromAgent(int totalPoints)
        {
            // ポイントからレベルと現在のゲージ量を計算
            // レベル1: 45pt以上
            // レベル2: 90pt以上
            // ...

            int newLevel = 1;
            int displayPoints = totalPoints;

            if (totalPoints < 45)
            {
                newLevel = 1;
                displayPoints = totalPoints;
            }
            else
            {
                // 45以上の場合
                // 例: 45 -> Level 2 (ゲージ0), 60 -> Level 2 (ゲージ15)
                // ユーザー要望: "第一段階の通知は45を超えた時発火"
                // つまり 0-44 は Level 1 (通知なし) のゲージ蓄積中
                // 45になった瞬間 Level 2 (通知あり) に突入し、ゲージはリセットされるイメージか、
                // あるいは満タンのままか。
                // ここでは「レベルアップしてゲージリセット」の挙動を再現する。

                newLevel = (totalPoints / PointsPerLevel) + 1;
                displayPoints = totalPoints % PointsPerLevel;
            }

            // レベルが変わったらアニメーション
            if (newLevel > _currentPenaltyLevel)
            {
                _currentPenaltyLevel = newLevel;
                UpdatePenaltyDisplay();
                
                // レベルアップ時は水を満タン状態に保つ
                AnimateLiquid(1.0);
                LiquidLevelChanged?.Invoke(1.0);
                
                ShakeMonitor();
                // ミニウィンドウに通知
                PenaltyLevelChanged?.Invoke(_currentPenaltyLevel);
                ShakeRequested?.Invoke();
                
                // シェイク終了後（750ms + 少し余裕）に水をリセット
                int delayedPoints = displayPoints;
                _ = Task.Delay(850).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _currentPoints = delayedPoints;
                        double ratio = (double)_currentPoints / PointsPerLevel;
                        AnimateLiquid(ratio);
                        LiquidLevelChanged?.Invoke(ratio);
                    });
                });
                return; // 既にポイント処理したのでここで終了
            }
            else if (newLevel < _currentPenaltyLevel)
            {
                _currentPenaltyLevel = newLevel;
                UpdatePenaltyDisplay();
                // ミニウィンドウに通知
                PenaltyLevelChanged?.Invoke(_currentPenaltyLevel);
            }

            // ポイント表示更新
            if (_currentPoints != displayPoints)
            {
                _currentPoints = displayPoints;
                double ratio = (double)_currentPoints / PointsPerLevel;
                AnimateLiquid(ratio);
                // ミニウィンドウに通知
                LiquidLevelChanged?.Invoke(ratio);
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            UpdateTimerState();

            if (_currentSession != null)
            {
                SaveSession(_currentSession);
            }
        }

        private void UpdateTimerState()
        {
            if (_currentSession == null) return;

            double remaining = _currentSession.RemainingSeconds;

            if (remaining <= 0)
            {
                StopSession();
                UpdateTimeDisplay("00:00");
                UpdateProgressRing(0);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    // 最前面に表示するために DefaultDesktopOnly オプションを使用
                    System.Windows.MessageBox.Show("集中セッション終了！お疲れ様でした。", "monitAI", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information, System.Windows.MessageBoxResult.OK, System.Windows.MessageBoxOptions.DefaultDesktopOnly);
                });

                StopMonitoringRequested?.Invoke();
                return;
            }

            TimeSpan t = TimeSpan.FromSeconds(remaining);
            string timeText = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}";

            UpdateTimeDisplay(timeText);

            double totalSeconds = _currentSession.DurationMinutes * 60;
            double progressValue = totalSeconds > 0 ? (remaining / totalSeconds) * 100 : 0;
            UpdateProgressRing(progressValue);
        }

        private void UpdateTimeDisplay(string timeText)
        {
            if (TimeDisplay != null)
            {
                TimeDisplay.Text = timeText;
            }
            if (MiniTimeDisplay != null)
            {
                MiniTimeDisplay.Text = timeText;
            }
        }

        private void UpdateProgressRing(double progressValue)
        {
            double angle = progressValue * 3.6;
            
            // 残りが0以下なら円弧を非表示
            if (progressValue <= 0)
            {
                if (ArcPath != null) ArcPath.Visibility = Visibility.Collapsed;
                if (MiniArcPath != null) MiniArcPath.Visibility = Visibility.Collapsed;
                return;
            }
            else
            {
                if (ArcPath != null) ArcPath.Visibility = Visibility.Visible;
                if (MiniArcPath != null) MiniArcPath.Visibility = Visibility.Visible;
            }
            
            if (angle >= 360) angle = 359.99;
            if (angle <= 0) angle = 0.01;

            double radians = (angle - 90) * (Math.PI / 180);
            bool isLargeArc = angle > 180;

            // 通常モード用の終点計算
            double x = CenterX + RingRadius * Math.Cos(radians);
            double y = CenterY + RingRadius * Math.Sin(radians);
            var endPoint = new Point(x, y);

            if (ArcSegment != null)
            {
                ArcSegment.Point = endPoint;
                ArcSegment.IsLargeArc = isLargeArc;
            }

            // ミニモード用も同じ座標系を使用
            if (MiniArcSegment != null)
            {
                MiniArcSegment.Point = endPoint;
                MiniArcSegment.IsLargeArc = isLargeArc;
            }
        }

        private async void OnDebugAddPointsClick(object sender, RoutedEventArgs e)
        {
            await DebugAddPoints();
        }

        public async Task DebugAddPoints()
        {
            // Agentにコマンド送信
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                string commandPath = Path.Combine(appData, "command.json");

                var command = new { Command = "AddPoints", Value = 15 };
                string json = JsonSerializer.Serialize(command);
                await File.WriteAllTextAsync(commandPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debug Command Error: {ex.Message}");
            }
        }

        public async Task DebugSubtractPoints()
        {
            // Agentにコマンド送信 (-15pt)
            try
            {
                string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
                string commandPath = Path.Combine(appData, "command.json");

                var command = new { Command = "AddPoints", Value = -15 };
                string json = JsonSerializer.Serialize(command);
                await File.WriteAllTextAsync(commandPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debug Command Error: {ex.Message}");
            }
        }

        private void AnimateLiquid(double targetScale)
        {
            var animation = new DoubleAnimation
            {
                To = targetScale,
                Duration = TimeSpan.FromMilliseconds(800),
                EasingFunction = new ElasticEase
                {
                    Oscillations = 1,
                    Springiness = 5,
                    EasingMode = EasingMode.EaseOut
                }
            };

            LiquidScaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private void ShakeMonitor()
        {
            ShakeElement(MonitorContainer);
        }

        private void ShakeElement(FrameworkElement? element)
        {
            if (element?.RenderTransform is not TranslateTransform transform) return;

            var shakeAnimation = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromMilliseconds(750)
            };

            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(100))));
            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))));
            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(6, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400))));
            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(-3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))));
            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(600))));
            shakeAnimation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750))));

            transform.BeginAnimation(TranslateTransform.XProperty, shakeAnimation);
        }

        private void OnStopSessionClick(object sender, RoutedEventArgs e)
        {
            DebugFinishSession();
        }

        public void DebugFinishSession()
        {
            if (_currentSession != null)
            {
                _currentSession.StartTime = DateTime.Now.AddMinutes(-_currentSession.DurationMinutes - 1);
            }
            UpdateTimerState();
        }

        private void OnToggleMiniModeClick(object sender, RoutedEventArgs e)
        {
            ToggleMiniModeRequested?.Invoke(this, EventArgs.Empty);
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = Window.GetWindow(this);
                if (win != null)
                {
                    win.WindowState = WindowState.Minimized;
                }
            }
            catch { }
        }

        private void OnDragMoveWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMoveRequested?.Invoke(this, e);
            }
        }

        private void OnDebugLogToggleClick(object sender, RoutedEventArgs e)
        {
            ShowDebugLogWindow();
        }

        public void ShowDebugLogWindow()
        {
            if (_debugLogWindow == null)
            {
                _debugLogWindow = new DebugLogWindow
                {
                    Owner = Window.GetWindow(this)
                };
            }

            _debugLogWindow.SetText(_latestLogContent);
            if (_debugLogWindow.IsVisible)
            {
                _debugLogWindow.Activate();
            }
            else
            {
                _debugLogWindow.Show();
            }
        }

        // Session persistence methods
        private void SaveSession(MonitoringSession session)
        {
            try
            {
                // フォルダがなければ作成
                string? dir = Path.GetDirectoryName(SessionFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(session);
                File.WriteAllText(SessionFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save Error: {ex.Message}");
            }
        }

        private void EndSession()
        {
            try
            {
                if (File.Exists(SessionFilePath))
                {
                    File.Delete(SessionFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"EndSession Error: {ex.Message}");
            }
        }

        // 例: テーマ切り替えボタンのクリックイベントハンドラを追加
        private void OnThemeToggleClick(object sender, RoutedEventArgs e)
        {
            RequestThemeToggle?.Invoke();
        }
    }
}