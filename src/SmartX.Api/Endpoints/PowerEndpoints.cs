using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class PowerEndpoints
{
    public static void MapPowerEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/power").WithTags("Power");

        // Exercises the overloaded +, -, ==, > operators on PowerReading directly, e.g.
        // GET /api/power/compare/{meterA}/{meterB} -> aggregate load, delta, imbalance flag.
        group.MapGet("/compare/{meterIdA}/{meterIdB}", (string meterIdA, string meterIdB,
            PowerComparisonService power) =>
        {
            var result = power.Compare(meterIdA, meterIdB);
            return result is null
                ? Results.NotFound("Both meters must be registered and have at least one reading.")
                : Results.Ok(result);
        });
    }
}
