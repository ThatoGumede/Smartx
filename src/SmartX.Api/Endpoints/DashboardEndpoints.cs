using SmartX.Api.Services;
using SmartX.Core.Dashboard;
using SmartX.Core.Sensors;

namespace SmartX.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/dashboard").WithTags("Dashboard");

        // Polled every few seconds by the client's dashboard.js - this is the data source
        // behind the pre-attentive colour/outline tiles described in the Part 1 research
        // report (Fusco et al., 2025): the client does no threshold maths itself, it just
        // paints the Status the API already computed.
        group.MapGet("/state", (SensorStore sensors, TelemetryStore telemetry) =>
        {
            var snapshots = sensors.GetAll().Select(sensor =>
            {
                var (warning, critical) = ThresholdsFor(sensor.Category);
                var status = telemetry.ComputeStatus(sensor.Id, warning, critical);
                var latest = telemetry.GetLatest(sensor.Id);

                return new SensorStateSnapshot
                {
                    SensorId = sensor.Id,
                    DisplayLabel = $"{sensor.DeploymentLocation} ({sensor.Category})",
                    Status = status,
                    LatestValue = latest?.DisplayValue ?? "-",
                    LastSeen = sensor.LastSeen,
                };
            }).ToList();

            return Results.Ok(snapshots);
        });
    }

    private static (double warning, double critical) ThresholdsFor(SensorCategory category) => category switch
    {
        SensorCategory.Environmental => (80, 95),
        SensorCategory.PowerConsumption => (2000, 2800),
        SensorCategory.Actuator => (double.MaxValue, double.MaxValue), // booleans never breach a numeric band
        _ => (double.MaxValue, double.MaxValue),
    };
}
