using SmartX.Core.Sensors;

namespace SmartX.Api.Data;

public record RegisterSensorRequest(string MacAddress, string DeploymentLocation, SensorCategory Category);

public record FloatTelemetryRequest(float Value, string Unit = "");
public record IntTelemetryRequest(int Value, string Unit = "");
public record BoolTelemetryRequest(bool Value, string Unit = "state");
public record BatchTelemetryRequest(float[][] Batches, string Unit = "%");
