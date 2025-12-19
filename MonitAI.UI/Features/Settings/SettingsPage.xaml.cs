using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace MonitAI.UI.Features.Settings
{
    /// <summary>
    /// 設定ページのコードビハインド。
    /// </summary>
    public partial class SettingsPage : Page
    {
        private const string ConfigDirName = "screenShot2";
        private const string ConfigFileName = "config.json";

        /// <summary>
        /// SettingsPageのコンストラクタ。
        /// </summary>
        public SettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                string configPath = GetConfigPath();
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (settings != null)
                    {
                        if (settings.TryGetValue("ApiKey", out var apiKey))
                        {
                            ApiKeyBox.Password = apiKey;
                        }

                        if (settings.TryGetValue("Model", out var model))
                        {
                            foreach (ComboBoxItem item in ModelComboBox.Items)
                            {
                                if (item.Content.ToString() == model)
                                {
                                    ModelComboBox.SelectedItem = item;
                                    break;
                                }
                            }
                        }

                        if (settings.TryGetValue("CliPath", out var cliPath))
                        {
                            CliPathBox.Text = cliPath;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string configPath = GetConfigPath();
                string dirPath = Path.GetDirectoryName(configPath)!;

                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                }

                // 既存の設定を読み込む（上書きしないように）
                var settings = new Dictionary<string, string>();
                if (File.Exists(configPath))
                {
                    try
                    {
                        string existingJson = File.ReadAllText(configPath);
                        var existing = JsonSerializer.Deserialize<Dictionary<string, string>>(existingJson);
                        if (existing != null) settings = existing;
                    }
                    catch { /* 無視 */ }
                }

                // 新しい値を設定
                settings["ApiKey"] = ApiKeyBox.Password;
                if (ModelComboBox.SelectedItem is ComboBoxItem item)
                {
                    settings["Model"] = item.Content.ToString()!;
                }
                settings["CliPath"] = CliPathBox.Text;

                // 保存
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);

                MessageBox.Show("設定を保存しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetConfigPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, ConfigDirName, ConfigFileName);
        }
    }
}
