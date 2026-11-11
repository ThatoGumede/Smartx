using System.Diagnostics;
using SmartX.Core.Collections;
using SmartX.Core.Mesh;

// Dependency-free checks for the Part 3 data structures. Each structure is compared with an
// independent oracle (SortedDictionary, LINQ, a hand-worked example). Run: dotnet run --project src/SmartX.SelfTest

var failures = 0;
void Check(string name, bool ok, string detail = "")
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail == "" ? "" : "  -> " + detail)}");
    if (!ok) failures++;
}

var rng = new Random(42);

// ---------------- AVL tree ----------------
{
    var avl = new AvlTree<long, int>();
    const int n = 100_000;
    for (var i = 0; i < n; i++) avl.Insert(i, i);   // worst case for a plain BST: keys arrive in order
    Check("AVL: 100,000 in-order inserts stay balanced", avl.VerifyInvariants() && avl.Height <= 1.45 * Math.Log2(n + 2),
        $"height {avl.Height} (a plain BST would be {n}); theoretical minimum {avl.MinimumPossibleHeight}; {avl.Rotations} rotations");

    var tree = new AvlTree<int, int>();
    var oracle = new SortedDictionary<int, int>();
    var ok = true;
    for (var i = 0; i < 20_000; i++)
    {
        var k = rng.Next(0, 5000);
        if (rng.NextDouble() < 0.6) { tree.Insert(k, i); oracle[k] = i; }
        else { var a = tree.Remove(k); var b = oracle.Remove(k); ok &= a == b; }
        if (i % 2000 == 0) ok &= tree.VerifyInvariants();
    }
    ok &= tree.VerifyInvariants() && tree.Count == oracle.Count && tree.InOrder().SequenceEqual(oracle);
    Check("AVL: 20,000 random inserts/deletes match SortedDictionary and keep AVL invariants", ok, $"{tree.Count} keys left");

    var good = true;
    for (var q = 0; q < 500; q++)
    {
        int lo = rng.Next(-100, 5100), hi = rng.Next(-100, 5100);
        var expected = oracle.Where(p => p.Key >= lo && p.Key <= hi).ToList();
        good &= tree.Range(lo, hi).Items.SequenceEqual(expected);
    }
    Check("AVL: 500 random range queries return exactly the LINQ result", good);

    var big = new AvlTree<long, int>();
    for (var i = 0; i < 100_000; i++) big.Insert(i * 3, i);
    var r = big.Range(150_000, 150_300);
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < 1000; i++) big.Range(150_000, 150_300);
    sw.Stop();
    var linear = Stopwatch.StartNew();
    var all = big.InOrder().ToList();
    for (var i = 0; i < 1000; i++) _ = all.Where(p => p.Key >= 150_000 && p.Key <= 150_300).ToList();
    linear.Stop();
    Check("AVL: range query visits at most results + 2 x height nodes (O(log n + k))", r.NodesVisited <= r.Items.Count + 2 * big.Height && r.Items.Count == 101,
        $"{r.Items.Count} results, {r.NodesVisited} of 100,000 nodes visited; 1000 queries: tree {sw.ElapsedMilliseconds} ms vs list scan {linear.ElapsedMilliseconds} ms");
}

// ---------------- Min-heap ----------------
{
    var heap = new MinHeap<int, int>();
    var values = Enumerable.Range(0, 5000).Select(_ => rng.Next(0, 1000)).ToList();
    foreach (var v in values) heap.Enqueue(v, v);
    var drained = new List<int>();
    while (heap.TryDequeue(out var item, out _)) drained.Add(item);
    Check("MinHeap: dequeues in ascending order", drained.SequenceEqual(values.OrderBy(x => x)) && heap.Count == 0);
}

// ---------------- Graph: BFS, Prim, Kruskal ----------------
{
    var g = new MeshGraph();
    g.SetLink("GATEWAY", "A", 1, -50);
    g.SetLink("A", "B", 2, -55);
    g.SetLink("B", "GATEWAY", 5, -70);
    g.SetLink("B", "C", 1, -48);
    g.SetLink("A", "C", 4, -60);
    var prim = g.Prim("GATEWAY");
    var kruskal = g.Kruskal("GATEWAY");
    Check("MST: hand-worked example costs 4 with 3 links (Prim and Kruskal)", prim.TotalCost == 4 && kruskal.TotalCost == 4 && prim.Edges.Count == 3,
        $"Prim {prim.TotalCost}, Kruskal {kruskal.TotalCost}");
    var path = MeshGraph.PathToRoot(prim, "C")!;
    var hops = new List<string> { "C" };
    foreach (var e in path) hops.Add(e.Other(hops[^1]));
    Check("MST: route from C to the gateway is C-B-A-GATEWAY", string.Join("-", hops) == "C-B-A-GATEWAY", string.Join("-", hops));

    var okAll = true;
    for (var trial = 0; trial < 200; trial++)
    {
        var rg = new MeshGraph();
        var nodes = Enumerable.Range(0, 25).Select(i => $"N{i}").Prepend("GATEWAY").ToList();
        for (var i = 1; i < nodes.Count; i++)                                    // random spanning structure keeps it connected
            rg.SetLink(nodes[i], nodes[rng.Next(0, i)], Math.Round(1 + rng.NextDouble() * 20, 2), -60);
        for (var extra = 0; extra < 30; extra++)
            rg.SetLink(nodes[rng.Next(nodes.Count)], nodes[rng.Next(nodes.Count)], Math.Round(1 + rng.NextDouble() * 20, 2), -60);

        var p = rg.Prim("GATEWAY");
        var k = rg.Kruskal("GATEWAY");
        okAll &= Math.Abs(p.TotalCost - k.TotalCost) < 0.01 && p.Edges.Count == nodes.Count - 1 && k.Edges.Count == nodes.Count - 1;

        var dead = nodes.Skip(1).Where(_ => rng.NextDouble() < 0.25).ToHashSet();
        Func<string, bool> alive = v => !dead.Contains(v);
        var reach = rg.Bfs("GATEWAY", alive);
        var pa = rg.Prim("GATEWAY", alive);
        var ka = rg.Kruskal("GATEWAY", alive);
        okAll &= pa.Edges.Count == reach.Count - 1 && ka.Edges.Count == reach.Count - 1 && Math.Abs(pa.TotalCost - ka.TotalCost) < 0.01;
        okAll &= !reach.Keys.Any(dead.Contains);
        foreach (var v in reach.Keys.Take(5))
            okAll &= rg.ShortestHopPath("GATEWAY", v, alive)!.Count - 1 == reach[v];
    }
    Check("Graph: 200 random meshes - Prim = Kruskal, BFS = MST vertex set, hop paths = BFS depth, with random node failures", okAll);
}

Console.WriteLine(failures == 0 ? "\nALL SELF-TESTS PASSED" : $"\n{failures} SELF-TEST(S) FAILED");
return failures == 0 ? 0 : 1;
