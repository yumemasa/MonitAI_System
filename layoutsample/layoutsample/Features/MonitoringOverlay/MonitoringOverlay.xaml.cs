using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using layoutsample.Features.Main;

namespace layoutsample.Features.MonitoringOverlay
{
    /// <summary>
    /// 監視オーバーレイのUserControl。すべてのロジックをコードビハインドに統合。
    /// </summary>
    public partial class MonitoringOverlay : UserControl
    {
        private DispatcherTimer? _timer;
        private MonitoringSession? _currentSession;
        private bool _isMiniMode;
        private int _currentPenaltyLevel = 1;
        private int _currentPoints = 0;

        private const int PointsPerLevel = 100;
        private const int ShakeAnimationDelayMs = 900;
        private const double RingRadius = 190;
        private const double CenterX = 200;
        private const double CenterY = 200;
        private const string SessionFileName = "current_session.json";

        private static readonly BrushConverter BrushConverterInstance = new();

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
        /// ミニモードかどうか。
        /// </summary>
        public bool IsMiniMode => _isMiniMode;

        /// <summary>
        /// MonitoringOverlayのコンストラクタ。
        /// </summary>
        public MonitoringOverlay()
        {
            InitializeComponent();
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
            }
            else
            {
                NormalMonitorLayout.Visibility = Visibility.Visible;
                MiniMonitorLayout.Visibility = Visibility.Collapsed;
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
            if (StatusText != null)
            {
                StatusText.Foreground = brush;
            }
        }

        private (SymbolRegular icon, string name, string color) GetPenaltyInfo(int level)
        {
            return level switch
            {
                1 => (SymbolRegular.Alert24, "通知 (警告)", "#80DEEA"),
                2 => (SymbolRegular.Color24, "グレースケール化", "#26C6DA"),
                3 => (SymbolRegular.Keyboard24, "入力遅延", "#FFEE58"),
                4 => (SymbolRegular.CursorHover24, "カーソル反転", "#FFA726"),
                5 => (SymbolRegular.Speaker224, "ビープ音", "#EF5350"),
                6 => (SymbolRegular.LockClosed24, "画面ロック", "#C62828"),
                7 => (SymbolRegular.Power24, "シャットダウン", "#4A0000"),
                _ => (SymbolRegular.Checkmark24, "不明", "#808080")
            };
        }

        private void StartTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _timer.Start();
            UpdateTimerState();
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
                    System.Windows.MessageBox.Show("集中セッション終了！お疲れ様でした。", "monitAI");
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
            if (angle >= 360) angle = 359.99;
            if (angle <= 0) angle = 0.01;

            double radians = (angle - 90) * (Math.PI / 180);
            double x = CenterX + RingRadius * Math.Cos(radians);
            double y = CenterY + RingRadius * Math.Sin(radians);

            var endPoint = new Point(x, y);
            bool isLargeArc = angle > 180;

            if (ArcSegment != null)
            {
                ArcSegment.Point = endPoint;
                ArcSegment.IsLargeArc = isLargeArc;
            }
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

        private async Task DebugAddPoints()
        {
            int nextPoints = _currentPoints + 20;

            if (nextPoints >= PointsPerLevel)
            {
                _currentPoints = PointsPerLevel;
                AnimateLiquid(1.0);

                if (_currentPenaltyLevel < 7)
                {
                    _currentPenaltyLevel++;
                    UpdatePenaltyDisplay();
                    ShakeMonitor();
                }

                await Task.Delay(ShakeAnimationDelayMs);
                _currentPoints = 0;
                AnimateLiquid(0.0);
            }
            else
            {
                _currentPoints = nextPoints;
                AnimateLiquid((double)_currentPoints / PointsPerLevel);
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
            MiniLiquidScaleTransform?.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }

        private void ShakeMonitor()
        {
            ShakeElement(MonitorContainer);
            ShakeElement(MiniMonitorContainer);
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

        private void DebugFinishSession()
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

        private void OnDragMoveWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMoveRequested?.Invoke(this, e);
            }
        }

        // Session persistence methods
        private void SaveSession(MonitoringSession session)
        {
            try
            {
                string json = JsonSerializer.Serialize(session);
                File.WriteAllText(SessionFileName, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save Error: {ex.Message}");
            }
        }

        private void EndSession()
        {
            if (File.Exists(SessionFileName))
            {
                File.Delete(SessionFileName);
            }
        }

        // 例: テーマ切り替えボタンのクリックイベントハンドラを追加
        private void OnThemeToggleClick(object sender, RoutedEventArgs e)
        {
            RequestThemeToggle?.Invoke();
        }
    }
}
