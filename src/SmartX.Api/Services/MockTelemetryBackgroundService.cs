using SmartX.Core.Sensors;
using SmartX.Core.Telemetry;

namespace SmartX.Api.Services;

/// <summary>
/// Seeds a handful of default Smart-X sensors on startup and then continuously generates
/// plausible mock telemetry for them every few seconds - simulating an ESP32 mesh under load
/// so the ingestion pipeline (TelemetryPacket&lt;T&gt;, the batch buffer, the dashboard status
/// calculation) can be demonstrated without needing physical hardware. Roughly one sensor in
/// six is made to "go quiet" for a while on each tick, and readings occasionally spike past
/// their threshold, so the dashboard's pre-attentive Disconnected/Critical states are
/// genuinely exercised rather than only ever showing green.
/// </summary>
public class MockTelemetryBackgroundService(
    SensorStore sensors,
    TelemetryStore telemetry,
    ILogger<MockTelemetryBackgroundService> logger) : BackgroundService
{
    private readonly Random _rng = new();
    private readonly HashSet<string> _quietSensors = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SeedDefaultSensors();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var sensor in sensors.GetAll())
            {
                TickSensor(sensor);
            }
        }
    }

    private void SeedDefaultSensors()
    {
        var defaults = new[]
        {
            new SensorProfile { MacAddress = "24:6F:28:AA:01:01", DeploymentLocation = "Facility A / Zone 1 / Sub-Zone B / Node 1", Category = SensorCategory.Environmental },
            new SensorProfile { MacAddress = "24:6F:28:AA:01:02", DeploymentLocation = "Facility A / Zone 1 / Sub-Zone B / Node 2", Category = SensorCategory.Environmental },
            new SensorProfile { MacAddress = "24:6F:28:AA:02:01", DeploymentLocation = "Facility A / Zone 2 / Sub-Zone A / Node 1", Category = SensorCategory.PowerConsumption },
            new SensorProfile { MacAddress = "24:6F:28:AA:02:02", DeploymentLocation = "Facility A / Zone 2 / Sub-Zone A / Node 2", Category = SensorCategory.PowerConsumption },
            new SensorProfile { MacAddress = "24:6F:28:AA:03:01", DeploymentLocation = "Facility A / Zone 3 / Sub-Zone A / Node 1", Category = SensorCategory.Actuator },
        };

        foreach (var sensor in defaults)
        {
            sensors.Register(sensor);
            logger.LogInformation("Seeded default sensor {SensorId} ({Category}) at {Location}",
                sensor.Id, sensor.Category, sensor.DeploymentLocation);
        }
    }

    private void TickSensor(SensorProfile sensor)
    {
        // Roughly 8% chance per tick to toggle a sensor's "quiet" (disconnected) state,
        // simulating a node dropping off / reconnecting to the mesh.
        if (_rng.NextDouble() < 0.03)
        {
            if (_quietSensors.Contains(sensor.Id)) _quietSensors.Remove(sensor.Id);
            else _quietSensors.Add(sensor.Id);
        }

        if (_quietSensors.Contains(sensor.Id))
            return; // skip this tick entirely -> dashboard will age this sensor into Disconnected

        bool spike = _rng.NextDouble() < 0.08; // ~1 in 12 ticks produces an anomalous spike

        TelemetryRecord record = sensor.Category switch
        {
            SensorCategory.Environmental => new TelemetryPacket<float>(
                sensor.Id,
                spike ? 96f + (float)_rng.NextDouble() * 4f : 38f + (float)_rng.NextDouble() * 20f,
                "%").ToRecord(),

            SensorCategory.PowerConsumption => new TelemetryPacket<int>(
                sensor.Id,
                spike ? 2850 + _rng.Next(0, 300) : 900 + _rng.Next(0, 1400),
                "W").ToRecord(),

            SensorCategory.Actuator => new TelemetryPacket<bool>(
                sensor.Id,
                _rng.NextDouble() < 0.5,
                "state").ToRecord(),

            _ => throw new ArgumentOutOfRangeException(nameof(sensor.Category)),
        };

        telemetry.Add(record);
        sensors.Touch(sensor.Id, record.Timestamp);
    }
}
