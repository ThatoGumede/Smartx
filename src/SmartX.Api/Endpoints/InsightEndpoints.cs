using SmartX.Api.Data;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class InsightEndpoints
{
    public static void MapInsightEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api").WithTags("Insights");

        group.MapGet("/insights", (ActionEngine engine) => Results.Ok(new
        {
            suggestions = engine.GetSuggestions(),
            patterns = engine.GetPatterns(),
            activity = engine.RecentActivity(),
        }));

        // The UI reports what the operator searched for, which is one of the signals the engine learns from.
        group.MapPost("/activity/search", (SearchActivityRequest request, ActionEngine engine) =>
        {
            engine.RecordSearch(request.Term ?? string.Empty);
            return Results.Ok();
        });

        group.MapPost("/insights/seed-demo", (ActionEngine engine) => Results.Ok(new { message = engine.SeedDemoHistory() }));
    }
}
