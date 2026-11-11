using SmartX.Api.Data;
using SmartX.Core.Dashboard;
using SmartX.Core.Mesh;

namespace SmartX.Api.Services;

/// <summary>
/// Turns the mesh graph plus live device status into what the Network Topology page needs: the picture,
/// the routing tree, and per-node routes. "Alive" means the device has reported recently (not Disconnected).
/// Two routing trees are always compared: the designed one (every link, as if nothing had failed) and the
/// active one (failed nodes excluded). Where they differ, the network has healed itself around a failure.
/// </summary>
public class TopologyService(MeshRegistry mesh, SensorStore sensors, DeviceStatusService statusService)
{
    private static readonly StringComparer Ids = StringComparer.OrdinalIgnoreCase;
    private const string Gateway = MeshGraph.GatewayId;

    private string NameOf(string vertex) =>
        Ids.Equals(vertex, Gateway) ? "Gateway" : sensors.GetByMac(vertex)?.DisplayName ?? vertex;

    private string StatusOf(string vertex) =>
        Ids.Equals(vertex, Gateway) ? "Gateway"
        : sensors.GetByMac(vertex) is { } s ? statusService.GetStatus(s).ToString() : "Removed";

    // One consistent view of who is alive for the whole computation.
    private HashSet<string> AliveSet(IEnumerable<string> vertices) =>
        vertices.Where(v => Ids.Equals(v, Gateway) || (sensors.GetByMac(v) is { } s && statusService.GetStatus(s) != SensorStatus.Disconnected))
                .ToHashSet(Ids);

    public TopologyResponse GetTopology() => mesh.Read(g =>
    {
        var vertices = g.Vertices.ToList();
        var alive = AliveSet(vertices);
        bool IsAlive(string v) => alive.Contains(v);

        var designedHops = g.Bfs(Gateway);
        var reachable = g.Bfs(Gateway, IsAlive);
        var designed = g.Prim(Gateway);
        var active = g.Prim(Gateway, IsAlive);
        var kruskal = g.Kruskal(Gateway, IsAlive);
        var inTree = active.Edges.ToHashSet();

        var nodes = vertices
            .Select(v => new TopologyNode(
                v, sensors.GetByMac(v)?.Id ?? v, NameOf(v),
                Ids.Equals(v, Gateway) ? "Gateway" : sensors.GetByMac(v)?.Category.ToString() ?? "Unknown",
                StatusOf(v), Ids.Equals(v, Gateway), IsAlive(v),
                designedHops.GetValueOrDefault(v, -1), reachable.ContainsKey(v)))
            .OrderBy(n => n.Hops < 0 ? int.MaxValue : n.Hops).ThenBy(n => n.Name)
            .ToList();

        var edges = g.Edges()
            .Select(e => new TopologyEdge(e.A, e.B, e.Cost, e.Rssi, inTree.Contains(e), IsAlive(e.A) && IsAlive(e.B)))
            .ToList();

        return new TopologyResponse(
            nodes, edges,
            new RoutingSummary(active.TotalCost, designed.TotalCost, kruskal.TotalCost,
                Math.Abs(active.TotalCost - kruskal.TotalCost) < 0.01, active.Edges.Count),
            vertices.Where(v => !IsAlive(v)).Select(NameOf).OrderBy(n => n).ToList(),
            vertices.Where(v => IsAlive(v) && !reachable.ContainsKey(v)).Select(NameOf).OrderBy(n => n).ToList(),
            reachable.Count - 1);
    });

    /// <summary>All nodes currently connected to <paramref name="fromKey"/> (default: the gateway), found by BFS.</summary>
    public ConnectedResponse? GetConnected(string? fromKey) => mesh.Read(g =>
    {
        string start = Gateway;
        if (!string.IsNullOrWhiteSpace(fromKey) && !Ids.Equals(fromKey, Gateway))
        {
            var s = sensors.Resolve(fromKey);
            if (s is null) return null;
            start = s.MacAddress;
        }

        var vertices = g.Vertices.ToList();
        var alive = AliveSet(vertices);
        var hops = g.Bfs(start, alive.Contains);

        return new ConnectedResponse(
            NameOf(start),
            hops.Where(h => !Ids.Equals(h.Key, start)).OrderBy(h => h.Value).ThenBy(h => NameOf(h.Key))
                .Select(h => new ConnectedNode(h.Key, NameOf(h.Key), h.Value)).ToList(),
            vertices.Where(v => !alive.Contains(v)).Select(NameOf).OrderBy(n => n).ToList(),
            vertices.Where(v => alive.Contains(v) && !hops.ContainsKey(v)).Select(NameOf).OrderBy(n => n).ToList());
    });

    public RouteResult? GetRoute(string key) => mesh.Read(g =>
    {
        var isGateway = Ids.Equals(key, Gateway);
        var sensor = isGateway ? null : sensors.Resolve(key);
        if (!isGateway && sensor is null) return null;
        var id = isGateway ? Gateway : sensor!.MacAddress;
        if (!g.HasVertex(id))
            return new RouteResult(id, NameOf(id), StatusOf(id), false, null, null, null, null, null, false, false, null, [],
                "This device is registered but has not reported any wireless links yet, so it is not part of the mesh graph.");

        var vertices = g.Vertices.ToList();
        var alive = AliveSet(vertices);
        bool IsAlive(string v) => alive.Contains(v);

        var designed = g.Prim(Gateway);
        var active = g.Prim(Gateway, IsAlive);
        var designedPath = MeshGraph.PathToRoot(designed, id);
        var activePath = MeshGraph.PathToRoot(active, id);

        List<RouteHop>? Hops(List<MeshEdge>? path)
        {
            if (path is null) return null;
            var hops = new List<RouteHop> { new(id, NameOf(id), 0, IsAlive(id)) };
            var current = id;
            foreach (var e in path)
            {
                current = e.Other(current);
                hops.Add(new RouteHop(current, NameOf(current), e.Cost, IsAlive(current)));
            }
            return hops;
        }

        var designedHops = Hops(designedPath);
        var activeHops = IsAlive(id) ? Hops(activePath) : null;
        var designedCost = designedPath?.Sum(e => e.Cost);
        var activeCost = activeHops is null ? (double?)null : activePath!.Sum(e => e.Cost);

        string? brokenAt = designedHops?.Skip(1).FirstOrDefault(h => !h.Alive)?.Name;
        var rerouted = activeHops is not null && designedHops is not null &&
                       !designedHops.Select(h => h.Id).SequenceEqual(activeHops.Select(h => h.Id), Ids);

        var fewest = g.ShortestHopPath(id, Gateway, IsAlive)?.Select((v, i) => new RouteHop(v, NameOf(v), 0, true)).ToList();

        // If this node is down, which other nodes used to route through it, and where do they hop now?
        var affected = new List<AffectedRoute>();
        if (!IsAlive(id) && !isGateway)
        {
            foreach (var other in vertices.Where(v => !Ids.Equals(v, id) && !Ids.Equals(v, Gateway)))
            {
                var path = MeshGraph.PathToRoot(designed, other);
                if (path is null || !path.Any(e => Ids.Equals(e.A, id) || Ids.Equals(e.B, id))) continue;
                var newPath = IsAlive(other) ? MeshGraph.PathToRoot(active, other) : null;
                var newRoute = newPath is null ? null : BuildHops(other, newPath, NameOf, IsAlive);
                affected.Add(new AffectedRoute(NameOf(other), newRoute, newPath?.Sum(e => e.Cost)));
            }
        }

        var advice = isGateway ? "This is the gateway: every route ends here."
            : !IsAlive(id) ? $"{NameOf(id)} is down, so it has no route. {affected.Count} node(s) relied on it in the designed tree" +
                             (affected.Count > 0 ? "; their new routes are listed below." : ".")
            : activeHops is null ? $"{NameOf(id)} is up, but every path to the gateway passes through failed nodes, so it is isolated."
            : rerouted ? $"Rerouted around {brokenAt ?? "a failed node"}: cost {designedCost:0.##} -> {activeCost:0.##}."
            : "Using its designed route; no failures on the path.";

        return new RouteResult(id, NameOf(id), StatusOf(id), IsAlive(id),
            designedHops, designedCost is null ? null : Math.Round(designedCost.Value, 2), brokenAt,
            activeHops, activeCost is null ? null : Math.Round(activeCost.Value, 2),
            rerouted, activeHops is not null, fewest, affected, advice);
    });

    private static List<RouteHop> BuildHops(string start, List<MeshEdge> path, Func<string, string> name, Func<string, bool> alive)
    {
        var hops = new List<RouteHop> { new(start, name(start), 0, alive(start)) };
        var current = start;
        foreach (var e in path)
        {
            current = e.Other(current);
            hops.Add(new RouteHop(current, name(current), e.Cost, alive(current)));
        }
        return hops;
    }
}
