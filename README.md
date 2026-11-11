# Smart-X IoT Mesh Ecosystem — Final PoE

Smart-X is a .NET 10 ingestion gateway and dashboard for a mesh of ESP32-style sensors. Part 1 built
registration, typed telemetry (`TelemetryPacket<T>`) and file attachments. Part 2 added a two-lane intake
queue, live device tracking, command overrides with undo, and a predictive Suggested Actions engine. This final
part adds **Network Topology & Mesh Routing**: a graph of the mesh, routes traced to the gateway, an
AVL-indexed history for range queries, and routing that heals when nodes fail.

```
smartx/
├── SmartX.sln
├── docker-compose.yml
├── start-all.cmd               # Windows: opens API, simulator and client
├── docs/
│   ├── UPDATES_FROM_FEEDBACK.md
│   └── Smart-X_Project_Completion_Report.pdf
└── src/
    ├── SmartX.Core/            # domain + data structures (AvlTree, MinHeap, MeshGraph)
    ├── SmartX.Api/             # Minimal API, queues, stores, topology and history services
    ├── SmartX.Client/          # Razor Pages dashboard; browser JavaScript calls the API
    ├── SmartX.DeviceSimulator/ # 11 simulated ESP32 nodes that talk to the API over HTTP
    └── SmartX.SelfTest/        # dependency-free tests of the Part 3 data structures
```

## Compile and run

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should show 10.x).
Docker is optional. No database and no NuGet packages beyond the framework are used.

```
dotnet restore SmartX.sln
dotnet build SmartX.sln
```

Then start three processes, in this order, each in its own terminal (run from the repo root):

```
cd src\SmartX.Api              &  dotnet run     (http://localhost:5050)
cd src\SmartX.DeviceSimulator  &  dotnet run     (registers 11 nodes, uploads 24 h of history, reports every 3 s)
cd src\SmartX.Client           &  dotnet run     (http://localhost:5100)
```

On Windows, `start-all.cmd` does the same in three windows and opens the browser. Run each `dotnet run`
from inside its own project folder: the client serves its scripts and styles from there.
With Docker, `docker compose up --build` starts all three containers on the same ports. The simulator reaches the
API by service name inside Docker, while the client's `ApiBaseUrl` stays `http://localhost:5050` because those
calls come from your browser.

## Using the application

Open http://localhost:5100. The menu has four entries.

- **Sensor Ingestion**: register a device (MAC, location, category, optional name), attach files, validate a
  deployment tree.
- **Telemetry Dashboard**: pre-attentive status tiles from Part 1.
- **Command Stream**: live devices with search and filters, ON/OFF overrides, undo, the intake pipeline, alerts and
  Suggested Actions. A Test lab drives the priority queue (details in the Part 2 section of the report).
- **Network Topology**: the mesh diagram (gateway in the centre, one ring per hop). Blue links are the routing
  tree and carry their power cost; grey links are spare; red dashed links touch a failed node.
  - *Trace a route*: type a MAC, id or name in the Route tracker, or click a node. The route to the gateway is
    highlighted in orange with the cost of each hop. Device tiles on Command Stream have a Trace route link.
  - *Read history*: the Historical telemetry log shows the selected node's readings for a time range, with min, max,
    average, a chart, and a line showing how many tree nodes the query touched.
  - *Break something*: Take selected node offline stops the simulated device. About 12 seconds later the gateway
    marks it down, the tree is recomputed, and the route tracker shows where its traffic now hops.
  - Connectivity lists what breadth-first search reaches from the gateway, which nodes are down, and which are
    up but cut off. Selecting a node also shows the subnet seen from that node.

## The data structures and why they are there

### 1. AVL tree: historical queries (`SmartX.Core/Collections/AvlTree.cs`)

Every sensor has an `AvlTree<DateTimeOffset, TelemetryRecord>` in `Services/HistoryArchive.cs`, holding up to
20,000 timestamped readings. Telemetry arrives in time order, which would degenerate a plain binary search tree into
a linked list. An AVL tree rotates after each insert or delete so that the two subtrees of every node differ in
height by at most one, which keeps its height near log2(n) however the keys arrive.

```csharp
private Node Rebalance(Node n)
{
    Update(n);
    var balance = Balance(n);
    if (balance > 1)                                   // left-heavy
    {
        if (Balance(n.Left!) < 0) n.Left = RotateLeft(n.Left!);   // left-right case
        return RotateRight(n);
    }
    if (balance < -1)                                  // right-heavy
    {
        if (Balance(n.Right!) > 0) n.Right = RotateRight(n.Right!); // right-left case
        return RotateLeft(n);
    }
    return n;
}
```

The reason it matters for this feature is the range query. "All moisture readings between 14:00 and 16:00" only
searches the left subtree when the lower bound is below the current node, and the right subtree when the upper bound
is above it, so whole branches outside the range are never visited:

```csharp
if (lo < 0) Collect(n.Left, from, to, items, ref visited);
if (lo <= 0 && hi >= 0) items.Add(new(n.Key, n.Value));
if (hi > 0) Collect(n.Right, from, to, items, ref visited);
```

That costs O(log n + k) for k results. With 1,443 stored readings and a tree of height 11, a 2-hour window returned
122 readings after visiting 128 nodes; a 10-minute window returned 12 after visiting 21. A list scan would read all
1,443 every time. `Remove` is implemented too, because the archive drops the oldest reading when a sensor passes its
cap. The live tiles and sparklines still use the small `SortedDictionary` window from Part 2; the AVL tree is the
long-term, indexed store behind `GET /api/history/{id}/range`.

### 2. Graph and breadth-first search (`SmartX.Core/Mesh/MeshGraph.cs`)

The mesh is an undirected weighted graph. Vertices are the nodes plus a `GATEWAY` vertex; edges are wireless
links. It is stored as an adjacency list (`Dictionary<string, List<MeshEdge>>`), so memory grows with vertices plus
edges and visiting a node's neighbours never scans unrelated nodes, which suits a sparse mesh where each node hears
only a few others.

Nodes report their neighbours and signal strength to `POST /api/mesh/links`, as a mesh neighbour table does. The
API turns RSSI into a cost (`LinkCost.cs`): distance is recovered from free-space path loss, and cost is
`d^2/100 + 1`, because transmit energy grows roughly with distance squared and the `+1` is a per-hop overhead. Lower
cost means cheaper in power.

Breadth-first search answers the connectivity question: which devices can still reach the gateway, and which cannot.
A node is "alive" if it has reported recently, and BFS simply never enters a dead node:

```csharp
while (queue.Count > 0)
{
    var v = queue.Dequeue();
    foreach (var edge in _adj[v])
    {
        var n = edge.Other(v);
        if (hops.ContainsKey(n) || (isAlive is not null && !isAlive(n))) continue;
        hops[n] = hops[v] + 1;
        queue.Enqueue(n);
    }
}
```

It runs in O(V + E). Nodes that are up but absent from the result are reported as isolated, and `GET
/api/mesh/connected?from=` runs the same search from any node to show its subnet. `ShortestHopPath` uses BFS with
parent pointers to find the fewest-hop route, which the route tracker shows alongside the cheapest one.

### 3. Minimum spanning tree with a heap (`MeshGraph.Prim`, `Collections/MinHeap.cs`)

Prim's algorithm grows a tree outward from the gateway, always adding the cheapest link between the tree and a
node not yet in it. That yields the set of links that connects every node at the lowest total power cost, and a
node's route is simply its path up the tree to the gateway. The "cheapest link" choice is what a min-heap is for:
`MinHeap<TItem,TPriority>` is an array-backed binary heap, so each push and pop costs O(log E) and the whole run
O(E log E).

```csharp
PushLinksOf(root);
while (heap.TryDequeue(out var candidate, out _))
{
    if (!inTree.Add(candidate.To)) continue;   // a cheaper link already brought this node in
    parent[candidate.To] = candidate.Edge;
    tree.Add(candidate.Edge);
    total += candidate.Edge.Cost;
    PushLinksOf(candidate.To);
}
```

Kruskal's algorithm (sort all links, keep each one that does not close a cycle, tested with union-find) is also
implemented as an independent check. Both must reach the same total, and the topology response reports
`verified: true` when they do.

The tree is computed twice. The designed tree uses every link. The active tree excludes failed nodes. When they
differ, the network has healed around a failure. With the simulated mesh, the tree costs 135.33 across 11 links;
after Moisture Sensor A fails it costs 141.67 across 10, and Moisture Sensor B's route changes from
`B -> A -> Gateway` (cost 18.1) to `B -> C -> Valve 1 -> Relay 1 -> Pump A -> Gateway` (cost 73.28). That jump
is a real property worth knowing: a minimum spanning tree minimises the total cost of the network, not each node's
own path cost, so an individual node can pay more than its cheapest possible route.

## Testing

`dotnet run --project src\SmartX.SelfTest` runs the structure tests without any test framework. Each structure is
checked against an independent oracle: the AVL tree against `SortedDictionary` and LINQ after 20,000 random
inserts and deletes (invariants verified throughout), 500 random range queries against LINQ, 100,000 in-order inserts
(height 17, the theoretical minimum), the heap against a sort, and 200 random meshes with random node failures where
Prim must equal Kruskal, BFS must match the tree's vertex set, and hop paths must match BFS depth. It exits non-zero
on failure.

Manual checks through the running system:

```powershell
$api = "http://localhost:5050"
Invoke-RestMethod "$api/api/mesh/topology"                          # nodes, links, tree cost, Prim = Kruskal
Invoke-RestMethod "$api/api/mesh/route/Moisture%20Sensor%20D"       # route by name; MAC or id also work
Invoke-RestMethod "$api/api/history/Moisture%20Sensor%20A/stats"    # count, tree height, rotations
$to = (Get-Date).ToUniversalTime(); $from = $to.AddHours(-2)
Invoke-RestMethod ("$api/api/history/Moisture%20Sensor%20A/range?from={0:o}&to={1:o}&take=5" -f $from, $to)
Invoke-RestMethod -Method Post "$api/api/simulation/faults/Moisture%20Sensor%20A"   # silence a node
Start-Sleep 15
Invoke-RestMethod "$api/api/mesh/route/Moisture%20Sensor%20B"       # rerouted: true
Invoke-RestMethod -Method Delete "$api/api/simulation/faults/Moisture%20Sensor%20A" # bring it back
```

### Testing the Part 2 structures

The Command Stream page has a Test lab that drives these through the same API a device uses.

- **Priority queue**: click Pause processing, Queue 30 routine packets, then Inject critical power spike, then Resume.
  In the Intake pipeline panel the critical reading has lane `priority` and a lower `#` than the routine packets,
  although it was sent last. The same from PowerShell: `POST /api/pipeline/pause`, many `POST /api/ingest` calls, one
  with `"number":3100` for a power meter, `POST /api/pipeline/resume`, then `GET /api/pipeline/stats` (the `recent`
  list is newest first).
- **Dictionary**: `POST /api/ingest` with an unregistered MAC returns 404; registering the same MAC twice returns 409.
- **HashSet**: stop the simulator and wait about 15 seconds. `GET /api/alerts` shows one active Disconnected alert per
  device while `suppressedDuplicates` keeps rising, because the set rejects the repeats.
- **Stack**: turn Pump A ON, use All actuators ON, then Undo last command; the bulk command reverts as one step.
- **SortedDictionary**: `POST /api/telemetry/{id}/batch` with older readings; `GET /api/telemetry/{id}` returns them in
  timestamp order.

## API summary

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/mesh/links` | A node reports its neighbours and RSSI |
| GET | `/api/mesh/topology` | Nodes, links, routing tree, costs, down and isolated nodes |
| GET | `/api/mesh/route/{mac, id or name}` | Designed and active route, fewest-hop route, affected nodes |
| GET | `/api/mesh/connected?from=` | BFS: everything reachable from the gateway or a node |
| POST | `/api/history/backfill` | Upload a stored log of older readings |
| GET | `/api/history/{key}/range?from=&to=&take=` | AVL range query with index statistics |
| GET | `/api/history/{key}/stats` | Archive size, tree height, rotations |
| GET/POST/DELETE | `/api/simulation/faults[/{key}]` | Silence or restore a simulated node (test lab) |

Parts 1 and 2 routes (`/api/ingest`, `/api/sensors`, `/api/stream/snapshot`, `/api/commands`, `/api/insights`,
`/api/pipeline/*`, `/api/alerts`) are unchanged.

## Limits

State is in memory, so restarting the API clears devices and history; the simulator re-registers and re-uploads
its 24 hours on its next start. There is no authentication. Link costs come from simulated signal strength. I tested
the API, simulator, structures and page scripts end to end on Linux with .NET 10, including headless runs of the page
scripts; I did not run the Docker images or `start-all.cmd` on Windows myself.
