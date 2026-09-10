namespace SmartX.Core.Telemetry;

/// <summary>
/// Storage/transport-friendly shape of a telemetry reading. The ingestion API accepts
/// strongly-typed <see cref="TelemetryPacket{T}"/> instances (see the three /api/telemetry
/// endpoints for float, int and bool payloads); each one is immediately flattened into a
/// TelemetryRecord so the sensor store, dashboard-state calculator and JSON responses only
/// ever have to deal with one shape, regardless of which C# type produced the reading.
/// </summary>
public class TelemetryRecord
{
    public required string SensorId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public double? NumericValue { get; set; }
    public bool? BoolValue { get; set; }

    public string DisplayValue => BoolValue is not null
        ? (BoolValue.Value ? "ON" : "OFF")
        : NumericValue?.ToString("0.##") ?? "-";
}
