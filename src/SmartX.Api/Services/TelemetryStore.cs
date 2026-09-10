using System.Collections.Concurrent;
using SmartX.Core.Dashboard;
using SmartX.Core.Telemetry;

namespace SmartX.Api.Services;

/// <summary>
/// Stores the recent telemetry history for every sensor and derives the pre-attentive
/// <see cref="SensorStatus"/> the dashboard renders (see the Part 1 research report:
/// colour/outline encoding rather than a plain progress bar). A bounded deque-like list per
/// sensor keeps memory use predictable even under the heavy mock load the brief asks for.
/// </summary>
public class TelemetryStore
{
    private const int MaxHistoryPerSensor = 200;
    private static readonly TimeSpan DisconnectAfter = TimeSpan.FromSeconds(12);

    private readonly ConcurrentDictionary<string, List<TelemetryRecord>> _history = new();
    private readonly object _lock = new();

    public void Add(TelemetryRecord record)
    {
        lock (_lock)
        {
            var list = _history.GetOrAdd(record.SensorId, _ => []);
            list.Add(record);
            if (list.Count > MaxHistoryPerSensor)
                list.RemoveAt(0);
        }
    }

    public void AddRange(IEnumerable<TelemetryRecord> records)
    {
        foreach (var record in records)
            Add(record);
    }

    public IReadOnlyList<TelemetryRecord> GetHistory(string sensorId, int take = 50)
    {
        if (!_history.TryGetValue(sensorId, out var list))
            return [];

        lock (_lock)
        {
            return list.Skip(Math.Max(0, list.Count - take)).ToList();
        }
    }

    public TelemetryRecord? GetLatest(string sensorId)
    {
        if (!_history.TryGetValue(sensorId, out var list) || list.Count == 0)
            return null;

        lock (_lock)
        {
            return list[^1];
        }
    }

    /// <summary>
    /// Derives the pre-attentive status for one sensor: grey/Disconnected if nothing has
    /// arrived recently, red/Critical if the latest numeric reading breaches its band,
    /// amber/Warning if it is close, otherwise green/Normal.
    /// </summary>
    public SensorStatus ComputeStatus(string sensorId, double warningBand, double criticalBand)
    {
        var latest = GetLatest(sensorId);
        if (latest is null)
            return SensorStatus.Disconnected;

        if (DateTimeOffset.UtcNow - latest.Timestamp > DisconnectAfter)
            return SensorStatus.Disconnected;

        if (latest.NumericValue is not { } value)
            return SensorStatus.Normal; // boolean/actuator channels don't use threshold bands

        double magnitude = Math.Abs(value);
        if (magnitude >= criticalBand) return SensorStatus.Critical;
        if (magnitude >= warningBand) return SensorStatus.Warning;
        return SensorStatus.Normal;
    }
}
