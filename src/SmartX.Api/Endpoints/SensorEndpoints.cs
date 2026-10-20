using SmartX.Api.Data;
using SmartX.Api.Services;
using SmartX.Core.Sensors;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    public static void MapSensorEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sensors").WithTags("Sensors");

        group.MapPost("/", (RegisterSensorRequest request, SensorStore store) =>
        {
            if (string.IsNullOrWhiteSpace(request.MacAddress) || string.IsNullOrWhiteSpace(request.DeploymentLocation))
                return Results.BadRequest("macAddress and deploymentLocation are required.");

            var profile = new SensorProfile
            {
                MacAddress = request.MacAddress,
                DeploymentLocation = request.DeploymentLocation,
                Category = request.Category,
                Name = request.Name?.Trim() ?? string.Empty,
            };

            return store.TryRegister(profile, out var result)
                ? Results.Created($"/api/sensors/{result.Id}", result)
                : Results.Conflict(new { message = "A device with that MAC address is already registered.", existing = result });
        });

        group.MapGet("/", (SensorStore store) => Results.Ok(store.GetAll()));

        group.MapGet("/{id}", (string id, SensorStore store) =>
            store.Get(id) is { } profile ? Results.Ok(profile) : Results.NotFound());

        group.MapDelete("/{id}", (string id, SensorStore store) =>
            store.Remove(id) ? Results.NoContent() : Results.NotFound());
    }
}
