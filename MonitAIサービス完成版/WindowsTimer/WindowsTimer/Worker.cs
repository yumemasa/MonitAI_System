using Microsoft.Extensions.Hosting;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WindowsTimer
{
    public class Worker : BackgroundService
    {
        private Stopwatch stopwatch;
        private DateTime startUtc;

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            startUtc = DateTime.UtcNow;
            stopwatch = Stopwatch.StartNew();

            EventLog.WriteEntry(
                "Application",
                "WindowsTimer started (time tamper protection enabled)",
                EventLogEntryType.Information
            );

            return Task.Run(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    CheckTimeTampering();
                    await Task.Delay(1000, stoppingToken);
                }
            }, stoppingToken);
        }

        private void CheckTimeTampering()
        {
            // 本来経過しているはずの時間
            var expectedUtc = startUtc + stopwatch.Elapsed;

            var nowUtc = DateTime.UtcNow;
            var diff = Math.Abs((nowUtc - expectedUtc).TotalSeconds);

            // 5秒以上ズレたら「改ざん」
            if (diff > 5)
            {
                EventLog.WriteEntry(
                    "Application",
                    $"System time tampering detected (diff={diff}s)",
                    EventLogEntryType.Warning
                );

                // ここで「ブロック処理」や「通知」だけ行う
                // ※ 強制 SetSystemTime は推奨しない
            }
        }
    }
}
