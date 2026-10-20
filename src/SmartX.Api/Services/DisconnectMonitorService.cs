namespace SmartX.Api.Services;

/// <summary>
/// Checks every couple of seconds for devices that have gone quiet. It deliberately re-reports every
/// silent device on every sweep; <see cref="AlertRegistry"/> (a HashSet) is what stops those repeats
/// becoming duplicate alerts.
/// </summary>
public class DisconnectMonitorService(SensorStore sensors, AlertRegistry alerts) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var sensor in sensors.GetAll())
                {
                    if (sensor.LastSeen is { } seen && DateTimeOffset.UtcNow - seen > DeviceStatusService.DisconnectAfter)
                        alerts.OnDisconnected(sensor);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }
}
