using SmartX.Core.Alerts;

namespace SmartX.Api.Services;

/// <summary>
/// Background consumer for <see cref="TelemetryPipeline"/>. Priority lane first, then the standard
/// queue. Committing a packet stores it in the timeline, refreshes the device's last-seen time and
/// feeds the alert registry.
/// </summary>
public class TelemetryProcessorService(
    TelemetryPipeline pipeline,
    TelemetryStore telemetry,
    SensorStore sensors,
    AlertRegistry alerts,
    HistoryArchive archive) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var next = pipeline.TryDequeue();
                if (next is null)
                {
                    await Task.Delay(15, stoppingToken);
                    continue;
                }

                Process(next.Value.Packet, next.Value.Lane);
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private void Process(QueuedPacket packet, string lane)
    {
        var sensor = sensors.Get(packet.Record.SensorId);
        if (sensor is null) return; // device was removed while its packet was queued

        telemetry.Add(packet.Record);
        archive.Add(packet.Record);
        sensors.Touch(sensor.Id, DateTimeOffset.UtcNow);

        var status = AlertClassifier.Classify(sensor.Category, packet.Record.NumericValue).Status;
        alerts.OnReading(sensor, status, packet.Record);
        pipeline.RecordProcessed(packet, lane, sensor.DisplayName);
    }
}
