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

            // メインウィンドウを閉じたら他ウィンドウが残っていてもアプリを終了させる
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // 初期テーマ適用（以降の切替も ApplicationThemeManager に任せる）
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);

            // MainWindowを直接インスタンス化
            var mainWindow = new Features.Main.MainWindow();
            mainWindow.Show();
        }

        // テーマ配色は Wpf.Ui の ThemesDictionary に委ねる。ここでは手動のブラシ上書きは行わない。
    }
}
