using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace MonitAI.UI
{
    /// <summary>
    /// アプリケーションのエントリーポイント。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// アプリケーション起動時の処理。
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // ダークテーマを適用
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            ApplyThemeColors(ApplicationTheme.Dark);

            // テーマ変更イベントを購読
            ApplicationThemeManager.Changed += OnThemeChanged;

            // MainWindowを直接インスタンス化
            var mainWindow = new Features.Main.MainWindow();
            mainWindow.Show();
        }

        /// <summary>
        /// テーマ変更時のハンドラー
        /// </summary>
        private void OnThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
        {
            ApplyThemeColors(currentApplicationTheme);
        }

        /// <summary>
        /// テーマに応じた色を適用
        /// </summary>
        private void ApplyThemeColors(ApplicationTheme theme)
        {
            var resources = Current.Resources;

            if (theme == ApplicationTheme.Light)
            {
                // Light theme colors
                resources["ApplicationBackgroundColor"] = resources["LightApplicationBackgroundColor"];
                resources["SolidBackgroundFillColorBaseColor"] = resources["LightSolidBackgroundFillColorBaseColor"];
                resources["TextFillColorPrimaryColor"] = resources["LightTextFillColorPrimaryColor"];
                resources["TextFillColorSecondaryColor"] = resources["LightTextFillColorSecondaryColor"];
                resources["TextFillColorTertiaryColor"] = resources["LightTextFillColorTertiaryColor"];
                resources["ControlFillColorSecondaryColor"] = resources["LightControlFillColorSecondaryColor"];
                resources["SurfaceStrokeColorDefaultColor"] = resources["LightSurfaceStrokeColorDefaultColor"];
                resources["ControlStrokeColorDefaultColor"] = resources["LightControlStrokeColorDefaultColor"];
            }
            else
            {
                // Dark theme colors
                resources["ApplicationBackgroundColor"] = resources["DarkApplicationBackgroundColor"];
                resources["SolidBackgroundFillColorBaseColor"] = resources["DarkSolidBackgroundFillColorBaseColor"];
                resources["TextFillColorPrimaryColor"] = resources["DarkTextFillColorPrimaryColor"];
                resources["TextFillColorSecondaryColor"] = resources["DarkTextFillColorSecondaryColor"];
                resources["TextFillColorTertiaryColor"] = resources["DarkTextFillColorTertiaryColor"];
                resources["ControlFillColorSecondaryColor"] = resources["DarkControlFillColorSecondaryColor"];
                resources["SurfaceStrokeColorDefaultColor"] = resources["DarkSurfaceStrokeColorDefaultColor"];
                resources["ControlStrokeColorDefaultColor"] = resources["DarkControlStrokeColorDefaultColor"];
            }
        }
    }
}
