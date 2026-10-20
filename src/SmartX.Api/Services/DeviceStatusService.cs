using SmartX.Core.Alerts;
using SmartX.Core.Dashboard;
using SmartX.Core.Sensors;

namespace SmartX.Api.Services;

/// <summary>Works out the status band (Normal / Warning / Critical / Disconnected) for a device right now.</summary>
public class DeviceStatusService(TelemetryStore telemetry)
{
    public static readonly TimeSpan DisconnectAfter = TimeSpan.FromSeconds(12);

    public SensorStatus GetStatus(SensorProfile sensor)
    {
        var latest = telemetry.GetLatest(sensor.Id);
        if (latest is null || sensor.LastSeen is null || DateTimeOffset.UtcNow - sensor.LastSeen.Value > DisconnectAfter)
            return SensorStatus.Disconnected;

        return AlertClassifier.Classify(sensor.Category, latest.NumericValue).Status;
    }
}
