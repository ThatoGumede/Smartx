using SmartX.Api.Data;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class HistoryEndpoints
{
    public static void MapHistoryEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/history").WithTags("History");

        // Bulk upload of older readings (e.g. a node reconnecting with its stored log). Goes straight to the
        // AVL-indexed archive; it does not touch the live tiles.
        group.MapPost("/backfill", (BackfillRequest request, SensorStore sensors, HistoryArchive archive) =>
        {
            var sensor = sensors.GetByMac(request.MacAddress);
            if (sensor is null) return Results.NotFound($"No device registered with MAC '{request.MacAddress}'.");
            if (request.Readings is null || request.Readings.Count == 0) return Results.BadRequest("readings is empty.");
            if (request.Readings.Count > 5000) return Results.BadRequest("A backfill call accepts at most 5000 readings.");

            var records = new List<SmartX.Core.Telemetry.TelemetryRecord>();
            foreach (var point in request.Readings)
            {
                var (record, error) = PacketFactory.Create(sensor.Id, request.DataType, point.Number, point.Flag, request.Unit, point.Timestamp);
                if (record is null) return Results.BadRequest(error);
                records.Add(record);
            }

            return Results.Ok(new { stored = archive.AddRange(records), stats = archive.Stats(sensor.Id) });
        });

        // Range query: GET /api/history/{id or MAC}/range?from=2026-11-10T14:00:00Z&to=2026-11-10T16:00:00Z&take=200
        group.MapGet("/{key}/range", (string key, DateTimeOffset? from, DateTimeOffset? to, int? take,
            SensorStore sensors, HistoryArchive archive) =>
        {
            var sensor = sensors.Resolve(key);
            if (sensor is null) return Results.NotFound($"No device matches '{key}'.");

            var end = to ?? DateTimeOffset.UtcNow;
            var start = from ?? end.AddHours(-1);
            if (start > end) return Results.BadRequest("'from' must not be after 'to'.");

            var limit = Math.Clamp(take ?? 200, 1, 1000);
            return Results.Ok(new { sensorId = sensor.Id, name = sensor.DisplayName, from = start, to = end, result = archive.Query(sensor.Id, start, end, limit) });
        });

        group.MapGet("/{key}/stats", (string key, SensorStore sensors, HistoryArchive archive) =>
            sensors.Resolve(key) is { } sensor ? Results.Ok(archive.Stats(sensor.Id)) : Results.NotFound());
    }
}
