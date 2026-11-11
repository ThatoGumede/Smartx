namespace SmartX.Api.Data;

public record NeighbourReport(string MacAddress, double Rssi);
public record LinkReportRequest(string MacAddress, List<NeighbourReport> Neighbours);

public record BackfillPoint(DateTimeOffset Timestamp, double? Number = null, bool? Flag = null);
public record BackfillRequest(string MacAddress, string DataType, string Unit, List<BackfillPoint> Readings);

public record TopologyNode(string Id, string SensorId, string Name, string Category, string Status, bool IsGateway, bool Alive, int Hops, bool Reachable);
public record TopologyEdge(string From, string To, double Cost, double Rssi, bool InRoutingTree, bool Active);
public record RoutingSummary(double ActiveCost, double DesignedCost, double KruskalCost, bool Verified, int TreeLinks);
public record TopologyResponse(
    List<TopologyNode> Nodes, List<TopologyEdge> Edges, RoutingSummary Routing,
    List<string> Down, List<string> Isolated, int ReachableCount);

public record RouteHop(string Id, string Name, double CostFromPrevious, bool Alive);
public record AffectedRoute(string Name, List<RouteHop>? NewRoute, double? NewCost);
public record RouteResult(
    string Id, string Name, string Status, bool Alive,
    List<RouteHop>? DesignedRoute, double? DesignedCost, string? BrokenAt,
    List<RouteHop>? ActiveRoute, double? ActiveCost, bool Rerouted, bool Reachable,
    List<RouteHop>? FewestHopsRoute, List<AffectedRoute> Affected, string Advice);

public record ConnectedNode(string Id, string Name, int Hops);
public record ConnectedResponse(string From, List<ConnectedNode> Connected, List<string> Down, List<string> Isolated);
