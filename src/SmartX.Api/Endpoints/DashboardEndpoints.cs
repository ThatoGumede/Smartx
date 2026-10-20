using SmartX.Api.Services;
using SmartX.Core.Dashboard;

namespace SmartX.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        // Polled by the Part 1 telemetry dashboard. Status comes from the shared classifier, so the
        // colours here always agree with the alert registry and the Command Stream page.
        group.MapGet("/state", (SensorStore sensors, TelemetryStore telemetry, DeviceStatusService statusService) =>
        {
            var snapshots = sensors.GetAll().Select(sensor =>
            {
                var latest = telemetry.GetLatest(sensor.Id);
                return new SensorStateSnapshot
                {
                    SensorId = sensor.Id,
                    DisplayLabel = $"{sensor.DisplayName} - {sensor.DeploymentLocation}",
                    Status = statusService.GetStatus(sensor),
                    LatestValue = latest?.DisplayValue ?? "-",
                    LastSeen = sensor.LastSeen,
                };
            }).ToList();

            return Results.Ok(snapshots);
        });
    }
}
