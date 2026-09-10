using SmartX.Core.Deployment;

namespace SmartX.Api.Endpoints;

public static class DeploymentEndpoints
{
    public static void MapDeploymentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/deployment").WithTags("Deployment");

        // Body: a nested DeploymentNode tree (Facility -> Zone -> SubZone -> Node). Validated
        // by the recursive DeploymentValidator in SmartX.Core.
        group.MapPost("/validate", (DeploymentNode tree) =>
        {
            ValidationResult result = DeploymentValidator.Validate(tree);
            return Results.Ok(result);
        });

        // A ready-made example tree so the endpoint can be exercised without building a
        // payload by hand - handy for the video demo / Postman collection.
        group.MapGet("/sample", () => Results.Ok(BuildSampleTree()));
    }

    private static DeploymentNode BuildSampleTree() => new()
    {
        Name = "Facility A",
        Tier = "Facility",
        Children =
        [
            new DeploymentNode
            {
                Name = "Zone 1",
                Tier = "Zone",
                Children =
                [
                    new DeploymentNode
                    {
                        Name = "Sub-Zone B",
                        Tier = "SubZone",
                        Children =
                        [
                            new DeploymentNode { Name = "Node 4", Tier = "Node", HasSensorAssigned = true },
                            new DeploymentNode { Name = "Node 5", Tier = "Node", HasSensorAssigned = false },
                        ],
                    },
                ],
            },
        ],
    };
}
