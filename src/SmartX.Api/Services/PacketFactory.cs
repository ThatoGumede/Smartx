using SmartX.Core.Telemetry;

namespace SmartX.Api.Services;

/// <summary>Builds the right TelemetryPacket&lt;T&gt; for a device-reported data type.</summary>
public static class PacketFactory
{
    public static (TelemetryRecord? Record, string? Error) Create(string sensorId, string dataType, double? number, bool? flag, string unit)
    {
        switch (dataType.Trim().ToLowerInvariant())
        {
            case "float" when number is not null:
                return (new TelemetryPacket<float>(sensorId, (float)number.Value, unit).ToRecord(), null);
            case "int" when number is not null:
                return (new TelemetryPacket<int>(sensorId, (int)Math.Round(number.Value), unit).ToRecord(), null);
            case "bool" when flag is not null:
                return (new TelemetryPacket<bool>(sensorId, flag.Value, unit).ToRecord(), null);
            case "float" or "int":
                return (null, "A 'number' value is required for float and int readings.");
            case "bool":
                return (null, "A 'flag' value is required for bool readings.");
            default:
                return (null, "dataType must be 'float', 'int' or 'bool'.");
        }
    }
}
