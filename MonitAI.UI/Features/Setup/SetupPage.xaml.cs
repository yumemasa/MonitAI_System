using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;
using MonitAI.UI.Features.Main;
using TextBlock = System.Windows.Controls.TextBlock;

namespace MonitAI.UI.Features.Setup
{
    /// <summary>
    /// セットアップページ。すべてのロジックをコードビハインドに統合。
    /// </summary>
    public partial class SetupPage : Page
    {
        // ユーザーデータの保存先ファイル名
        private const string DataFileName = "user_data.json";

        // 履歴の最大保存数
        private const int MaxHistoryCount = 15;

        // 色変換用のブラシコンバータ
        private static readonly BrushConverter BrushConverterInstance = new();

        // SnackbarサービスとContentDialogサービス
        private readonly ISnackbarService? _snackbarService;
        private readonly IContentDialogService? _contentDialogService;

        // お気に入りと履歴のリスト
        private List<SessionItem> _favorites = new List<SessionItem>(); // お気に入りリスト
        private List<SessionItem> _histories = new List<SessionItem>(); // 履歴リスト

        private double _timerValue = 45; // タイマーの初期値（分）
        private SessionItem? _undoBackup; // 元に戻す操作用のバックアップ

        /// <summary>
        /// 監視開始リクエストイベント。
        /// </summary>
        public event Action<MonitoringSession>? StartMonitoringRequested;

        /// <summary>
        /// SetupPageのコンストラクタ。
        /// </summary>
        /// <param name="snackbarService">Snackbarサービスのインスタンス</param>
        /// <param name="contentDialogService">ContentDialogサービスのインスタンス</param>
        public SetupPage(ISnackbarService? snackbarService = null, IContentDialogService? contentDialogService = null)
        {
            InitializeComponent();
            _snackbarService = snackbarService;
            _contentDialogService = contentDialogService;
        }

        /// <summary>
        /// ページがロードされたときの処理。
        /// </summary>
        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadUserData(); // ユーザーデータを読み込む
            UpdateTimerDisplay(); // タイマー表示を更新
            RebuildFavoritesList(); // お気に入りリストを再構築
            RebuildHistoryList(); // 履歴リストを再構築
        }

        /// <summary>
        /// ページのサイズ変更時の処理。
        /// </summary>
        /// <param name="e">サイズ変更イベントの引数</param>
        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            //幅が狭いときはプリセットボタンを減らす
            if (e.NewSize.Width < 600)
            {
                if (Preset90Button != null) Preset90Button.Visibility = Visibility.Collapsed; //90分ボタンを非表示
                if (Preset120Button != null) Preset120Button.Visibility = Visibility.Collapsed; //120分ボタンを非表示
                if (PresetButtonsGrid != null) PresetButtonsGrid.Columns = 3; // ボタン列数を3に設定
            }
            else
            {
                if (Preset90Button != null) Preset90Button.Visibility = Visibility.Visible; //90分ボタンを表示
                if (Preset120Button != null) Preset120Button.Visibility = Visibility.Visible; //120分ボタンを表示
                if (PresetButtonsGrid != null) PresetButtonsGrid.Columns = 5; // ボタン列数を5に設定
            }

            // 右側リストエリアの表示・カラム幅切り替え（例：900px未満で非表示＆幅0）
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
                }
            }
        }

        /// <summary>
        /// 目標入力欄のテキスト変更時の処理。
        /// </summary>
        /// <param name="e">テキスト変更イベントの引数</param>
        private void GoalInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateStartButton(); // スタートボタンの状態を更新
        }

        /// <summary>
        /// タイマースライダーの値変更時の処理。
        /// </summary>
        /// <param name="e">値変更イベントの引数</param>
        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _timerValue = e.NewValue; // タイマー値を更新
            UpdateTimerDisplay(); // タイマー表示を更新
        }

        /// <summary>
        /// タイマー表示を更新する。
        /// </summary>
        private void UpdateTimerDisplay()
        {
            if (TimeDisplay != null)
            {
                // 元の実装に戻す
                TimeDisplay.FontFamily = new FontFamily("Segoe UI"); // デフォルトフォントに戻す
                TimeDisplay.Text = ((int)_timerValue).ToString("D3").TrimStart('0').PadLeft(3, ' '); //先頭の0のみスペースに置き換え
            }
            if (EndTimeText != null)
            {
                // 終了時刻を表示
                EndTimeText.Text = $"End {DateTime.Now.AddMinutes(_timerValue):HH:mm}";
            }
        }

        /// <summary>
        /// スタートボタンの状態を更新する。
        /// </summary>
        private void UpdateStartButton()
        {
            if (StartButton != null)
            {
                bool canStart = !string.IsNullOrWhiteSpace(GoalInput?.Text); //目標が入力されているか確認
                StartButton.IsEnabled = canStart; // ボタンの有効/無効を設定
                StartButton.Content = canStart ? "集中を開始 (Start)" : "目標を入力してください"; // ボタンのテキストを設定
            }
        }

        /// <summary>
        /// プリセットボタンがクリックされたときの処理。
        /// </summary>
        /// <param name="e">クリックイベントの引数</param>
        private void OnPresetTimeClick(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string s && double.TryParse(s, out double m))
            {
                _timerValue = m; // タイマー値をプリセット値に設定
                if (TimeSlider != null)
                {
                    TimeSlider.Value = m; // スライダーの値を更新
                }
                UpdateTimerDisplay(); // タイマー表示を更新
            }
        }

        /// <summary>
        /// スタートボタンがクリックされたときの処理。
        /// </summary>
        /// <param name="e">クリックイベントの引数</param>
        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GoalInput?.Text))
            {
                return; //目標が入力されていない場合は処理を中断
            }

            string goalText = GoalInput.Text; //目標テキストを取得
            string ngText = NgInput?.Text ?? string.Empty; // 禁止事項テキストを取得

            var newItem = new SessionItem
            {
                Title = goalText,
                NgText = ngText,
                Minutes = (int)_timerValue,
                Timestamp = DateTime.Now.ToString("MM/dd HH:mm")
            };

            _histories.Insert(0, newItem); // 履歴リストの先頭に追加
            if (_histories.Count > MaxHistoryCount)
            {
                _histories.RemoveAt(_histories.Count - 1); // 履歴が最大数を超えた場合は古い項目を削除
            }

            SaveUserData(); // ユーザーデータを保存
            RebuildHistoryList(); // 履歴リストを再構築

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
                UpdateAgentConfig(goalText, ngText, session.StartTime, session.DurationMinutes);
                UpdateServiceConfig(session.StartTime, session.DurationMinutes);
                StartAgentProcess();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting agent: {ex.Message}");
            }

            // 監視開始を通知
            StartMonitoringRequested?.Invoke(session);
        }

        private void UpdateAgentConfig(string goal, string ng, DateTime startTime, double durationMinutes)
        {
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "screenShot2");
            if (!Directory.Exists(appData)) Directory.CreateDirectory(appData);
            string configPath = Path.Combine(appData, "config.json");

            Dictionary<string, string> settings = new Dictionary<string, string>();
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
            
            // API Key & Mode & Model は SettingsPage で設定するため、ここでは上書きしない
            // ただし、初回起動時などでキーが存在しない場合はデフォルト値を入れる
            if (!settings.ContainsKey("ApiKey")) settings["ApiKey"] = "";
            if (!settings.ContainsKey("UseApi")) settings["UseApi"] = "False";
            if (!settings.ContainsKey("Model")) settings["Model"] = "gemini-2.5-flash-lite";

            // EndTime for Agent auto-stop
            DateTime endTime = startTime.AddMinutes(durationMinutes);
            settings["EndTime"] = endTime.ToString("o"); // ISO 8601 format

            // 常に正しいパスで上書きする (動的パスに変更)
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            settings["CliPath"] = Path.Combine(userProfile, @"AppData\Roaming\npm\gemini.cmd");
            
            if (!settings.ContainsKey("Model")) settings["Model"] = "gemini-2.5-flash-lite";

            File.WriteAllText(configPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void UpdateServiceConfig(DateTime startTime, double durationMinutes)
        {
            // 動的パスに変更 (リポジトリルートを探す簡易的なロジック)
            // 注意: 開発環境と本番環境でパス構成が異なる場合は、設定ファイルやレジストリで管理するのが望ましいが、
            // ここではユーザープロファイル配下の source\repos を基準にする。
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string serviceConfigPath = Path.Combine(userProfile, @"source\repos\MonitAI_System\MonitAIサービス完成版\settings.json");
            
            DateTime endTime = startTime.AddMinutes(durationMinutes);

            string agentPath = Path.Combine(userProfile, @"source\repos\MonitAI_System\MonitAI.Agent\bin\Debug\net8.0-windows\MonitAI.Agent.exe");

            var config = new
            {
                Monitoring = new
                {
                    StartDate = startTime.ToString("yyyy-MM-dd"),
                    EndDate = endTime.ToString("yyyy-MM-dd"),
                    StartTime = startTime.ToString("HH:mm"),
                    EndTime = endTime.ToString("HH:mm"),
                    ProcessName = "MonitAI.Agent",
                    ProcessPath = agentPath
                }
            };

            File.WriteAllText(serviceConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void StartAgentProcess()
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string agentPath = Path.Combine(userProfile, @"source\repos\MonitAI_System\MonitAI.Agent\bin\Debug\net8.0-windows\MonitAI.Agent.exe");
            
            if (File.Exists(agentPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = agentPath,
                    UseShellExecute = true
                });
            }
        }

        /// <summary>
        /// クイックアイテムがクリックされたときの処理。
        /// </summary>
        /// <param name="e">クリックイベントの引数</param>
        private void OnQuickItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is SessionItem item)
            {
                ApplyQuickItem(item); // クイックアイテムを適用
            }
        }

        /// <summary>
        /// クイックアイテムを適用する。
        /// </summary>
        /// <param name="item">適用するセッションアイテム</param>
        private void ApplyQuickItem(SessionItem item)
        {
            _undoBackup = new SessionItem
            {
                Title = GoalInput?.Text ?? string.Empty,
                NgText = NgInput?.Text ?? string.Empty,
                Minutes = (int)_timerValue
            };

            if (GoalInput != null)
            {
                GoalInput.Text = item.Title ?? string.Empty; //目標テキストを設定
            }
            if (NgInput != null)
            {
                NgInput.Text = item.NgText ?? string.Empty; // 禁止事項テキストを設定
            }
            _timerValue = item.Minutes; // タイマー値を設定
            if (TimeSlider != null)
            {
                TimeSlider.Value = _timerValue; // スライダーの値を更新
            }
            UpdateTimerDisplay(); // タイマー表示を更新
            UpdateStartButton(); // スタートボタンの状態を更新

            ShowSnackbar("設定を復元しました"); // スナックバーに通知を表示
        }

        /// <summary>
        /// お気に入りのトグルボタンがクリックされたときの処理。
        /// </summary>
        /// <param name="e">クリックイベントの引数</param>
        private void OnFavoriteToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is SessionItem item)
            {
                ToggleFavorite(item); // お気に入りの状態を切り替え
            }
        }

        /// <summary>
        /// お気に入りの状態を切り替える。
        /// </summary>
        /// <param name="item">切り替えるセッションアイテム</param>
        private void ToggleFavorite(SessionItem item)
        {
            if (_favorites.Contains(item))
            {
                // お気に入りから削除して履歴に追加
                _favorites.Remove(item);
                item.IsTransient = true;
                _histories.Insert(0, item);
            }
            else if (_histories.Contains(item))
            {
                // 履歴から削除してお気に入りに追加
                _histories.Remove(item);
                item.IsTransient = false;
                _favorites.Add(item);
            }

            SaveUserData(); // ユーザーデータを保存
            RebuildFavoritesList(); // お気に入りリストを再構築
            RebuildHistoryList(); // 履歴リストを再構築
        }

        /// <summary>
        /// 履歴アイテムの削除ボタンがクリックされたときの処理。
        /// </summary>
        /// <param name="e">クリックイベントの引数</param>
        private void OnDeleteHistoryItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is SessionItem item)
            {
                _histories.Remove(item); // 履歴リストから削除
                SaveUserData(); // ユーザーデータを保存
                RebuildHistoryList(); // 履歴リストを再構築
            }
        }

        /// <summary>
        /// 履歴をすべて削除するボタンがクリックされたときの処理。
        /// </summary>
        /// <param name="e">クリックイベントの引数</param>
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
                    _histories.Clear(); // 履歴リストをクリア
                    SaveUserData(); // ユーザーデータを保存
                    RebuildHistoryList(); // 履歴リストを再構築
                }
            }
            else
            {
                _histories.Clear(); // 履歴リストをクリア
                SaveUserData(); // ユーザーデータを保存
                RebuildHistoryList(); // 履歴リストを再構築
            }
        }

        /// <summary>
        /// お気に入りリストを再構築する。
        /// </summary>
        private void RebuildFavoritesList()
        {
            if (FavoritesList == null) return;

            FavoritesList.Items.Clear(); // リストをクリア
            foreach (var item in _favorites)
            {
                FavoritesList.Items.Add(CreateFavoriteItemControl(item)); // お気に入りアイテムを追加
            }
        }

        /// <summary>
        /// 履歴リストを再構築する。
        /// </summary>
        private void RebuildHistoryList()
        {
            if (HistoryList == null) return;

            HistoryList.Items.Clear(); // リストをクリア
            foreach (var item in _histories)
            {
                HistoryList.Items.Add(CreateHistoryItemControl(item)); // 履歴アイテムを追加
            }
        }

        /// <summary>
        /// お気に入りアイテムのUIコントロールを作成する。
        /// </summary>
        private UIElement CreateFavoriteItemControl(SessionItem item)
        {
            var cardBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"]
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var mainButton = new Wpf.Ui.Controls.Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Appearance = ControlAppearance.Transparent,
                Tag = item,
                ToolTip = item.Title,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 4, 8),
                CornerRadius = new CornerRadius(6, 0, 0, 6),
                BorderThickness = new Thickness(0)
            };
            mainButton.Click += OnQuickItemClick;

            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var timeText = new TextBlock
            {
                Text = $"{item.Minutes}m",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            Grid.SetColumn(timeText, 0);
            contentGrid.Children.Add(timeText);

            var stackPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var titleText = new TextBlock
            {
                Text = item.Title,
                FontWeight = FontWeights.Medium,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stackPanel.Children.Add(titleText);

            if (!string.IsNullOrEmpty(item.NgText))
            {
                var ngStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                var ngIcon = new SymbolIcon
                {
                    Symbol = SymbolRegular.Prohibited12,
                    FontSize = 10,
                    Margin = new Thickness(0, 1, 4, 0),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                };
                var ngText = new TextBlock
                {
                    Text = item.NgText,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                };
                ngStack.Children.Add(ngIcon);
                ngStack.Children.Add(ngText);
                stackPanel.Children.Add(ngStack);
            }

            Grid.SetColumn(stackPanel, 1);
            contentGrid.Children.Add(stackPanel);
            mainButton.Content = contentGrid;
            Grid.SetColumn(mainButton, 0);
            grid.Children.Add(mainButton);

            var separator = new Border
            {
                Width = 1,
                Background = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"],
                Margin = new Thickness(0, 8, 0, 8)
            };
            Grid.SetColumn(separator, 1);
            grid.Children.Add(separator);

            var favClickableArea = new Grid
            {
                Tag = item,
                ToolTip = "お気に入り解除",
                Background = Brushes.Transparent
            };
            var favBorder = new Border
            {
                CornerRadius = new CornerRadius(0, 6, 6, 0),
                Background = Brushes.Transparent,
                Child = favClickableArea
            };
            favClickableArea.MouseLeftButtonDown += OnFavoriteToggleClick;
            favClickableArea.MouseEnter += ClickableArea_MouseEnter;
            favClickableArea.MouseLeave += ClickableArea_MouseLeave;

            var favIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.Star24,
                FontSize = 16,
                Filled = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFA, 0xCD, 0x15))
            };

            favClickableArea.Children.Add(favIcon);
            Grid.SetColumn(favBorder, 2);
            grid.Children.Add(favBorder);

            cardBorder.Child = grid;
            return cardBorder;
        }

        /// <summary>
        /// 履歴アイテムのUIコントロールを作成する。
        /// </summary>
        private UIElement CreateHistoryItemControl(SessionItem item)
        {
            var cardBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"]
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

            var mainButton = new Wpf.Ui.Controls.Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Appearance = ControlAppearance.Transparent,
                Tag = item,
                ToolTip = item.Title,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 4, 8),
                CornerRadius = new CornerRadius(6, 0, 0, 6),
                BorderThickness = new Thickness(0)
            };
            mainButton.Click += OnQuickItemClick;

            var contentGrid = new Grid();
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var timeText = new TextBlock
            {
                Text = $"{item.Minutes}m",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            Grid.SetColumn(timeText, 0);
            contentGrid.Children.Add(timeText);

            var stackPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var timestampText = new TextBlock
            {
                Text = item.Timestamp,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 2),
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            };
            stackPanel.Children.Add(timestampText);

            var titleText = new TextBlock
            {
                Text = item.Title,
                FontWeight = FontWeights.Medium,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stackPanel.Children.Add(titleText);

            if (!string.IsNullOrEmpty(item.NgText))
            {
                var ngStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                var ngIcon = new SymbolIcon
                {
                    Symbol = SymbolRegular.Prohibited12,
                    FontSize = 10,
                    Margin = new Thickness(0, 1, 4, 0),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                };
                var ngText = new TextBlock
                {
                    Text = item.NgText,
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
                };
                ngStack.Children.Add(ngIcon);
                ngStack.Children.Add(ngText);
                stackPanel.Children.Add(ngStack);
            }

            Grid.SetColumn(stackPanel, 1);
            contentGrid.Children.Add(stackPanel);
            mainButton.Content = contentGrid;
            Grid.SetColumn(mainButton, 0);
            grid.Children.Add(mainButton);

            var separator = new Border
            {
                Width = 1,
                Background = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"],
                Margin = new Thickness(0, 8, 0, 8)
            };
            Grid.SetColumn(separator, 1);
            grid.Children.Add(separator);

            var actionsPanel = new UniformGrid { Columns = 1, Rows = 2, Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetColumn(actionsPanel, 2);

            var deleteClickableArea = new Grid
            {
                Tag = item,
                ToolTip = "削除",
                Background = Brushes.Transparent
            };
            deleteClickableArea.MouseLeftButtonDown += OnDeleteHistoryItemClick;
            deleteClickableArea.MouseEnter += ClickableArea_MouseEnter;
            deleteClickableArea.MouseLeave += ClickableArea_MouseLeave;
            deleteClickableArea.Children.Add(new SymbolIcon
            {
                Symbol = SymbolRegular.Dismiss16,
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
            actionsPanel.Children.Add(deleteClickableArea);

            var toggleClickableArea = new Grid
            {
                Tag = item,
                ToolTip = "お気に入りに追加",
                Background = Brushes.Transparent
            };
            toggleClickableArea.MouseLeftButtonDown += OnFavoriteToggleClick;
            toggleClickableArea.MouseEnter += ClickableArea_MouseEnter;
            toggleClickableArea.MouseLeave += ClickableArea_MouseLeave;
            toggleClickableArea.Children.Add(new SymbolIcon
            {
                Symbol = SymbolRegular.Star24,
                FontSize = 16,
                Filled = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            });
            actionsPanel.Children.Add(toggleClickableArea);

            grid.Children.Add(actionsPanel);
            cardBorder.Child = grid;
            return cardBorder;
        }

        /// <summary>
        /// マウスオーバー時の処理。
        /// </summary>
        private void ClickableArea_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
            }
        }

        /// <summary>
        /// マウスアウト時の処理。
        /// </summary>
        private void ClickableArea_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is Grid grid)
            {
                grid.Background = Brushes.Transparent;
            }
        }

        /// <summary>
        /// スナックバーにメッセージを表示する。
        /// </summary>
        private void ShowSnackbar(string message)
        {
            if (_snackbarService is Wpf.Ui.SnackbarService snackbarService)
            {
                var presenter = snackbarService.GetSnackbarPresenter();
                if (presenter != null)
                {
                    var undoButton = new Wpf.Ui.Controls.Button
                    {
                        Content = "元に戻す",
                        Appearance = ControlAppearance.Secondary,
                        Padding = new Thickness(10, 2, 10, 2),
                        Margin = new Thickness(12, 0, 0, 0),
                        MinWidth = 64
                    };
                    undoButton.Click += (_, _) => Undo();

                    var contentPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = message,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    contentPanel.Children.Add(undoButton);

                    var snackbar = new Snackbar(presenter)
                    {
                        Title = contentPanel,
                        Icon = new SymbolIcon(SymbolRegular.ArrowCounterclockwise24),
                        Appearance = ControlAppearance.Secondary,
                        Timeout = TimeSpan.FromSeconds(5),
                        IsCloseButtonEnabled = false
                    };

                    presenter.AddToQue(snackbar);
                    return;
                }
            }

            _snackbarService?.Show(
                message,
                string.Empty,
                ControlAppearance.Secondary,
                new SymbolIcon(SymbolRegular.CheckmarkCircle24),
                TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// 元に戻す操作を実行する。
        /// </summary>
        private void Undo()
        {
            if (_undoBackup != null)
            {
                if (GoalInput != null)
                {
                    GoalInput.Text = _undoBackup.Title ?? string.Empty;
                }
                if (NgInput != null)
                {
                    NgInput.Text = _undoBackup.NgText ?? string.Empty;
                }
                _timerValue = _undoBackup.Minutes;
                if (TimeSlider != null)
                {
                    TimeSlider.Value = _timerValue;
                }
                UpdateTimerDisplay();
                UpdateStartButton();
            }
        }

        /// <summary>
        /// ユーザーデータを読み込みます。
        /// </summary>
        public void LoadUserData()
        {
            try
            {
                if (File.Exists(DataFileName))
                {
                    var json = File.ReadAllText(DataFileName); // JSONファイルを読み込む
                    var data = JsonSerializer.Deserialize<AppData>(json); // JSONをデシリアライズ
                    if (data != null)
                    {
                        _favorites = data.Favorites ?? new List<SessionItem>(); // お気に入りリストを設定
                        _histories = data.Histories ?? new List<SessionItem>(); // 履歴リストを設定
                    }
                }
                else
                {
                    // データが存在しない場合の初期値
                    _favorites.Add(new SessionItem { Title = "英単語暗記", NgText = "スマホ", Minutes = 30 });
                    _histories.Add(new SessionItem { Title = "読書", Minutes = 45, Timestamp = "サンプル" });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadUserData Error: {ex.Message}"); // エラーをログに出力
                _favorites = new List<SessionItem>(); // 空のリストを設定
                _histories = new List<SessionItem>(); // 空のリストを設定
            }
        }

        /// <summary>
        /// ユーザーデータを保存します。
        /// </summary>
        public void SaveUserData()
        {
            try
            {
                var data = new AppData
                {
                    Favorites = _favorites, // お気に入りリストを保存
                    Histories = _histories.Where(h => !h.IsTransient).ToList() // 一時的な履歴を除外して保存
                };
                var json = JsonSerializer.Serialize(data); // JSONにシリアライズ
                File.WriteAllText(DataFileName, json); // ファイルに書き込む
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveUserData Error: {ex.Message}"); // エラーをログに出力
            }
        }
    }

    /// <summary>
    /// セッションアイテムを表すデータモデル。
    /// </summary>
    public class SessionItem
    {
        public string? Title { get; set; } // セッションのタイトル
        public string? NgText { get; set; } // 禁止事項のテキスト
        public int Minutes { get; set; } // セッションの時間（分）
        public string? Timestamp { get; set; } // セッションのタイムスタンプ
        public bool IsTransient { get; set; } = false; // 一時的なアイテムかどうか
    }

    /// <summary>
    /// アプリケーションの永続化データ。
    /// </summary>
    public class AppData
    {
        public List<SessionItem> Favorites { get; set; } = new List<SessionItem>(); // お気に入りリスト
        public List<SessionItem> Histories { get; set; } = new List<SessionItem>(); // 履歴リスト
    }
}