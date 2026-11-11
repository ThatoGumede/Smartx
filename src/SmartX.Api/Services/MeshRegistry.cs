using SmartX.Api.Data;
using SmartX.Core.Mesh;

namespace SmartX.Api.Services;

/// <summary>
/// Holds the mesh graph. Nodes report which neighbours they can hear and how strongly (RSSI), the way an
/// ESP-NOW or Zigbee neighbour table works; each report becomes or refreshes an undirected weighted edge.
/// The graph is not thread-safe, so all reads and writes go through one lock.
/// </summary>
public class MeshRegistry(SensorStore sensors)
{
    private readonly MeshGraph _graph = new();
    private readonly object _lock = new();

    /// <summary>Returns the number of links recorded, or -1 if the reporting device is not registered.</summary>
    public int ReportLinks(string mac, IEnumerable<NeighbourReport> neighbours)
    {
        var self = sensors.GetByMac(mac);
        if (self is null) return -1;

        lock (_lock)
        {
            _graph.AddVertex(self.MacAddress);
            var recorded = 0;
            foreach (var n in neighbours)
            {
                var id = n.MacAddress.Equals(MeshGraph.GatewayId, StringComparison.OrdinalIgnoreCase)
                    ? MeshGraph.GatewayId
                    : sensors.GetByMac(n.MacAddress)?.MacAddress;
                if (id is null) continue;

                _graph.SetLink(self.MacAddress, id, LinkCost.FromRssi(n.Rssi), n.Rssi);
                recorded++;
            }
            return recorded;
        }
    }

    public T Read<T>(Func<MeshGraph, T> reader)
    {
        lock (_lock) return reader(_graph);
    }
}
