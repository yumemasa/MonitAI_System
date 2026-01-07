using System;
using System.ServiceProcess;
using System.Threading;

namespace WindowsSystems
{
    internal class Protect
    {
        // 監視対象サービス名
        private const string TargetServiceName = "MonitAI_Service";

        // 監視間隔（ミリ秒）
        private const int CheckIntervalMs = 5000; // 5秒

        public void Start()
        {
            // 無限監視ループ
            while (true)
            {
                try
                {
                    using (var sc = new ServiceController(TargetServiceName))
                    {
                        // 停止していたら再起動
                        if (sc.Status == ServiceControllerStatus.Stopped)
                        {
                            sc.Start();
                        }
                    }
                }
                catch
                {
                    // ・サービス未登録
                    // ・一時的な SCM エラー
                    // → 落ちないことが最優先なので握りつぶす
                }

                Thread.Sleep(CheckIntervalMs);
            }
        }
    }
}