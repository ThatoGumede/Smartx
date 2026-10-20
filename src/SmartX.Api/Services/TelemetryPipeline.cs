using SmartX.Core.Alerts;
using SmartX.Core.Dashboard;
using SmartX.Core.Sensors;
using SmartX.Core.Telemetry;

namespace SmartX.Api.Services;

public record QueuedPacket(TelemetryRecord Record, SensorCategory Category, double Severity, DateTimeOffset EnqueuedAt, long Sequence);

public record ProcessedEntry(long Order, string SensorId, string Label, string Lane, string Value, double WaitedMs, DateTimeOffset ProcessedAt);

public record PipelineStats(int StandardDepth, int PriorityDepth, long ProcessedStandard, long ProcessedPriority, bool Paused, IReadOnlyList<ProcessedEntry> Recent);

/// <summary>
/// Two-lane intake for incoming telemetry.
///  - Standard lane: a <see cref="Queue{T}"/>, strict first-in-first-out.
///  - Priority lane: a <see cref="PriorityQueue{TElement,TPriority}"/> for critical readings (a power
///    spike, a moisture crash). Whenever anything is waiting in this lane it is served before the
///    standard queue, so a critical alert never sits behind a backlog of routine packets. Within the
///    lane the worst overshoot comes out first.
/// The API endpoints only enqueue and return immediately (202 Accepted); <see cref="TelemetryProcessorService"/>
/// drains the lanes in the background.
/// </summary>
public class TelemetryPipeline
{
    private const int RecentLimit = 50;

    private readonly Queue<QueuedPacket> _standard = new();
    private readonly PriorityQueue<QueuedPacket, (double NegSeverity, long Sequence)> _priority = new();
    private readonly LinkedList<ProcessedEntry> _recent = new();
    private readonly object _lock = new();
    private long _sequence, _order, _processedStandard, _processedPriority;

    public bool Paused { get; private set; }

    /// <summary>Routes the record to a lane and returns which one ("standard" or "priority").</summary>
    public string Enqueue(TelemetryRecord record, SensorCategory category)
    {
        var result = AlertClassifier.Classify(category, record.NumericValue);
        lock (_lock)
        {
            var packet = new QueuedPacket(record, category, result.Severity, DateTimeOffset.UtcNow, ++_sequence);
            if (result.Status == SensorStatus.Critical)
            {
                // PriorityQueue dequeues the smallest priority first, so negate severity.
                _priority.Enqueue(packet, (-result.Severity, packet.Sequence));
                return "priority";
            }

            _standard.Enqueue(packet);
            return "standard";
        }
    }

    public (QueuedPacket Packet, string Lane)? TryDequeue()
    {
        lock (_lock)
        {
            if (Paused) return null;
            if (_priority.TryDequeue(out var urgent, out _)) return (urgent, "priority");
            if (_standard.TryDequeue(out var routine)) return (routine, "standard");
            return null;
        }
    }

    public void RecordProcessed(QueuedPacket packet, string lane, string label)
    {
        lock (_lock)
        {
            if (lane == "priority") _processedPriority++; else _processedStandard++;
            var waited = (DateTimeOffset.UtcNow - packet.EnqueuedAt).TotalMilliseconds;
            _recent.AddFirst(new ProcessedEntry(++_order, packet.Record.SensorId, label, lane,
                packet.Record.DisplayValue + packet.Record.Unit, Math.Round(waited, 1), DateTimeOffset.UtcNow));
            if (_recent.Count > RecentLimit) _recent.RemoveLast();
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_lock) Paused = paused;
    }

    public PipelineStats GetStats()
    {
        lock (_lock)
            return new PipelineStats(_standard.Count, _priority.Count, _processedStandard, _processedPriority, Paused, _recent.ToList());
    }
}
