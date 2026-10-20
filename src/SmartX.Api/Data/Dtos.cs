using SmartX.Core.Sensors;

namespace SmartX.Api.Data;

public record RegisterSensorRequest(string MacAddress, string DeploymentLocation, SensorCategory Category, string? Name = null);

public record FloatTelemetryRequest(float Value, string Unit = "");
public record IntTelemetryRequest(int Value, string Unit = "");
public record BoolTelemetryRequest(bool Value, string Unit = "state");
public record BatchTelemetryRequest(float[][] Batches, string Unit = "%");

/// <summary>What a physical device would send: its own MAC address plus one typed reading.</summary>
public record IngestRequest(string MacAddress, string DataType, double? Number = null, bool? Flag = null, string Unit = "");

public record CommandRequest(string SensorId, string Action);
public record BulkCommandRequest(string Action);
public record SearchActivityRequest(string Term);

public record StreamDevice(
    string SensorId, string MacAddress, string Name, string Location,
    SensorCategory Category, SmartX.Core.Dashboard.SensorStatus Status,
    string LatestValue, DateTimeOffset? LastSeen, List<double> Series,
    bool? ActuatorOn, bool Flagged);
