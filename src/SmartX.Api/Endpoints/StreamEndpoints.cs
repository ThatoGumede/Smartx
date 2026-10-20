using SmartX.Api.Data;
using SmartX.Api.Services;
using SmartX.Core.Dashboard;
using SmartX.Core.Sensors;

namespace SmartX.Api.Endpoints;

public static class StreamEndpoints
{
    public static void MapStreamEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Stream");

        // Everything the Command Stream page needs about devices, with search/category/status filtering
        // done server-side so the browser only receives what it will draw.
        group.MapGet("/stream/snapshot", (string? q, string? category, string? status,
            SensorStore sensors, TelemetryStore telemetry, DeviceStatusService statusService,
            CommandService commands, ActionEngine engine) =>
        {
            SensorCategory? categoryFilter = Enum.TryParse<SensorCategory>(category, true, out var cat) ? cat : null;
            SensorStatus? statusFilter = Enum.TryParse<SensorStatus>(status, true, out var st) ? st : null;
            var term = q?.Trim();
            var flagged = engine.GetSuggestions().Where(s => s.SensorId is not null).Select(s => s.SensorId!).ToHashSet();

            var devices = new List<StreamDevice>();
            foreach (var sensor in sensors.GetAll())
            {
                if (categoryFilter is not null && sensor.Category != categoryFilter) continue;

                var sensorStatus = statusService.GetStatus(sensor);
                if (statusFilter is not null && sensorStatus != statusFilter) continue;

                if (!string.IsNullOrEmpty(term) &&
                    !(sensor.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                      || sensor.DeploymentLocation.Contains(term, StringComparison.OrdinalIgnoreCase)
                      || sensor.MacAddress.Contains(term, StringComparison.OrdinalIgnoreCase)
                      || sensor.Category.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var latest = telemetry.GetLatest(sensor.Id);
                var unit = latest is null || latest.Unit is "" or "state" ? "" : latest.Unit;
                devices.Add(new StreamDevice(
                    sensor.Id, sensor.MacAddress, sensor.DisplayName, sensor.DeploymentLocation,
                    sensor.Category, sensorStatus,
                    latest is null ? "-" : latest.DisplayValue + unit,
                    sensor.LastSeen,
                    telemetry.GetPoints(sensor.Id, 30).Select(p => Math.Round(p.Value, 2)).ToList(),
                    sensor.Category == SensorCategory.Actuator ? commands.GetDesiredState(sensor.Id) : null,
                    flagged.Contains(sensor.Id)));
            }

            return Results.Ok(devices);
        });

        group.MapGet("/pipeline/stats", (TelemetryPipeline pipeline) => Results.Ok(pipeline.GetStats()));

        group.MapPost("/pipeline/pause", (TelemetryPipeline pipeline) =>
        {
            pipeline.SetPaused(true);
            return Results.Ok(pipeline.GetStats());
        });

        group.MapPost("/pipeline/resume", (TelemetryPipeline pipeline) =>
        {
            pipeline.SetPaused(false);
            return Results.Ok(pipeline.GetStats());
        });

        group.MapGet("/alerts", (AlertRegistry alerts, SensorStore sensors) => Results.Ok(new
        {
            active = alerts.ActiveSnapshot()
                .Select(a => new { sensorId = a.SensorId, label = sensors.Get(a.SensorId)?.DisplayName ?? a.SensorId, kind = a.Kind })
                .OrderBy(a => a.kind).ThenBy(a => a.label).ToList(),
            suppressedDuplicates = alerts.SuppressedDuplicates,
            events = alerts.RecentEvents(15),
        }));
    }
}
