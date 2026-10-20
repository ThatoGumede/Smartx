using SmartX.Api.Data;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class CommandEndpoints
{
    public static void MapCommandEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/commands").WithTags("Commands");

        group.MapPost("/", (CommandRequest request, CommandService commands) =>
        {
            if (!TryParse(request.Action, out var on)) return Results.BadRequest("action must be ON or OFF.");
            var (command, error) = commands.Issue(request.SensorId, on);
            return command is null ? Results.BadRequest(error) : Results.Ok(command);
        });

        group.MapPost("/all", (BulkCommandRequest request, CommandService commands) =>
        {
            if (!TryParse(request.Action, out var on)) return Results.BadRequest("action must be ON or OFF.");
            var (command, error) = commands.IssueAll(on);
            return command is null ? Results.BadRequest(error) : Results.Ok(command);
        });

        group.MapPost("/undo", (CommandService commands) =>
        {
            var (command, error) = commands.Undo();
            return command is null ? Results.NotFound(error) : Results.Ok(command);
        });

        group.MapGet("/history", (CommandService commands) => Results.Ok(new
        {
            undoDepth = commands.UndoDepth,
            commands = commands.History(20),
        }));
    }

    private static bool TryParse(string action, out bool on)
    {
        on = action.Equals("ON", StringComparison.OrdinalIgnoreCase);
        return on || action.Equals("OFF", StringComparison.OrdinalIgnoreCase);
    }
}
