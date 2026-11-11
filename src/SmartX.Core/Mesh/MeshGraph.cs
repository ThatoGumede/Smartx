using SmartX.Core.Collections;

namespace SmartX.Core.Mesh;

/// <summary>An undirected wireless link between two nodes. Cost is relative transmit power; lower is better.</summary>
public record MeshEdge(string A, string B, double Cost, double Rssi)
{
    public string Other(string vertex) => string.Equals(vertex, A, StringComparison.OrdinalIgnoreCase) ? B : A;
}

/// <summary>A minimum spanning tree rooted at the gateway. ParentEdge maps each node to the link toward its parent.</summary>
public record MstResult(string Root, double TotalCost, List<MeshEdge> Edges, Dictionary<string, MeshEdge> ParentEdge);

/// <summary>
/// The physical mesh as an undirected weighted graph stored as an adjacency list: vertices are the ESP32 nodes
/// plus the gateway, edges are the wireless links. An adjacency list keeps memory proportional to
/// vertices + edges, which suits a sparse mesh where each node only hears a handful of neighbours, and
/// lets traversal visit a node's neighbours without scanning every other node.
/// A node's "alive" state is passed in as a predicate, so the same graph answers both "what was the design"
/// (everything alive) and "what works right now" (failed nodes excluded).
/// </summary>
public class MeshGraph
{
    public const string GatewayId = "GATEWAY";

    private static readonly StringComparer Ids = StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<string, List<MeshEdge>> _adj = new(Ids);

    public MeshGraph() => AddVertex(GatewayId);

    public IReadOnlyCollection<string> Vertices => _adj.Keys;
    public bool HasVertex(string id) => _adj.ContainsKey(id);
    public void AddVertex(string id) { if (!_adj.ContainsKey(id)) _adj[id] = new List<MeshEdge>(); }

    /// <summary>Adds the link, or replaces it if the two nodes were already linked.</summary>
    public void SetLink(string a, string b, double cost, double rssi)
    {
        if (Ids.Equals(a, b)) return;
        AddVertex(a); AddVertex(b);
        RemoveLink(a, b);
        var edge = new MeshEdge(a, b, cost, rssi);
        _adj[a].Add(edge);
        _adj[b].Add(edge);
    }

    public void RemoveLink(string a, string b)
    {
        if (_adj.TryGetValue(a, out var la)) la.RemoveAll(e => Ids.Equals(e.Other(a), b));
        if (_adj.TryGetValue(b, out var lb)) lb.RemoveAll(e => Ids.Equals(e.Other(b), a));
    }

    public IReadOnlyList<MeshEdge> EdgesOf(string vertex) => _adj.TryGetValue(vertex, out var l) ? l : [];

    public IEnumerable<MeshEdge> Edges() => _adj.Values.SelectMany(l => l).Distinct();

    /// <summary>
    /// Breadth-first search from <paramref name="start"/>, returning every reachable vertex with its hop count.
    /// Vertices for which <paramref name="isAlive"/> is false are never entered, so the result is exactly the set of
    /// nodes that can still reach (or be reached from) the start through working links. O(V + E).
    /// </summary>
    public Dictionary<string, int> Bfs(string start, Func<string, bool>? isAlive = null)
    {
        var hops = new Dictionary<string, int>(Ids);
        if (!_adj.ContainsKey(start) || (isAlive is not null && !isAlive(start))) return hops;

        var queue = new Queue<string>();
        hops[start] = 0;
        queue.Enqueue(start);

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
        return hops;
    }

    /// <summary>The route with the fewest hops (not the cheapest), found by BFS with parent pointers.</summary>
    public List<string>? ShortestHopPath(string from, string to, Func<string, bool>? isAlive = null)
    {
        if (!_adj.ContainsKey(from) || !_adj.ContainsKey(to)) return null;
        if (isAlive is not null && (!isAlive(from) || !isAlive(to))) return null;

        var parent = new Dictionary<string, string>(Ids);
        var seen = new HashSet<string>(Ids) { from };
        var queue = new Queue<string>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var v = queue.Dequeue();
            if (Ids.Equals(v, to)) break;
            foreach (var edge in _adj[v])
            {
                var n = edge.Other(v);
                if (seen.Contains(n) || (isAlive is not null && !isAlive(n))) continue;
                seen.Add(n);
                parent[n] = v;
                queue.Enqueue(n);
            }
        }

        if (!seen.Contains(to)) return null;
        var path = new List<string> { to };
        while (!Ids.Equals(path[^1], from)) path.Add(parent[path[^1]]);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Prim's algorithm from the gateway. It grows a single tree outward, always taking the cheapest link that
    /// connects a tree node to a node not yet in the tree; a <see cref="MinHeap{TItem,TPriority}"/> of candidate
    /// links makes each "cheapest link" choice O(log E), for O(E log E) overall. Only nodes reachable from the
    /// root through alive nodes are spanned.
    /// </summary>
    public MstResult Prim(string root, Func<string, bool>? isAlive = null)
    {
        var parent = new Dictionary<string, MeshEdge>(Ids);
        var tree = new List<MeshEdge>();
        if (!_adj.ContainsKey(root) || (isAlive is not null && !isAlive(root)))
            return new MstResult(root, 0, tree, parent);

        var inTree = new HashSet<string>(Ids) { root };
        var heap = new MinHeap<(string To, MeshEdge Edge), double>();
        double total = 0;

        void PushLinksOf(string v)
        {
            foreach (var edge in _adj[v])
            {
                var n = edge.Other(v);
                if (inTree.Contains(n) || (isAlive is not null && !isAlive(n))) continue;
                heap.Enqueue((n, edge), edge.Cost);
            }
        }

        PushLinksOf(root);
        while (heap.TryDequeue(out var candidate, out _))
        {
            if (!inTree.Add(candidate.To)) continue;   // a cheaper link already brought this node in
            parent[candidate.To] = candidate.Edge;
            tree.Add(candidate.Edge);
            total += candidate.Edge.Cost;
            PushLinksOf(candidate.To);
        }

        return new MstResult(root, Math.Round(total, 2), tree, parent);
    }

    /// <summary>
    /// Kruskal's algorithm over the same component. It sorts every link by cost and keeps each one that does not
    /// close a cycle, using union-find to test that. It exists here as an independent check: both algorithms
    /// must reach the same total cost even if they pick different but equally cheap links.
    /// </summary>
    public MstResult Kruskal(string root, Func<string, bool>? isAlive = null)
    {
        var component = Bfs(root, isAlive).Keys.ToHashSet(Ids);
        var tree = new List<MeshEdge>();
        double total = 0;

        var leader = component.ToDictionary(v => v, v => v, Ids);
        string Find(string v)
        {
            while (!Ids.Equals(leader[v], v)) { leader[v] = leader[leader[v]]; v = leader[v]; }   // path halving
            return v;
        }

        foreach (var edge in Edges().Where(e => component.Contains(e.A) && component.Contains(e.B)).OrderBy(e => e.Cost))
        {
            var ra = Find(edge.A);
            var rb = Find(edge.B);
            if (Ids.Equals(ra, rb)) continue;
            leader[ra] = rb;
            tree.Add(edge);
            total += edge.Cost;
        }

        return new MstResult(root, Math.Round(total, 2), tree, new Dictionary<string, MeshEdge>(Ids));
    }

    /// <summary>Follows parent links from a node up to the tree's root. Null if the node is not in the tree.</summary>
    public static List<MeshEdge>? PathToRoot(MstResult mst, string node)
    {
        var path = new List<MeshEdge>();
        var current = node;
        while (!Ids.Equals(current, mst.Root))
        {
            if (!mst.ParentEdge.TryGetValue(current, out var edge) || path.Count > 10_000) return null;
            path.Add(edge);
            current = edge.Other(current);
        }
        return path;
    }
}
