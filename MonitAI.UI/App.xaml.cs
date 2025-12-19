using System.Windows;
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

            // MainWindowを直接インスタンス化
            var mainWindow = new Features.Main.MainWindow();
            mainWindow.Show();
        }
    }
}