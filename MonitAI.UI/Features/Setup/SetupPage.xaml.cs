using MonitAI.UI.Features.Main;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

// TextBlock の曖昧さ回避
using TextBlock = System.Windows.Controls.TextBlock;

namespace MonitAI.UI.Features.Setup
{
    /// <summary>
    /// セットアップページ。
    /// すべてのロジック（UI制御、データ保存、履歴管理、Agent連携）をこのクラス内に集約しています。
    /// </summary>
    public partial class SetupPage : Page
    {
        #region Constants & Fields

        // ユーザーデータの保存先ファイル名（UI用：お気に入り/履歴）
        private const string DataFileName = "user_data.json";

        // 履歴の最大保存数
        private const int MaxHistoryCount = 15;

        // 色変換用のブラシコンバータ（Source A/B 互換用：将来のXAML拡張に備える）
        private static readonly BrushConverter BrushConverterInstance = new();

        // SnackbarサービスとContentDialogサービス
        private readonly ISnackbarService? _snackbarService;
        private readonly IContentDialogService? _contentDialogService;

        // お気に入りと履歴のリスト
        private List<SessionItem> _favorites = new();
        private List<SessionItem> _histories = new();

        // 現在のタイマー設定値（分）
        private double _timerValue = 45;

        // Undo/Redo用の状態管理スタック（Source B 採用）
        private readonly Stack<TextState> _undoStack = new();
        private readonly Stack<TextState> _redoStack = new();
        private record TextState(string Goal, string Ng);

        // 履歴操作（Undo/Redo/QuickItem適用）中にイベント発火を防ぐフラグ
        private bool _isNavigatingHistory;

        /// <summary>
        /// 監視開始リクエストイベント（MainWindowへ通知）
        /// </summary>
        public event Action<MonitoringSession>? StartMonitoringRequested;

        #endregion

        #region Constructor & Lifecycle

        public SetupPage(ISnackbarService? snackbarService = null, IContentDialogService? contentDialogService = null)
        {
            InitializeComponent();
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUserData();
            UpdateTimerDisplay();
            RebuildFavoritesList();
            RebuildHistoryList();
            UpdateStartButton();
            UpdateHistoryButtons();

            // 初期スナップショット（空状態）を登録しておくと Undo の挙動が安定しやすい
            PushSnapshot(new TextState(GoalInput?.Text ?? "", NgInput?.Text ?? ""));
        }

        /// <summary>
        /// ページのサイズ変更時の処理（Source B 採用）
        /// </summary>
        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // プリセットボタンのレスポンシブ
            if (e.NewSize.Width < 600)
            {
                if (Preset90Button != null) Preset90Button.Visibility = Visibility.Collapsed;
                if (Preset120Button != null) Preset120Button.Visibility = Visibility.Collapsed;
                if (PresetButtonsGrid != null) PresetButtonsGrid.Columns = 3;
            }
            else
            {
                if (Preset90Button != null) Preset90Button.Visibility = Visibility.Visible;
                if (Preset120Button != null) Preset120Button.Visibility = Visibility.Visible;
                if (PresetButtonsGrid != null) PresetButtonsGrid.Columns = 5;
            }

            // 右側リストエリアの表示切替
            if (RightListArea != null && RightColumn != null)
            {
                if (e.NewSize.Width < 600)
                {
                    RightListArea.Visibility = Visibility.Collapsed;
                    RightColumn.Width = new GridLength(0);
                }
                else
                {
                    RightListArea.Visibility = Visibility.Visible;
                    RightColumn.Width = new GridLength(340);

                    // Favoritesエリアの高さを右カラムの50%までに制限（Source B）
                    if (FavoritesExpander != null)
                    {
                        double availableHeight = RightListArea.ActualHeight;
                        if (availableHeight == 0) availableHeight = e.NewSize.Height - 100;

                        if (availableHeight > 0)
                        {
                            FavoritesExpander.MaxHeight = availableHeight / 2.0;
                        }
                    }
                }
            }
        }

        #endregion

        #region UI Event Handlers

        private void GoalInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateStartButton();
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _timerValue = e.NewValue;
            UpdateTimerDisplay();
        }

        private void OnPresetTimeClick(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string s && double.TryParse(s, out double m))
            {
                _timerValue = m;
                if (TimeSlider != null) TimeSlider.Value = m;
                UpdateTimerDisplay();
            }
        }

        /// <summary>
        /// Start押下：UI履歴保存 + 監視セッション生成 + Agent連携（Source Aのビジネスロジックを保持）
        /// </summary>
        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GoalInput?.Text))
            {
                // UX: 何も起きないよりフラッシュで気づかせる
                FlashInputControls();
                return;
            }

            string goalText = GoalInput.Text;
            string ngText = NgInput?.Text ?? string.Empty;

            // 履歴へ追加
            var newItem = new SessionItem
            {
                Title = goalText,
                NgText = ngText,
                Minutes = (int)_timerValue,
                Timestamp = DateTime.Now.ToString("MM/dd HH:mm")
            };

            _histories.Insert(0, newItem);
            if (_histories.Count > MaxHistoryCount)
                _histories.RemoveAt(_histories.Count - 1);

            SaveUserData();
            RebuildHistoryList();

            var session = new MonitoringSession
            {
                IsActive = true,
                StartTime = DateTime.Now,
                DurationMinutes = _timerValue,
                Goal = goalText,
                NgItem = ngText
            };

            try
            {
                // ===== Source A: Agent連携ロジック（削除厳禁）=====
                UpdateAgentConfig(goalText, ngText, session.StartTime, session.DurationMinutes);
                StartAgentProcess();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error starting agent: {ex.Message}");
                _snackbarService?.Show(
                    "エラー",
                    $"Agent起動に失敗しました: {ex.Message}",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24),
                    TimeSpan.FromSeconds(4));
            }

            StartMonitoringRequested?.Invoke(session);
        }

        private void Input_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isNavigatingHistory) return;

            var currentState = new TextState(GoalInput?.Text ?? "", NgInput?.Text ?? "");
            if (_undoStack.Count > 0 && _undoStack.Peek() == currentState) return;

            PushSnapshot(currentState);
        }

        private void OnQuickItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is SessionItem item)
                ApplyQuickItem(item);
        }

        private void OnFavoriteToggleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is SessionItem item)
                ToggleFavorite(item);
        }

        private void OnDeleteHistoryItemClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is SessionItem item)
            {
                _histories.Remove(item);
                SaveUserData();
                RebuildHistoryList();
            }
        }

        private async void OnDeleteAllHistoryClick(object sender, RoutedEventArgs e)
        {
            if (_contentDialogService != null)
            {
                var result = await _contentDialogService.ShowSimpleDialogAsync(
                    new SimpleContentDialogCreateOptions
                    {
                        Title = "履歴の全削除",
                        Content = "すべての履歴を削除しますか？この操作は取り消せません。",
                        PrimaryButtonText = "削除",
                        CloseButtonText = "キャンセル"
                    });

                if (result == ContentDialogResult.Primary)
                {
                    _histories.Clear();
                    SaveUserData();
                    RebuildHistoryList();
                }
            }
            else
            {
                _histories.Clear();
                SaveUserData();
                RebuildHistoryList();
            }
        }

        #endregion

        #region Timer UI

        private void UpdateTimerDisplay()
        {
            if (TimeDisplay != null)
            {
                TimeDisplay.FontFamily = new FontFamily("Segoe UI");
                TimeDisplay.Text = ((int)_timerValue).ToString("D3").TrimStart('0').PadLeft(3, ' ');
            }

            if (EndTimeText != null)
            {
                EndTimeText.Text = $"End {DateTime.Now.AddMinutes(_timerValue):HH:mm}";
            }
        }

        private void UpdateStartButton()
        {
            if (StartButton == null) return;

            bool canStart = !string.IsNullOrWhiteSpace(GoalInput?.Text);
            StartButton.IsEnabled = canStart;
            StartButton.Content = canStart ? "活動を開始！" : "目標を入力してください";
        }

        #endregion

        #region Undo/Redo (Source B)

        private void PushSnapshot(TextState state)
        {
            _undoStack.Push(state);
            _redoStack.Clear();
            UpdateHistoryButtons();
        }

        private void UpdateHistoryButtons()
        {
            if (UndoButton != null) UndoButton.IsEnabled = _undoStack.Count > 0;
            if (RedoButton != null) RedoButton.IsEnabled = _redoStack.Count > 0;
        }

        private void OnUndoClick(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count == 0) return;

            var currentState = new TextState(GoalInput?.Text ?? "", NgInput?.Text ?? "");
            _redoStack.Push(currentState);

            var previousState = _undoStack.Pop();
            ApplyTextState(previousState);

            UpdateHistoryButtons();
        }

        private void OnRedoClick(object sender, RoutedEventArgs e)
        {
            if (_redoStack.Count == 0) return;

            var currentState = new TextState(GoalInput?.Text ?? "", NgInput?.Text ?? "");
            _undoStack.Push(currentState);

            var nextState = _redoStack.Pop();
            ApplyTextState(nextState);

            UpdateHistoryButtons();
        }

        private void ApplyTextState(TextState state)
        {
            _isNavigatingHistory = true;
            try
            {
                if (GoalInput != null) GoalInput.Text = state.Goal;
                if (NgInput != null) NgInput.Text = state.Ng;
                UpdateStartButton();
                FlashInputControls();
            }
            finally
            {
                _isNavigatingHistory = false;
            }
        }

        #endregion

        #region User Data (Favorites/History persistence)

        public void LoadUserData()
        {
            try
            {
                if (File.Exists(DataFileName))
                {
                    var json = File.ReadAllText(DataFileName);
                    var data = JsonSerializer.Deserialize<AppData>(json);
                    if (data != null)
                    {
                        _favorites = data.Favorites ?? new List<SessionItem>();
                        _histories = data.Histories ?? new List<SessionItem>();
                    }
                }
                else
                {
                    _favorites.Add(new SessionItem { Title = "英単語暗記", NgText = "Youtube", Minutes = 30 });
                    _histories.Add(new SessionItem { Title = "読書", Minutes = 45, Timestamp = "サンプル" });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadUserData Error: {ex.Message}");
                _favorites = new List<SessionItem>();
                _histories = new List<SessionItem>();
            }
        }

        public void SaveUserData()
        {
            try
            {
                var data = new AppData
                {
                    Favorites = _favorites,
                    Histories = _histories.Where(h => !h.IsTransient).ToList()
                };
                var json = JsonSerializer.Serialize(data);
                File.WriteAllText(DataFileName, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveUserData Error: {ex.Message}");
            }
        }

        private void ToggleFavorite(SessionItem item)
        {
            if (_favorites.Contains(item))
            {
                _favorites.Remove(item);
                item.IsTransient = true;
                _histories.Insert(0, item);
            }
            else if (_histories.Contains(item))
            {
                _histories.Remove(item);
                item.IsTransient = false;
                _favorites.Add(item);
            }

            SaveUserData();
            RebuildFavoritesList();
            RebuildHistoryList();
        }

        #endregion

        #region Dynamic list UI

        private void RebuildFavoritesList()
        {
            if (FavoritesList == null) return;

            FavoritesList.Items.Clear();
            foreach (var item in _favorites)
            {
                FavoritesList.Items.Add(CreateFavoriteItemControl(item));
            }
        }

        private void RebuildHistoryList()
        {
            if (HistoryList == null) return;

            HistoryList.Items.Clear();
            foreach (var item in _histories)
            {
                HistoryList.Items.Add(CreateHistoryItemControl(item));
            }
        }

        private (Border Card, Grid Grid, Grid ActionArea) CreateBaseItemVisuals(SessionItem item)
        {
            var cardBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1)
            };

            // SetResourceReference を使用してテーマ変更に追従させる
            cardBorder.SetResourceReference(Border.BackgroundProperty, "ControlFillColorSecondaryBrush");
            cardBorder.SetResourceReference(Border.BorderBrushProperty, "SurfaceStrokeColorDefaultBrush");

            // カード全体のホバー処理は削除し、各ボタン要素（mainButton、アイコンエリア）の
            // Wpf.Ui 標準ホバースタイルに任せる（ダークモードと同じ動作に統一）

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            // アイテムエリア: お気に入り/削除ボタンと同じ方式（Border + MouseLeftButtonDown）で統一
            var mainItemArea = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(6, 0, 0, 6),
                Tag = item,
                ToolTip = item.Title
            };
            mainItemArea.MouseLeftButtonDown += (s, e) =>
            {
                if (s is FrameworkElement el && el.Tag is SessionItem sessionItem)
                {
                    ApplyQuickItem(sessionItem);
                }
            };
            mainItemArea.MouseEnter += (s, e) =>
            {
                if (s is Border b)
                {
                    b.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");
                }
            };
            mainItemArea.MouseLeave += (s, e) =>
            {
                if (s is Border b)
                {
                    b.Background = Brushes.Transparent;
                }
            };

            var contentGrid = new Grid { Margin = new Thickness(12, 8, 4, 8) };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var timeText = new TextBlock
            {
                Text = $"{item.Minutes}m",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            // 修正: Foregroundを動的参照化
            timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");

            Grid.SetColumn(timeText, 0);
            contentGrid.Children.Add(timeText);

            var stackPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            if (!string.IsNullOrEmpty(item.Timestamp))
            {
                var timestampBlock = new TextBlock
                {
                    Text = item.Timestamp,
                    FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 2)
                };
                // 修正: Foregroundを動的参照化
                timestampBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
                stackPanel.Children.Add(timestampBlock);
            }

            stackPanel.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontWeight = FontWeights.Medium,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
                // メインテキストは標準色を継承するため設定不要ですが、明示するなら以下を追加
                // Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"] 
                // ただしSetResourceReference推奨
            });

            if (!string.IsNullOrEmpty(item.NgText))
            {
                var ngStack = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };

                var prohibitedIcon = new SymbolIcon
                {
                    Symbol = SymbolRegular.Prohibited12,
                    FontSize = 10,
                    Margin = new Thickness(0, 1, 4, 0)
                };
                // 修正: Foregroundを動的参照化
                prohibitedIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "TextFillColorTertiaryBrush");
                ngStack.Children.Add(prohibitedIcon);

                var ngTextBlock = new TextBlock
                {
                    Text = item.NgText,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                // 修正: Foregroundを動的参照化
                ngTextBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
                ngStack.Children.Add(ngTextBlock);

                stackPanel.Children.Add(ngStack);
            }

            Grid.SetColumn(stackPanel, 1);
            contentGrid.Children.Add(stackPanel);

            mainItemArea.Child = contentGrid;
            Grid.SetColumn(mainItemArea, 0);
            grid.Children.Add(mainItemArea);

            var separator = new Border
            {
                Width = 1,
                Margin = new Thickness(0, 8, 0, 8)
            };
            // 修正: Backgroundを動的参照化
            separator.SetResourceReference(Border.BackgroundProperty, "SurfaceStrokeColorDefaultBrush");

            Grid.SetColumn(separator, 1);
            grid.Children.Add(separator);

            var actionArea = new Grid();
            Grid.SetColumn(actionArea, 2);
            grid.Children.Add(actionArea);

            cardBorder.Child = grid;
            return (cardBorder, grid, actionArea);
        }

        private UIElement CreateFavoriteItemControl(SessionItem item)
        {
            var visuals = CreateBaseItemVisuals(item);

            var favClickableArea = CreateClickableIconArea(
                item,
                "お気に入り解除",
                new SymbolIcon
                {
                    Symbol = SymbolRegular.Star24,
                    FontSize = 16,
                    Filled = true,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFA, 0xCD, 0x15))
                },
                OnFavoriteToggleClick);

            visuals.ActionArea.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(0, 6, 6, 0),
                Background = Brushes.Transparent,
                Child = favClickableArea
            });

            return visuals.Card;
        }

        private UIElement CreateHistoryItemControl(SessionItem item)
        {
            var visuals = CreateBaseItemVisuals(item);

            var actionsPanel = new UniformGrid { Columns = 1, Rows = 2, Margin = new Thickness(0, 2, 0, 2) };

            // 削除ボタン
            var deleteIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.Dismiss16,
                FontSize = 14
            };
            // 修正: アイコン色を動的参照化
            deleteIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "TextFillColorTertiaryBrush");

            actionsPanel.Children.Add(CreateClickableIconArea(
                item,
                "削除",
                deleteIcon,
                OnDeleteHistoryItemClick));

            // お気に入り追加ボタン（未登録状態）
            var addFavIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.Star24,
                FontSize = 16,
                Filled = false
            };
            // 修正: アイコン色を動的参照化
            addFavIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "TextFillColorTertiaryBrush");

            actionsPanel.Children.Add(CreateClickableIconArea(
                item,
                "お気に入りに追加",
                addFavIcon,
                OnFavoriteToggleClick));

            visuals.ActionArea.Children.Add(actionsPanel);
            return visuals.Card;
        }

        private Grid CreateClickableIconArea(SessionItem item, string tooltip, SymbolIcon icon, MouseButtonEventHandler onClick)
        {
            var grid = new Grid
            {
                Tag = item,
                ToolTip = tooltip,
                Background = Brushes.Transparent,
            };

            grid.MouseLeftButtonDown += onClick;
            grid.MouseEnter += (s, e) =>
            {
                if (s is Grid g)
                {
                    // SetResourceReference でテーマ追従するホバー背景を設定
                    g.SetResourceReference(Panel.BackgroundProperty, "ControlFillColorDefaultBrush");
                }
            };
            grid.MouseLeave += (s, e) =>
            {
                if (s is Grid g)
                {
                    g.Background = Brushes.Transparent;
                }
            };

            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(icon);

            return grid;
        }
        #endregion

        #region UX Effects (Flash animation)

        private async void FlashInputControls()
        {
            if (GoalInput == null || NgInput == null) return;

            var originalGoalBrush = GoalInput.Background;
            var originalNgBrush = NgInput.Background;

            var targetColor = Colors.Transparent;
            if (originalGoalBrush is SolidColorBrush solidBrush)
                targetColor = solidBrush.Color;

            var flashColor = Color.FromArgb(120, 0x60, 0xCD, 0xFF);
            var animationBrush = new SolidColorBrush(flashColor);

            GoalInput.Background = animationBrush;
            NgInput.Background = animationBrush;

            var colorAnimation = new ColorAnimation
            {
                From = flashColor,
                To = targetColor,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            animationBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnimation);
            await Task.Delay(850);

            // アニメーション後は SetResourceReference で動的リソースを再設定（テーマ変更に追従）
            GoalInput.SetResourceReference(Control.BackgroundProperty, "ControlFillColorSecondaryBrush");
            NgInput.SetResourceReference(Control.BackgroundProperty, "ControlFillColorSecondaryBrush");
        }

        private void ApplyQuickItem(SessionItem item)
        {
            // 適用前に現在の状態をUndoスタックへ保存
            var currentState = new TextState(GoalInput?.Text ?? "", NgInput?.Text ?? "");
            if (_undoStack.Count == 0 || _undoStack.Peek() != currentState)
            {
                PushSnapshot(currentState);
            }

            _isNavigatingHistory = true;
            try
            {
                if (GoalInput != null) GoalInput.Text = item.Title ?? string.Empty;
                if (NgInput != null) NgInput.Text = item.NgText ?? string.Empty;

                _timerValue = item.Minutes;
                if (TimeSlider != null) TimeSlider.Value = _timerValue;

                UpdateTimerDisplay();
                UpdateStartButton();

                FlashInputControls();
            }
            finally
            {
                _isNavigatingHistory = false;
            }
        }

        #endregion

        #region Source A: Agent integration (must keep)

        private void UpdateAgentConfig(string goal, string ng, DateTime startTime, double durationMinutes)
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);

            string configPath = Path.Combine(appData, "config.json");

            Dictionary<string, string> settings = new();
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
                catch { }
            }

            settings["Rules"] = $"目標: {goal}\n禁止: {ng}";

            // 状態リセット（前回のポイント引き継ぎ防止）
            string statusPath = Path.Combine(appData, "status.json");
            if (File.Exists(statusPath)) File.Delete(statusPath);

            string logPath = Path.Combine(appData, "agent_log.txt");
            if (File.Exists(logPath)) File.Delete(logPath);

            // API Key & Mode & Model は SettingsPage で設定するため、ここでは上書きしない
            if (!settings.ContainsKey("ApiKey")) settings["ApiKey"] = "";
            if (!settings.ContainsKey("UseApi")) settings["UseApi"] = "False";
            if (!settings.ContainsKey("Model")) settings["Model"] = "gemini-2.5-flash";


            // StartTime
            settings["StartTime"] = startTime.ToString("o"); // ISO 8601

            // EndTime
            DateTime endTime = startTime.AddMinutes(durationMinutes);
            settings["EndTime"] = endTime.ToString("o"); // ISO 8601

            // 常に正しいパスで上書き（現状仕様）
            string appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            settings["CliPath"] = Path.Combine(appDataRoaming, @"npm\gemini.cmd");

            if (!settings.ContainsKey("Model")) settings["Model"] = "gemini-2.5-flash";

            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void StartAgentProcess()
        {
            string? agentPath = GetAgentPath();

            if (!string.IsNullOrEmpty(agentPath) && File.Exists(agentPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = agentPath,
                    UseShellExecute = true
                });
            }
            else
            {
                Debug.WriteLine($"Agent not found at: {agentPath}");
                _snackbarService?.Show(
                    "エラー",
                    "Agentが見つかりませんでした。",
                    ControlAppearance.Danger,
                    new SymbolIcon(SymbolRegular.ErrorCircle24),
                    TimeSpan.FromSeconds(3));
            }
        }

        private string? GetSolutionRoot()
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "MonitAI_System.sln")))
                {
                    return current;
                }
                current = Directory.GetParent(current)?.FullName ?? "";
            }
            return null;
        }

        private string? GetAgentPath()
        {
            string? solutionRoot = GetSolutionRoot();
            if (string.IsNullOrEmpty(solutionRoot)) return null;

            string configName = "Release";
#if DEBUG
            configName = "Debug";
#endif
            string frameworkName = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory).Name;
            return Path.Combine(solutionRoot, "MonitAI.Agent", "bin", configName, frameworkName, "MonitAI.Agent.exe");
        }

        #endregion
    }

    #region Models (Source B adopted)

    public class SessionItem
    {
        public string? Title { get; set; }
        public string? NgText { get; set; }
        public int Minutes { get; set; }
        public string? Timestamp { get; set; }

        // ファイルには保存しない一時フラグ
        public bool IsTransient { get; set; } = false;
    }

    public class AppData
    {
        public List<SessionItem> Favorites { get; set; } = new();
        public List<SessionItem> Histories { get; set; } = new();
    }

    #endregion
}