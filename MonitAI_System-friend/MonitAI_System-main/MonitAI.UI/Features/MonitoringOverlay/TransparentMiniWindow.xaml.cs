using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Controls;
using MonitAI.UI.Features.Main;

namespace MonitAI.UI.Features.MonitoringOverlay
{
    public partial class TransparentMiniWindow : Window
    {
        // Win32 API for taskbar minimize/restore support
        private const int GWL_STYLE = -16;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private MonitoringSession? _session;
        private DispatcherTimer? _timer;
        private DispatcherTimer? _buttonHideTimer;
        private DispatcherTimer? _chaosTimer; // 高速色変化（カオスモード）
        private bool _isTransparent = false;
        private int _currentPenaltyLevel = 1;
        private readonly Random _random = new();

        // 透過時の背景ブラシ
        private static readonly Brush TransparentOuterBrush = new SolidColorBrush(Color.FromArgb(40, 32, 32, 32));
        private static readonly Brush TransparentInnerBrush = Brushes.Transparent;
        private static readonly Brush TransparentBorderBrush = new SolidColorBrush(Color.FromArgb(60, 64, 64, 64));

        // 通常時の背景ブラシ
        private static readonly Brush NormalOuterBrush = new SolidColorBrush(Color.FromArgb(224, 32, 32, 32)); // #E0202020
        private static readonly Brush NormalInnerBrush = new SolidColorBrush(Color.FromRgb(45, 45, 45)); // #2D2D2D
        private static readonly Brush NormalBorderBrush = new SolidColorBrush(Color.FromRgb(64, 64, 64)); // #404040

        private const double RingRadius = 190;
        private const double CenterX = 200;
        private const double CenterY = 200;

        /// <summary>
        /// 通常モードへ戻るリクエスト
        /// </summary>
        public event Action? RestoreRequested;

        /// <summary>
        /// ログウィンドウ表示リクエスト (Ctrl+L)
        /// </summary>
        public event Action? ShowLogRequested;

        /// <summary>
        /// デバッグポイント追加リクエスト (Ctrl+Up)
        /// </summary>
        public event Action? DebugAddPointsRequested;

        /// <summary>
        /// デバッグポイント減少リクエスト (Ctrl+Down)
        /// </summary>
        public event Action? DebugSubtractPointsRequested;

        /// <summary>
        /// セッション終了リクエスト (Ctrl+Q)
        /// </summary>
        public event Action? DebugFinishSessionRequested;

        public TransparentMiniWindow()
        {
            InitializeComponent();
            PositionBottomLeft();
            this.PreviewKeyDown += OnPreviewKeyDown;
            this.Loaded += (s, e) => this.Focus();
            this.SourceInitialized += OnSourceInitialized;
            
            // ボタン自動非表示タイマー
            _buttonHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _buttonHideTimer.Tick += (s, e) =>
            {
                _buttonHideTimer.Stop();
                if (!IsMouseOver)
                {
                    AnimateButtonOpacity(0.0);
                }
            };
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            // タスクバーからの最小化/復元を有効にするためにウィンドウスタイルを設定
            var hwnd = new WindowInteropHelper(this).Handle;
            int style = GetWindowLong(hwnd, GWL_STYLE);
            SetWindowLong(hwnd, GWL_STYLE, style | WS_MINIMIZEBOX);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl + L -> ログウィンドウ表示
            if (e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ShowLogRequested?.Invoke();
                e.Handled = true;
                return;
            }

            // Ctrl + Up -> +15pt
            if (e.Key == Key.Up && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                DebugAddPointsRequested?.Invoke();
                e.Handled = true;
            }
            // Ctrl + Down -> -15pt
            else if (e.Key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                DebugSubtractPointsRequested?.Invoke();
                e.Handled = true;
            }
            // Ctrl + Q -> 終了
            else if (e.Key == Key.Q && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                DebugFinishSessionRequested?.Invoke();
                e.Handled = true;
            }
        }

        private void OnWindowMouseEnter(object sender, MouseEventArgs e)
        {
            // ホバー時にボタンを表示
            AnimateButtonOpacity(1.0);
        }

        private void OnWindowMouseLeave(object sender, MouseEventArgs e)
        {
            // マウスが離れたらボタンを非表示
            AnimateButtonOpacity(0.0);
        }

        private void AnimateButtonOpacity(double targetOpacity)
        {
            var animation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            ButtonContainer?.BeginAnimation(OpacityProperty, animation);

            // 透過切替ボタンも同じ可視状態に合わせる
            var toggleAnimation = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            TransparentToggleButton?.BeginAnimation(OpacityProperty, toggleAnimation);
        }

        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            // タスクバーからの復帰時にウィンドウを前面に
            if (WindowState == WindowState.Normal)
            {
                // 一時的にTopmostを解除してから再設定することで確実に前面に
                Topmost = false;
                Topmost = true;
                Activate();
            }
        }

        private void PositionBottomLeft()
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + 20;
            Top = area.Bottom - Height - 20;
        }

        public void SetSession(MonitoringSession session, int penaltyLevel, double liquidScale)
        {
            _session = session;
            _currentPenaltyLevel = penaltyLevel;

            UpdatePenaltyIcon();
            AnimateLiquid(liquidScale);
            UpdateTimerDisplay();
            StartTimer();
            
            // ミニモード切替時にボタンを3000ms表示
            ShowButtonsTemporarily();
        }

        public void StopTimer()
        {
            _timer?.Stop();
            StopChaosWater();
        }

        private void StartTimer()
        {
            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => UpdateTimerDisplay();
            _timer.Start();
        }

        private void UpdateTimerDisplay()
        {
            if (_session == null) return;

            double remaining = _session.RemainingSeconds;
            if (remaining <= 0)
            {
                TimeDisplay.Text = "00:00";
                return;
            }

            TimeSpan t = TimeSpan.FromSeconds(remaining);
            string timeText = t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes:D2}:{t.Seconds:D2}";

            TimeDisplay.Text = timeText;
            UpdateProgressRing(remaining);
        }

        private void UpdateProgressRing(double remaining)
        {
            if (_session == null) return;

            double totalSeconds = _session.DurationMinutes * 60;
            double progressValue = totalSeconds > 0 ? (remaining / totalSeconds) * 100 : 0;

            double angle = progressValue * 3.6;
            
            // 残り時間が0以下なら円弧を非表示
            if (remaining <= 0)
            {
                if (ArcPath != null) ArcPath.Visibility = Visibility.Collapsed;
                return;
            }
            else
            {
                if (ArcPath != null) ArcPath.Visibility = Visibility.Visible;
            }
            
            if (angle >= 360) angle = 359.99;
            if (angle <= 0) angle = 0.01;

            double radians = (angle - 90) * (Math.PI / 180);
            bool isLargeArc = angle > 180;

            double x = CenterX + RingRadius * Math.Cos(radians);
            double y = CenterY + RingRadius * Math.Sin(radians);

            if (ArcSegment != null)
            {
                ArcSegment.Point = new Point(x, y);
                ArcSegment.IsLargeArc = isLargeArc;
            }
        }

        public void UpdatePenaltyLevel(int level)
        {
            _currentPenaltyLevel = level;
            UpdatePenaltyIcon();
        }

        private void UpdatePenaltyIcon()
        {
            var (icon, _, color) = GetPenaltyInfo(_currentPenaltyLevel);
            if (PenaltyIcon != null)
            {
                PenaltyIcon.Symbol = icon;
            }
            if (LiquidWater != null)
            {
                LiquidWater.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            }

            // カオスモード（本番: レベル6で発火、それ以外で停止）
            if (_currentPenaltyLevel == 6)
            {
                StartChaosWater();
            }
            else
            {
                StopChaosWater();
            }
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
        }

        #endregion

        private (SymbolRegular icon, string name, string color) GetPenaltyInfo(int level)
        {
            // "きれいな水" から "汚染・危険の蓄積" へイメージを変更
            return level switch
            {
                // Lv1: 濁った黄緑 (酸・不快感)
                1 => (SymbolRegular.Alert24, "通知", "#CDDC39"),

                // Lv2: アンバー (警告・汚染)
                2 => (SymbolRegular.Color24, "グレースケール", "#FFC107"),

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
        public void AnimateLiquid(double targetScale)
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

        /// <summary>
        /// シェイクアニメーションを実行します
        /// </summary>
        public void Shake()
        {
            if (ShakeTransform == null) return;

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

            ShakeTransform.BeginAnimation(TranslateTransform.XProperty, shakeAnimation);
        }

        private void OnDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void OnRestoreClick(object sender, RoutedEventArgs e)
        {
            RestoreRequested?.Invoke();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void OnTransparentToggleClick(object sender, RoutedEventArgs e)
        {
            _isTransparent = !_isTransparent;
            ApplyTransparency();
        }

        private void ShowButtonsTemporarily()
        {
            // ボタンを表示
            AnimateButtonOpacity(1.0);
            // タイマーリセット
            _buttonHideTimer?.Stop();
            _buttonHideTimer?.Start();
        }

        private void ApplyTransparency()
        {
            if (_isTransparent)
            {
                // 透過モード: 背景を透明に
                OuterBorder.Background = TransparentOuterBrush;
                OuterBorder.BorderBrush = TransparentBorderBrush;
                InnerBackground.Background = TransparentInnerBrush;
                if (LiquidWater != null) LiquidWater.Opacity = 0.3;
                TransparentButtonIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.Eye24;
            }
            else
            {
                // 通常モード: 背景を不透明に
                OuterBorder.Background = NormalOuterBrush;
                OuterBorder.BorderBrush = NormalBorderBrush;
                InnerBackground.Background = NormalInnerBrush;
                if (LiquidWater != null) LiquidWater.Opacity = 1.0;
                TransparentButtonIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.EyeOff24;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // 閉じる代わりに非表示にして再利用
            e.Cancel = true;
            _timer?.Stop();
            Hide();
        }
    }
}
