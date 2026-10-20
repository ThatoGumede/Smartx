using SmartX.Core.Dashboard;
using SmartX.Core.Sensors;
using SmartX.Core.Telemetry;

namespace SmartX.Api.Services;

public record AlertEvent(long Id, DateTimeOffset At, string SensorId, string Label, string Kind, string Message);

/// <summary>
/// Tracks which alert states are currently active using a <see cref="HashSet{T}"/> of "sensorId|Kind"
/// keys. A set only stores unique elements, so <c>Add</c> returns false when the same alert is already
/// active - that is how duplicate disconnect (or repeated critical) alerts for the same device are
/// recognised and dropped instead of being processed again. The number dropped is counted so it can be
/// shown and tested.
/// </summary>
public class AlertRegistry
{
    private readonly HashSet<string> _active = new();
    private readonly List<AlertEvent> _events = new();
    private readonly object _lock = new();
    private long _eventId, _suppressed;

    /// <summary>Raised once per genuinely new alert (never for suppressed duplicates).</summary>
    public event Action<AlertEvent>? AlertRaised;

    public long SuppressedDuplicates => Interlocked.Read(ref _suppressed);

    private static string Key(string id, string kind) => $"{id}|{kind}";

    public void OnReading(SensorProfile sensor, SensorStatus status, TelemetryRecord record)
    {
        var raised = new List<AlertEvent>();
        lock (_lock)
        {
            if (_active.Remove(Key(sensor.Id, "Disconnected")))
                Append(sensor, "Reconnected", $"{sensor.DisplayName} is reporting again.");

            var reading = record.DisplayValue + record.Unit;
            switch (status)
            {
                case SensorStatus.Critical:
                    _active.Remove(Key(sensor.Id, "Warning"));
                    TryRaise(sensor, "Critical", $"{sensor.DisplayName} reported {reading}.", raised);
                    break;
                case SensorStatus.Warning:
                    _active.Remove(Key(sensor.Id, "Critical"));
                    TryRaise(sensor, "Warning", $"{sensor.DisplayName} reported {reading}.", raised);
                    break;
                default:
                    var cleared = _active.Remove(Key(sensor.Id, "Critical")) | _active.Remove(Key(sensor.Id, "Warning"));
                    if (cleared) Append(sensor, "Recovered", $"{sensor.DisplayName} is back in range ({reading}).");
                    break;
            }
        }

        foreach (var e in raised) AlertRaised?.Invoke(e);
    }

    public void OnDisconnected(SensorProfile sensor)
    {
        var raised = new List<AlertEvent>();
        lock (_lock) TryRaise(sensor, "Disconnected", $"{sensor.DisplayName} has stopped reporting.", raised);
        foreach (var e in raised) AlertRaised?.Invoke(e);
    }

    public List<(string SensorId, string Kind)> ActiveSnapshot()
    {
        lock (_lock)
            return _active.Select(k => k.Split('|')).Select(p => (p[0], p[1])).ToList();
    }

    public List<AlertEvent> RecentEvents(int take = 20)
    {
        lock (_lock) return _events.AsEnumerable().Reverse().Take(take).ToList();
    }

    private void TryRaise(SensorProfile sensor, string kind, string message, List<AlertEvent> raised)
    {
        if (_active.Add(Key(sensor.Id, kind)))
            raised.Add(Append(sensor, kind, message));
        else
            Interlocked.Increment(ref _suppressed);
    }

    private AlertEvent Append(SensorProfile sensor, string kind, string message)
    {
        var e = new AlertEvent(++_eventId, DateTimeOffset.UtcNow, sensor.Id, sensor.DisplayName, kind, message);
        _events.Add(e);
        if (_events.Count > 100) _events.RemoveAt(0);
        return e;
    }
}
