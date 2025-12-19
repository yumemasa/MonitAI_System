using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        private const string DataFileName = "user_data.json";
        private const int MaxHistoryCount = 15;

        private static readonly BrushConverter BrushConverterInstance = new();

        private readonly ISnackbarService? _snackbarService;
        private readonly IContentDialogService? _contentDialogService;

        private List<SessionItem> _favorites = new List<SessionItem>();
        private List<SessionItem> _histories = new List<SessionItem>();
        private double _timerValue = 45;
        private SessionItem? _undoBackup;

        /// <summary>
        /// 監視開始リクエストイベント。
        /// </summary>
        public event Action<MonitoringSession>? StartMonitoringRequested;

        /// <summary>
        /// SetupPageのコンストラクタ。
        /// </summary>
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
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 幅が狭いときはプリセットボタンを減らす
            if (e.NewSize.Width < 800)
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
        }

        private void GoalInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateStartButton();
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _timerValue = e.NewValue;
            UpdateTimerDisplay();
        }

        private void UpdateTimerDisplay()
        {
            if (TimeDisplay != null)
            {
                TimeDisplay.Text = ((int)_timerValue).ToString();
            }
            if (EndTimeText != null)
            {
                EndTimeText.Text = $"End {DateTime.Now.AddMinutes(_timerValue):HH:mm}";
            }
        }

        private void UpdateStartButton()
        {
            if (StartButton != null)
            {
                bool canStart = !string.IsNullOrWhiteSpace(GoalInput?.Text);
                StartButton.IsEnabled = canStart;
                StartButton.Content = canStart ? "集中を開始 (Start)" : "目標を入力してください";
            }
        }

        private void OnPresetTimeClick(object sender, RoutedEventArgs e)
        {
            if (sender is Wpf.Ui.Controls.Button btn && btn.Tag is string s && double.TryParse(s, out double m))
            {
                _timerValue = m;
                if (TimeSlider != null)
                {
                    TimeSlider.Value = m;
                }
                UpdateTimerDisplay();
            }
        }

        private void OnStartClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GoalInput?.Text))
            {
                return;
            }

            string goalText = GoalInput.Text;
            string ngText = NgInput?.Text ?? string.Empty;

            var newItem = new SessionItem
            {
                Title = goalText,
                NgText = ngText,
                Minutes = (int)_timerValue,
                Timestamp = DateTime.Now.ToString("MM/dd HH:mm")
            };

            _histories.Insert(0, newItem);
            if (_histories.Count > MaxHistoryCount)
            {
                _histories.RemoveAt(_histories.Count - 1);
            }

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

            // 監視開始を通知
            StartMonitoringRequested?.Invoke(session);
        }

        private void OnQuickItemClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is SessionItem item)
            {
                ApplyQuickItem(item);
            }
        }

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
                GoalInput.Text = item.Title ?? string.Empty;
            }
            if (NgInput != null)
            {
                NgInput.Text = item.NgText ?? string.Empty;
            }
            _timerValue = item.Minutes;
            if (TimeSlider != null)
            {
                TimeSlider.Value = _timerValue;
            }
            UpdateTimerDisplay();
            UpdateStartButton();

            ShowSnackbar("設定を復元しました");
        }

        private void OnFavoriteToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton toggle && toggle.Tag is SessionItem item)
            {
                ToggleFavorite(item);
            }
        }

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

            SaveUserData();
            RebuildFavoritesList();
            RebuildHistoryList();
        }

        private void OnDeleteHistoryItemClick(object sender, RoutedEventArgs e)
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

        private UIElement CreateFavoriteItemControl(SessionItem item)
        {
            // 全体を包むカード状のボーダー（背景色をつける）
            var cardBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"], // カード背景色
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"]
            };

            // 内部レイアウト: [クリック可能なメインエリア] [区切り線] [お気に入りボタン]
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // メインエリア
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) }); // 区切り線（オプション）
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // ボタンエリア

            // 1. メインクリックエリア（透明ボタンで覆う）
            var mainButton = new Wpf.Ui.Controls.Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Appearance = ControlAppearance.Transparent, // 背景透明
                Tag = item,
                ToolTip = item.Title,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 8, 4, 8),
                CornerRadius = new CornerRadius(6, 0, 0, 6), // 左側だけ丸める
                BorderThickness = new Thickness(0)
            };
            mainButton.Click += OnQuickItemClick;

            // ボタンの中身（時間とタイトル）
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

            // 2. 区切り線（うっすら）
            var separator = new Border
            {
                Width = 1,
                Background = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"],
                Margin = new Thickness(0, 8, 0, 8)
            };
            Grid.SetColumn(separator, 1);
            grid.Children.Add(separator);

            // 3. お気に入り解除ボタン（右端）
            var toggleButton = new ToggleButton
            {
                IsChecked = true,
                Tag = item,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ToolTip = "お気に入り解除",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            toggleButton.Click += OnFavoriteToggleClick;

            var toggleContent = new SymbolIcon
            {
                Symbol = SymbolRegular.Star24,
                FontSize = 16,
                Foreground = (Brush)BrushConverterInstance.ConvertFrom("#FACD15")!, // 黄色
                Filled = true
            };
            toggleButton.Content = toggleContent;

            Grid.SetColumn(toggleButton, 2);
            grid.Children.Add(toggleButton);

            cardBorder.Child = grid;
            return cardBorder;
        }

        private UIElement CreateHistoryItemControl(SessionItem item)
        {
            // 全体を包むカード状のボーダー
            var cardBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"]
            };

            // レイアウト: [メインエリア] [区切り線] [操作ボタンエリア(UniformGrid)]
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); // ボタン幅

            // 1. メインクリックエリア
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

            var timestampText = new TextBlock
            {
                Text = item.Timestamp,
                FontSize = 10,
                Margin = new Thickness(0, 1, 0, 0),
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            };
            stackPanel.Children.Add(timestampText);

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

            // 2. 区切り線
            var separator = new Border
            {
                Width = 1,
                Background = (Brush)Application.Current.Resources["SurfaceStrokeColorDefaultBrush"],
                Margin = new Thickness(0, 8, 0, 8)
            };
            Grid.SetColumn(separator, 1);
            grid.Children.Add(separator);

            // 3. 操作ボタンエリア (削除 & 追加)
            // UniformGridを使うことで、上下のスペースを完全に2等分して配置ズレを防ぐ
            var actionsPanel = new UniformGrid
            {
                Columns = 1,
                Rows = 2,
                Margin = new Thickness(0, 2, 0, 2)
            };
            Grid.SetColumn(actionsPanel, 2);

            // 削除ボタン (上)
            var deleteButton = new Wpf.Ui.Controls.Button
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 30,
                Height = 30,
                ToolTip = "削除",
                Tag = item,
                Appearance = ControlAppearance.Transparent,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(0)
            };
            deleteButton.Click += OnDeleteHistoryItemClick;

            var deleteIcon = new SymbolIcon
            {
                Symbol = SymbolRegular.Dismiss16,
                FontSize = 14,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            };
            deleteButton.Content = deleteIcon;
            actionsPanel.Children.Add(deleteButton);

            // お気に入り追加ボタン (下)
            var toggleButton = new ToggleButton
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = false,
                Tag = item,
                Width = 30,
                Height = 30,
                ToolTip = "お気に入りに追加",
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            toggleButton.Click += OnFavoriteToggleClick;

            var toggleContent = new SymbolIcon
            {
                Symbol = SymbolRegular.Star24,
                FontSize = 16,
                Filled = false,
                Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
            };
            toggleButton.Content = toggleContent;
            actionsPanel.Children.Add(toggleButton);

            grid.Children.Add(actionsPanel);

            cardBorder.Child = grid;
            return cardBorder;
        }

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
                    _favorites.Add(new SessionItem { Title = "英単語暗記", NgText = "スマホ", Minutes = 30 });
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

        /// <summary>
        /// ユーザーデータを保存します。
        /// </summary>
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
    }

    /// <summary>
    /// セッションアイテムを表すデータモデル。
    /// </summary>
    public class SessionItem
    {
        public string? Title { get; set; }
        public string? NgText { get; set; }
        public int Minutes { get; set; }
        public string? Timestamp { get; set; }
        public bool IsTransient { get; set; } = false;
    }

    /// <summary>
    /// アプリケーションの永続化データ。
    /// </summary>
    public class AppData
    {
        public List<SessionItem> Favorites { get; set; } = new List<SessionItem>();
        public List<SessionItem> Histories { get; set; } = new List<SessionItem>();
    }
}
