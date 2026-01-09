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
        private string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "screenShot2",
            "config.json");

        /// <summary>
        /// SettingsPageのコンストラクタ。
        /// </summary>
        public SettingsPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (!File.Exists(ConfigPath)) return;

            try
            {
                string json = File.ReadAllText(ConfigPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                if (settings != null)
                {
                    // API Key
                    if (settings.TryGetValue("ApiKey", out var apiKey))
                    {
                        ApiKeyInput.Password = apiKey;
                    }

                    // Mode
                    if (settings.TryGetValue("UseApi", out var useApiStr) && bool.TryParse(useApiStr, out var useApi))
                    {
                        if (useApi) RadioApi.IsChecked = true;
                        else RadioCli.IsChecked = true;
                    }
                    else
                    {
                        RadioCli.IsChecked = true; // Default
                    }

                    // Model
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

                    // CLI Path
                    if (settings.TryGetValue("CliPath", out var cliPath))
                    {
                        CliPathInput.Text = cliPath;
                    }

                    // ACP Script Path
                    if (settings.TryGetValue("AcpScriptPath", out var acpPath))
                    {
                        AcpScriptPathInput.Text = acpPath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Dictionary<string, string> settings = new Dictionary<string, string>();

                // 既存の設定を読み込む（上書きしないように）
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    settings = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }

                // UIの値を反映
                settings["ApiKey"] = ApiKeyInput.Password;
                settings["UseApi"] = (RadioApi.IsChecked == true).ToString();
                
                if (ModelComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    settings["Model"] = selectedItem.Content.ToString() ?? "gemini-2.5-flash-lite";
                }

                // Advanced Settings (空の場合はキーを削除、または空文字列として保存)
                settings["CliPath"] = CliPathInput.Text.Trim();
                settings["AcpScriptPath"] = AcpScriptPathInput.Text.Trim();

                // 保存
                string dir = Path.GetDirectoryName(ConfigPath)!;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));

                MessageBox.Show("設定を保存しました。", "MonitAI", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定の保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
