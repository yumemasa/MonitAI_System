using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace LockLibrary
{
    // bi_p に直接貼る場合でも同じ namespace にしておくと
    // 既存の using LockLibrary; をそのまま使えます。
    public static class NotifyHelper
    {
        private static NotifyIcon _notifyIcon;
        private static Thread _uiThread;
        private static SynchronizationContext _syncContext;

        public static void InitNotifyIcon()
        {
            if (_notifyIcon != null) return;

            // UI スレッドを別に作成してメッセージループを動かす
            _uiThread = new Thread(() =>
            {
                // SynchronizationContext をセット（必要なら Invoke で UI 操作）
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                _syncContext = SynchronizationContext.Current;

                using (ApplicationContext ctx = new ApplicationContext())
                {
                    _notifyIcon = new NotifyIcon
                    {
                        Icon = SystemIcons.Application,
                        Visible = true,
                        Text = "MonitAI Active"
                    };

                    // ダブルクリックで何かするならイベント追加可能
                    _notifyIcon.DoubleClick += (s, e) =>
                    {
                        // 例：何もしない／将来ここでフォームを復帰するなど
                    };

                    Application.Run(ctx); // メッセージループ開始（この中で _notifyIcon を生成）
                }
            });

            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.IsBackground = true;
            _uiThread.Start();

            // NotifyIcon が作られるまで短く待つ（厳密には同期化すべきだが簡易で）
            int retries = 0;
            while (_notifyIcon == null && retries++ < 50)
            {
                Thread.Sleep(20);
            }
        }

        // 任意のスレッドから呼べるように同期コンテキスト経由で実行する
        public static void Show(string title, string message)
        {
            if (_notifyIcon == null)
                InitNotifyIcon();

            if (_syncContext != null)
            {
                _syncContext.Post(_ =>
                {
                    try
                    {
                        _notifyIcon?.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
                    }
                    catch
                    {
                        // 応急処置：何もしない（失敗してもアプリ全体を止めない）
                    }
                }, null);
            }
            else
            {
                // 最終的フォールバック（可能なら呼ばない）
                try
                {
                    _notifyIcon?.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
                }
                catch { }
            }
        }

        public static void Dispose()
        {
            if (_syncContext != null)
            {
                _syncContext.Post(_ =>
                {
                    try
                    {
                        if (_notifyIcon != null)
                        {
                            _notifyIcon.Visible = false;
                            _notifyIcon.Dispose();
                            _notifyIcon = null;
                        }
                    }
                    catch { }
                }, null);
            }

            try
            {
                if (_uiThread != null && _uiThread.IsAlive)
                {
                    // Application.ExitThread を呼んでメッセージループを止めたいが、
                    // コンテキスト参照が無い可能性があるので安全に Abort しない
                    // シンプル：UI スレッドが background なのでプロセス終了で掃除される
                    _uiThread = null;
                }
            }
            catch { }
        }
    }
}
