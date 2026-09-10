using SmartX.Api.Data;
using SmartX.Api.Services;
using SmartX.Core.Telemetry;

namespace SmartX.Api.Endpoints;

public static class TelemetryEndpoints
{
    public static void MapTelemetryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/telemetry").WithTags("Telemetry");

        // Three thin endpoints, one per CLR value type a sensor can report in. Each one
        // instantiates the SAME generic TelemetryPacket<T> with a different T - this is the
        // concrete demonstration of the "uniform handling of disparate data structures without
        // boxing" requirement: no `object`/`dynamic` payload anywhere in this pipeline.
        group.MapPost("/{sensorId}/float", (string sensorId, FloatTelemetryRequest body,
            SensorStore sensors, TelemetryStore telemetry) =>
        {
            var packet = new TelemetryPacket<float>(sensorId, body.Value, body.Unit);
            return Ingest(sensorId, packet.ToRecord(), sensors, telemetry);
        });

        group.MapPost("/{sensorId}/int", (string sensorId, IntTelemetryRequest body,
            SensorStore sensors, TelemetryStore telemetry) =>
        {
            var packet = new TelemetryPacket<int>(sensorId, body.Value, body.Unit);
            return Ingest(sensorId, packet.ToRecord(), sensors, telemetry);
        });

        group.MapPost("/{sensorId}/bool", (string sensorId, BoolTelemetryRequest body,
            SensorStore sensors, TelemetryStore telemetry) =>
        {
            var packet = new TelemetryPacket<bool>(sensorId, body.Value, body.Unit);
            return Ingest(sensorId, packet.ToRecord(), sensors, telemetry);
        });

        // Accepts a jagged array of historical batches (float[][]), flattens it through
        // TelemetryBatchBuffer into a List<TelemetryPacket<float>>, then commits it.
        group.MapPost("/{sensorId}/batch", (string sensorId, BatchTelemetryRequest body,
            SensorStore sensors, TelemetryStore telemetry) =>
        {
            if (sensors.Get(sensorId) is null)
                return Results.NotFound($"Sensor '{sensorId}' is not registered.");

            var buffer = new TelemetryBatchBuffer();
            buffer.LoadBatches(body.Batches);
            var (totalSamples, average, largestBatchSize) = buffer.Summarise();

            List<TelemetryPacket<float>> packets = buffer.FlattenToPackets(sensorId, body.Unit);
            telemetry.AddRange(packets.Select(p => p.ToRecord()));

            if (packets.Count > 0)
                sensors.Touch(sensorId, packets[^1].Timestamp);

            return Results.Ok(new
            {
                sensorId,
                batchesReceived = body.Batches.Length,
                totalSamples,
                average,
                largestBatchSize,
                packetsCommitted = packets.Count,
            });
        });

        group.MapGet("/{sensorId}", (string sensorId, int take, TelemetryStore telemetry) =>
            Results.Ok(telemetry.GetHistory(sensorId, take <= 0 ? 50 : take)));
    }

    private static IResult Ingest(string sensorId, TelemetryRecord record, SensorStore sensors, TelemetryStore telemetry)
    {
        if (sensors.Get(sensorId) is null)
            return Results.NotFound($"Sensor '{sensorId}' is not registered.");

        telemetry.Add(record);
        sensors.Touch(sensorId, record.Timestamp);
        return Results.Ok(record);
    }
}
