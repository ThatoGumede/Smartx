using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/simulation").WithTags("Simulation");

        // The simulator polls this list and stays silent for every MAC in it.
        group.MapGet("/faults", (FaultInjection faults) => Results.Ok(faults.Current()));

        group.MapPost("/faults/{key}", (string key, SensorStore sensors, FaultInjection faults) =>
        {
            if (sensors.Resolve(key) is not { } sensor) return Results.NotFound($"No device matches '{key}'.");
            faults.Silence(sensor.MacAddress);
            return Results.Ok(faults.Current());
        });

        group.MapDelete("/faults/{key}", (string key, SensorStore sensors, FaultInjection faults) =>
        {
            if (sensors.Resolve(key) is not { } sensor) return Results.NotFound($"No device matches '{key}'.");
            faults.Restore(sensor.MacAddress);
            return Results.Ok(faults.Current());
        });
    }
}
