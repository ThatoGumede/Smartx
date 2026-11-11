using SmartX.Api.Data;
using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class MeshEndpoints
{
    public static void MapMeshEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/mesh").WithTags("Mesh");

        // A node reports the neighbours it can hear (MAC + RSSI), like a mesh neighbour table.
        group.MapPost("/links", (LinkReportRequest request, MeshRegistry mesh) =>
        {
            var recorded = mesh.ReportLinks(request.MacAddress, request.Neighbours ?? []);
            return recorded < 0
                ? Results.NotFound($"No device registered with MAC '{request.MacAddress}'.")
                : Results.Ok(new { linksRecorded = recorded });
        });

        group.MapGet("/topology", (TopologyService topology) => Results.Ok(topology.GetTopology()));

        group.MapGet("/route/{key}", (string key, TopologyService topology) =>
            topology.GetRoute(key) is { } route ? Results.Ok(route) : Results.NotFound($"No device matches '{key}'."));

        group.MapGet("/connected", (string? from, TopologyService topology) =>
            topology.GetConnected(from) is { } result ? Results.Ok(result) : Results.NotFound($"No device matches '{from}'."));
    }
}
