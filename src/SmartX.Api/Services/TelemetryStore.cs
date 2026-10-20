using SmartX.Core.Telemetry;

namespace SmartX.Api.Services;

/// <summary>
/// Historical sensor logs. Each sensor owns a <see cref="SortedDictionary{TKey,TValue}"/> keyed by
/// timestamp, so the history is always in chronological order no matter what order packets arrive
/// in - a reconnecting node can upload a backfilled batch of old readings and they slot into the
/// right place. The timeline graphs read straight off the sorted values. A separate dictionary
/// holds the latest reading per sensor so "current value" is O(1).
/// </summary>
public class TelemetryStore
{
    private const int MaxHistoryPerSensor = 500;

    private readonly Dictionary<string, SortedDictionary<DateTimeOffset, TelemetryRecord>> _history = new();
    private readonly Dictionary<string, TelemetryRecord> _latest = new();
    private readonly object _lock = new();

    public void Add(TelemetryRecord record)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(record.SensorId, out var log))
                _history[record.SensorId] = log = new SortedDictionary<DateTimeOffset, TelemetryRecord>();

            // Two packets can share a timestamp; nudge by a tick so neither is lost.
            var key = record.Timestamp;
            while (log.ContainsKey(key)) key = key.AddTicks(1);
            log[key] = record;

            if (log.Count > MaxHistoryPerSensor)
                log.Remove(log.Keys.First());

            if (!_latest.TryGetValue(record.SensorId, out var current) || record.Timestamp >= current.Timestamp)
                _latest[record.SensorId] = record;
        }
    }

    public void AddRange(IEnumerable<TelemetryRecord> records)
    {
        foreach (var record in records) Add(record);
    }

    /// <summary>The most recent <paramref name="take"/> readings, oldest first.</summary>
    public IReadOnlyList<TelemetryRecord> GetHistory(string sensorId, int take = 50)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(sensorId, out var log)) return [];
            return log.Values.Skip(Math.Max(0, log.Count - take)).ToList();
        }
    }

    public TelemetryRecord? GetLatest(string sensorId)
    {
        lock (_lock) return _latest.GetValueOrDefault(sensorId);
    }

    /// <summary>Numeric view of the timeline (booleans become 1/0) for sparklines and trend analysis.</summary>
    public List<(DateTimeOffset At, double Value)> GetPoints(string sensorId, int take)
    {
        return GetHistory(sensorId, take)
            .Select(r => (r.Timestamp, r.NumericValue ?? (r.BoolValue == true ? 1d : 0d)))
            .ToList();
    }
}
