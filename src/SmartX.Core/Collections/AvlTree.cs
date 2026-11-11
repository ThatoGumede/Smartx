namespace SmartX.Core.Collections;

public record RangeResult<TKey, TValue>(List<KeyValuePair<TKey, TValue>> Items, int NodesVisited);

/// <summary>
/// Self-balancing binary search tree (AVL). After every insert or delete each node's two subtrees differ
/// in height by at most one, which keeps the tree's height at about 1.44 * log2(n) however the keys
/// arrive. Telemetry arrives almost perfectly in time order, which would turn a plain BST into a linked
/// list; the rotations below are what stop that. Lookups, inserts and deletes are O(log n), and a range
/// query is O(log n + k) for k results because whole subtrees outside the range are never visited.
/// </summary>
public class AvlTree<TKey, TValue> where TKey : IComparable<TKey>
{
    private sealed class Node(TKey key, TValue value)
    {
        public TKey Key { get; set; } = key;
        public TValue Value { get; set; } = value;
        public Node? Left { get; set; }
        public Node? Right { get; set; }
        public int Height { get; set; } = 1;
    }

    private Node? _root;

    public int Count { get; private set; }
    public long Rotations { get; private set; }
    public int Height => H(_root);

    /// <summary>Smallest height any binary tree holding Count nodes could have: ceil(log2(n + 1)).</summary>
    public int MinimumPossibleHeight => Count == 0 ? 0 : (int)Math.Ceiling(Math.Log2(Count + 1));

    private static int H(Node? n) => n?.Height ?? 0;
    private static void Update(Node n) => n.Height = 1 + Math.Max(H(n.Left), H(n.Right));
    private static int Balance(Node n) => H(n.Left) - H(n.Right);

    private Node RotateRight(Node y)
    {
        var x = y.Left!;
        y.Left = x.Right;
        x.Right = y;
        Update(y); Update(x);
        Rotations++;
        return x;
    }

    private Node RotateLeft(Node x)
    {
        var y = x.Right!;
        x.Right = y.Left;
        y.Left = x;
        Update(x); Update(y);
        Rotations++;
        return y;
    }

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

    /// <summary>Adds the key, or replaces the value if the key exists. Returns true if a new node was added.</summary>
    public bool Insert(TKey key, TValue value)
    {
        var added = false;
        _root = Insert(_root, key, value, ref added);
        if (added) Count++;
        return added;
    }

    private Node Insert(Node? n, TKey key, TValue value, ref bool added)
    {
        if (n is null) { added = true; return new Node(key, value); }

        var c = key.CompareTo(n.Key);
        if (c < 0) n.Left = Insert(n.Left, key, value, ref added);
        else if (c > 0) n.Right = Insert(n.Right, key, value, ref added);
        else { n.Value = value; return n; }

        return Rebalance(n);
    }

    public bool Remove(TKey key)
    {
        var removed = false;
        _root = Remove(_root, key, ref removed);
        if (removed) Count--;
        return removed;
    }

    private Node? Remove(Node? n, TKey key, ref bool removed)
    {
        if (n is null) return null;

        var c = key.CompareTo(n.Key);
        if (c < 0) n.Left = Remove(n.Left, key, ref removed);
        else if (c > 0) n.Right = Remove(n.Right, key, ref removed);
        else
        {
            removed = true;
            if (n.Left is null) return n.Right;
            if (n.Right is null) return n.Left;

            // Two children: take the in-order successor's data, then delete the successor.
            var successor = n.Right;
            while (successor.Left is not null) successor = successor.Left;
            n.Key = successor.Key;
            n.Value = successor.Value;
            var ignored = false;
            n.Right = Remove(n.Right, successor.Key, ref ignored);
        }
        return Rebalance(n);
    }

    public bool TryGet(TKey key, out TValue value)
    {
        var n = _root;
        while (n is not null)
        {
            var c = key.CompareTo(n.Key);
            if (c == 0) { value = n.Value; return true; }
            n = c < 0 ? n.Left : n.Right;
        }
        value = default!;
        return false;
    }

    public bool TryGetMin(out TKey key, out TValue value)
    {
        var n = _root;
        if (n is null) { key = default!; value = default!; return false; }
        while (n.Left is not null) n = n.Left;
        key = n.Key; value = n.Value;
        return true;
    }

    public bool TryGetMax(out TKey key, out TValue value)
    {
        var n = _root;
        if (n is null) { key = default!; value = default!; return false; }
        while (n.Right is not null) n = n.Right;
        key = n.Key; value = n.Value;
        return true;
    }

    /// <summary>
    /// Every entry with from &lt;= key &lt;= to, in ascending key order. The left subtree is only searched if
    /// the lower bound is below the current key, and the right only if the upper bound is above it, so
    /// the search touches the two boundary paths plus the results instead of scanning every stored entry.
    /// </summary>
    public RangeResult<TKey, TValue> Range(TKey from, TKey to)
    {
        var items = new List<KeyValuePair<TKey, TValue>>();
        var visited = 0;
        if (from.CompareTo(to) <= 0) Collect(_root, from, to, items, ref visited);
        return new RangeResult<TKey, TValue>(items, visited);
    }

    private static void Collect(Node? n, TKey from, TKey to, List<KeyValuePair<TKey, TValue>> items, ref int visited)
    {
        if (n is null) return;
        visited++;

        var lo = from.CompareTo(n.Key);
        var hi = to.CompareTo(n.Key);

        if (lo < 0) Collect(n.Left, from, to, items, ref visited);
        if (lo <= 0 && hi >= 0) items.Add(new KeyValuePair<TKey, TValue>(n.Key, n.Value));
        if (hi > 0) Collect(n.Right, from, to, items, ref visited);
    }

    public IEnumerable<KeyValuePair<TKey, TValue>> InOrder()
    {
        var stack = new Stack<Node>();
        var n = _root;
        while (n is not null || stack.Count > 0)
        {
            while (n is not null) { stack.Push(n); n = n.Left; }
            n = stack.Pop();
            yield return new KeyValuePair<TKey, TValue>(n.Key, n.Value);
            n = n.Right;
        }
    }

    /// <summary>Checks the BST ordering, the stored heights and the AVL balance rule everywhere. Used by tests.</summary>
    public bool VerifyInvariants() => Verify(_root, default, false, default, false, out _);

    private static bool Verify(Node? n, TKey? lo, bool hasLo, TKey? hi, bool hasHi, out int height)
    {
        height = 0;
        if (n is null) return true;
        if (hasLo && n.Key.CompareTo(lo!) <= 0) return false;
        if (hasHi && n.Key.CompareTo(hi!) >= 0) return false;

        if (!Verify(n.Left, lo, hasLo, n.Key, true, out var lh)) return false;
        if (!Verify(n.Right, n.Key, true, hi, hasHi, out var rh)) return false;

        height = 1 + Math.Max(lh, rh);
        return Math.Abs(lh - rh) <= 1 && n.Height == height;
    }
}
