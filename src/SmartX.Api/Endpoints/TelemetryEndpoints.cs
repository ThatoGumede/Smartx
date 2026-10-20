using SmartX.Api.Data;
using SmartX.Api.Services;
using SmartX.Core.Telemetry;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/telemetry").WithTags("Telemetry");

        // Id-based typed endpoints (Part 1). Each builds the same generic TelemetryPacket<T> with a
        // different T, then hands it to the pipeline. They return 202: the packet is queued, not yet stored.
        group.MapPost("/{sensorId}/float", (string sensorId, FloatTelemetryRequest body, SensorStore sensors, TelemetryPipeline pipeline) =>
            Submit(sensorId, new TelemetryPacket<float>(sensorId, body.Value, body.Unit).ToRecord(), sensors, pipeline));

        group.MapPost("/{sensorId}/int", (string sensorId, IntTelemetryRequest body, SensorStore sensors, TelemetryPipeline pipeline) =>
            Submit(sensorId, new TelemetryPacket<int>(sensorId, body.Value, body.Unit).ToRecord(), sensors, pipeline));

        group.MapPost("/{sensorId}/bool", (string sensorId, BoolTelemetryRequest body, SensorStore sensors, TelemetryPipeline pipeline) =>
            Submit(sensorId, new TelemetryPacket<bool>(sensorId, body.Value, body.Unit).ToRecord(), sensors, pipeline));

        // Jagged-array batch: float[][] -> TelemetryBatchBuffer -> List<TelemetryPacket<float>> -> pipeline.
        // Batches carry older timestamps; the SortedDictionary timeline puts them in order when stored.
        group.MapPost("/{sensorId}/batch", (string sensorId, BatchTelemetryRequest body, SensorStore sensors, TelemetryPipeline pipeline) =>
        {
            var sensor = sensors.Get(sensorId);
            if (sensor is null) return Results.NotFound($"Sensor '{sensorId}' is not registered.");

            var buffer = new TelemetryBatchBuffer();
            buffer.LoadBatches(body.Batches);
            var (totalSamples, average, largestBatchSize) = buffer.Summarise();

            List<TelemetryPacket<float>> packets = buffer.FlattenToPackets(sensorId, body.Unit);
            foreach (var packet in packets)
                pipeline.Enqueue(packet.ToRecord(), sensor.Category);

            return Results.Accepted(null, new
            {
                sensorId,
                batchesReceived = body.Batches.Length,
                totalSamples,
                average,
                largestBatchSize,
                packetsQueued = packets.Count,
            });
        });

        // History is read from the SortedDictionary, so it always comes back in timestamp order.
        group.MapGet("/{sensorId}", (string sensorId, int take, TelemetryStore telemetry) =>
            Results.Ok(telemetry.GetHistory(sensorId, take <= 0 ? 50 : take)));
    }

    private static IResult Submit(string sensorId, TelemetryRecord record, SensorStore sensors, TelemetryPipeline pipeline)
    {
        var sensor = sensors.Get(sensorId);
        if (sensor is null) return Results.NotFound($"Sensor '{sensorId}' is not registered.");

        var lane = pipeline.Enqueue(record, sensor.Category);
        return Results.Accepted($"/api/telemetry/{sensorId}", new { sensorId, lane });
    }
}
