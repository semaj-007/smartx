using SmartX.Core.Services;

namespace SmartX.Api.Services;

/// <summary>Drains bounded FIFO work off the request path; critical work bypasses this cadence.</summary>
public sealed class TelemetryProcessor(RealTimeEngine engine) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
        var lastDisconnectCheck = DateTime.MinValue;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            engine.ProcessPending(128);
            var now = DateTime.UtcNow;
            if (now - lastDisconnectCheck >= TimeSpan.FromSeconds(1))
            {
                engine.MarkDisconnected(now, TimeSpan.FromSeconds(15));
                lastDisconnectCheck = now;
            }
        }
    }
}
