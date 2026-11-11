namespace SmartX.Core.Collections;

/// <summary>
/// Array-backed binary min-heap. The smallest priority is always at index 0, and the children of the node
/// at index i sit at 2i+1 and 2i+2. Enqueue and dequeue each sift one element along a single root-to-leaf
/// path, so both are O(log n), while peeking at the minimum is O(1). Prim's algorithm uses it to pick the
/// cheapest wireless link leaving the tree built so far.
/// </summary>
public class MinHeap<TItem, TPriority> where TPriority : IComparable<TPriority>
{
    private readonly List<(TItem Item, TPriority Priority)> _a = new();

    public int Count => _a.Count;

    public void Enqueue(TItem item, TPriority priority)
    {
        _a.Add((item, priority));
        SiftUp(_a.Count - 1);
    }

    public bool TryDequeue(out TItem item, out TPriority priority)
    {
        if (_a.Count == 0) { item = default!; priority = default!; return false; }

        (item, priority) = _a[0];
        var last = _a[^1];
        _a.RemoveAt(_a.Count - 1);
        if (_a.Count > 0) { _a[0] = last; SiftDown(0); }
        return true;
    }

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (_a[i].Priority.CompareTo(_a[parent].Priority) >= 0) break;
            (_a[i], _a[parent]) = (_a[parent], _a[i]);
            i = parent;
        }
    }

    private void SiftDown(int i)
    {
        while (true)
        {
            int left = 2 * i + 1, right = left + 1, smallest = i;
            if (left < _a.Count && _a[left].Priority.CompareTo(_a[smallest].Priority) < 0) smallest = left;
            if (right < _a.Count && _a[right].Priority.CompareTo(_a[smallest].Priority) < 0) smallest = right;
            if (smallest == i) return;
            (_a[i], _a[smallest]) = (_a[smallest], _a[i]);
            i = smallest;
        }
    }
}
