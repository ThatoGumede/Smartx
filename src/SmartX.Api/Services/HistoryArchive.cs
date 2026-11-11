using System.Diagnostics;
using SmartX.Core.Collections;
using SmartX.Core.Telemetry;

namespace SmartX.Api.Services;

public record HistoryQueryResult(
    int Count, int NodesVisited, int TotalStored, int TreeHeight, int MinimumHeight, double ElapsedMs, bool Truncated,
    double? Min, double? Max, double? Average, int? OnCount,
    DateTimeOffset? OldestStored, DateTimeOffset? NewestStored, List<TelemetryRecord> Readings);

public record ArchiveStats(string SensorId, int Count, int Height, int MinimumHeight, long Rotations, DateTimeOffset? Oldest, DateTimeOffset? Newest);

/// <summary>
/// Long-term telemetry history, one <see cref="AvlTree{TKey,TValue}"/> per sensor keyed by timestamp. The
/// hot store (TelemetryStore) keeps the last few hundred readings for live tiles; this archive holds up to
/// 20,000 per sensor and answers "everything between 14:00 and 16:00" by walking only the part of the tree
/// that overlaps the range. Once a sensor passes its cap, the oldest reading is removed from the tree.
/// </summary>
public class HistoryArchive
{
    private const int MaxPerSensor = 20_000;

    private readonly Dictionary<string, AvlTree<DateTimeOffset, TelemetryRecord>> _trees = new();
    private readonly object _lock = new();

    public void Add(TelemetryRecord record)
    {
        lock (_lock) AddLocked(record);
    }

    public int AddRange(IEnumerable<TelemetryRecord> records)
    {
        var n = 0;
        lock (_lock)
            foreach (var r in records) { AddLocked(r); n++; }
        return n;
    }

    private void AddLocked(TelemetryRecord record)
    {
        if (!_trees.TryGetValue(record.SensorId, out var tree))
            _trees[record.SensorId] = tree = new AvlTree<DateTimeOffset, TelemetryRecord>();

        var key = record.Timestamp;
        while (tree.TryGet(key, out _)) key = key.AddTicks(1);   // keep both readings if timestamps collide
        tree.Insert(key, record);

        if (tree.Count > MaxPerSensor && tree.TryGetMin(out var oldest, out _))
            tree.Remove(oldest);
    }

    public ArchiveStats Stats(string sensorId)
    {
        lock (_lock)
        {
            if (!_trees.TryGetValue(sensorId, out var t)) return new ArchiveStats(sensorId, 0, 0, 0, 0, null, null);
            DateTimeOffset? oldest = t.TryGetMin(out var lo, out _) ? lo : null;
            DateTimeOffset? newest = t.TryGetMax(out var hi, out _) ? hi : null;
            return new ArchiveStats(sensorId, t.Count, t.Height, t.MinimumPossibleHeight, t.Rotations, oldest, newest);
        }
    }

    public HistoryQueryResult Query(string sensorId, DateTimeOffset from, DateTimeOffset to, int take)
    {
        lock (_lock)
        {
            if (!_trees.TryGetValue(sensorId, out var tree))
                return new HistoryQueryResult(0, 0, 0, 0, 0, 0, false, null, null, null, null, null, null, []);

            var timer = Stopwatch.StartNew();
            var result = tree.Range(from, to);
            timer.Stop();

            var records = result.Items.Select(p => p.Value).ToList();
            var numeric = records.Where(r => r.NumericValue is not null).Select(r => r.NumericValue!.Value).ToList();
            int? onCount = records.Any(r => r.BoolValue is not null) ? records.Count(r => r.BoolValue == true) : null;

            DateTimeOffset? oldest = tree.TryGetMin(out var lo, out _) ? lo : null;
            DateTimeOffset? newest = tree.TryGetMax(out var hi, out _) ? hi : null;

            return new HistoryQueryResult(
                records.Count, result.NodesVisited, tree.Count, tree.Height, tree.MinimumPossibleHeight,
                Math.Round(timer.Elapsed.TotalMilliseconds, 3), records.Count > take,
                numeric.Count > 0 ? Math.Round(numeric.Min(), 2) : null,
                numeric.Count > 0 ? Math.Round(numeric.Max(), 2) : null,
                numeric.Count > 0 ? Math.Round(numeric.Average(), 2) : null,
                onCount, oldest, newest,
                records.Skip(Math.Max(0, records.Count - take)).ToList());
        }
    }
}
