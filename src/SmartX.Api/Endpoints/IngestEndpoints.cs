using SmartX.Api.Data;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class IngestEndpoints
{
    public static void MapIngestEndpoints(this WebApplication app)
    {
        // The endpoint a real ESP32 would call: it only knows its own MAC address. The MAC is resolved to
        // the device through the registry's Dictionary (O(1)), the reading is wrapped in the matching
        // TelemetryPacket<T>, and it is queued for the background processor.
        app.MapPost("/api/ingest", (IngestRequest request, SensorStore sensors, TelemetryPipeline pipeline) =>
        {
            if (string.IsNullOrWhiteSpace(request.MacAddress))
                return Results.BadRequest("macAddress is required.");

            var sensor = sensors.GetByMac(request.MacAddress);
            if (sensor is null)
                return Results.NotFound($"No device registered with MAC '{request.MacAddress}'.");

            var (record, error) = PacketFactory.Create(sensor.Id, request.DataType, request.Number, request.Flag, request.Unit);
            if (record is null) return Results.BadRequest(error);

            var lane = pipeline.Enqueue(record, sensor.Category);
            return Results.Accepted(null, new { sensorId = sensor.Id, lane });
        }).WithTags("Ingest");

        // Actuators poll this to learn what state the operator wants them in.
        app.MapGet("/api/devices/{mac}/desired-state", (string mac, SensorStore sensors, CommandService commands) =>
            sensors.GetByMac(mac) is { } sensor
                ? Results.Ok(new { on = commands.GetDesiredState(sensor.Id) })
                : Results.NotFound()).WithTags("Ingest");
    }
}
