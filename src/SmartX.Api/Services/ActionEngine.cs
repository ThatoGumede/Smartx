using SmartX.Core.Alerts;
using SmartX.Core.Dashboard;
using SmartX.Core.Sensors;

namespace SmartX.Api.Services;

public record Suggestion(string Kind, string Title, string Detail, string? SensorId, string? CommandAction, double Confidence);
public record LearnedPattern(string Trigger, string Target, int Count, double Confidence);
public record ActivityEntry(DateTimeOffset At, string Kind, string Subject, string Note);

/// <summary>
/// Automated Action Engine. Three techniques feed the "Suggested Actions" panel:
///
/// 1. Association learning. Whenever a device raises an alert and the operator then searches for, or
///    sends a command to, another device within a couple of minutes, the pair (alerting device -> acted-on
///    device) gets its count bumped in a weighted directed graph, a dictionary of dictionaries. Confidence
///    is count / number of times the alerting device has alerted. Once a pair has been seen at least
///    twice, the next alert on that device produces a suggestion, for example "Moisture Sensor B is
///    critical - you usually turn Pump A ON".
/// 2. Trend projection. A least-squares line over a device's recent readings (read from the sorted
///    timeline) projects whether a still-normal reading will cross a warning threshold within 90 seconds.
/// 3. Statistical anomaly detection. A z-score of the latest reading against the preceding window flags
///    readings that are wildly out of character while still inside the normal band.
/// </summary>
public class ActionEngine
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(2);
    private const int MinSupport = 2;

    private sealed class Occurrence(string sensorId, DateTimeOffset at)
    {
        public string SensorId { get; } = sensorId;
        public DateTimeOffset At { get; } = at;
        public HashSet<string> ActedOn { get; } = new();
    }

    private readonly AlertRegistry _alerts;
    private readonly SensorStore _sensors;
    private readonly TelemetryStore _telemetry;
    private readonly object _lock = new();

    private readonly List<Occurrence> _recentAlerts = new();
    private readonly Dictionary<string, int> _triggerCounts = new();
    private readonly Dictionary<string, Dictionary<string, int>> _assoc = new();          // trigger -> target -> count
    private readonly Dictionary<string, Dictionary<string, int>> _actionChoices = new();   // "trigger|target" -> action -> count
    private readonly List<ActivityEntry> _activity = new();

    public ActionEngine(AlertRegistry alerts, SensorStore sensors, TelemetryStore telemetry)
    {
        _alerts = alerts;
        _sensors = sensors;
        _telemetry = telemetry;
        _alerts.AlertRaised += OnAlert;
    }

    private void OnAlert(AlertEvent e)
    {
        if (e.Kind is not ("Critical" or "Warning" or "Disconnected")) return;
        lock (_lock)
        {
            _recentAlerts.Add(new Occurrence(e.SensorId, e.At));
            Bump(_triggerCounts, e.SensorId, 1);
            _recentAlerts.RemoveAll(o => e.At - o.At > TimeSpan.FromMinutes(30));
        }
    }

    public void RecordSearch(string term)
    {
        term = term.Trim();
        if (term.Length < 2) return;
        var matches = _sensors.GetAll()
            .Where(s => s.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Take(3).ToList();

        lock (_lock)
        {
            AddActivity("Search", term, matches.Count == 0 ? "no device matched" : $"{matches.Count} device(s) matched");
            foreach (var m in matches) Learn(m.Id, "search");
        }
    }

    public void RecordCommand(string sensorId, string action)
    {
        var name = _sensors.Get(sensorId)?.DisplayName ?? sensorId;
        lock (_lock)
        {
            AddActivity("Command", name, action);
            Learn(sensorId, action);
        }
    }

    // Caller holds the lock.
    private void Learn(string targetId, string action)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var occ in _recentAlerts)
        {
            if (occ.SensorId == targetId || now - occ.At > Window) continue;
            if (!occ.ActedOn.Add(targetId)) continue; // one lesson per alert occurrence per target

            if (!_assoc.TryGetValue(occ.SensorId, out var targets))
                _assoc[occ.SensorId] = targets = new Dictionary<string, int>();
            Bump(targets, targetId, 1);

            var key = $"{occ.SensorId}|{targetId}";
            if (!_actionChoices.TryGetValue(key, out var choices))
                _actionChoices[key] = choices = new Dictionary<string, int>();
            Bump(choices, action, 1);
        }
    }

    /// <summary>
    /// Demo helper: pre-loads three past occurrences of "Moisture Sensor B alerts, operator turns Pump A ON"
    /// so the learned-pattern suggestion can be shown without waiting for real history to build up.
    /// </summary>
    public string SeedDemoHistory()
    {
        var all = _sensors.GetAll();
        var trigger = all.FirstOrDefault(s => s.Name == "Moisture Sensor B") ?? all.FirstOrDefault(s => s.Category == SensorCategory.Environmental);
        var target = all.FirstOrDefault(s => s.Name == "Pump A") ?? all.FirstOrDefault(s => s.Category == SensorCategory.Actuator);
        if (trigger is null || target is null) return "Need at least one environmental sensor and one actuator registered.";

        lock (_lock)
        {
            Bump(_triggerCounts, trigger.Id, 3);
            if (!_assoc.TryGetValue(trigger.Id, out var targets)) _assoc[trigger.Id] = targets = new Dictionary<string, int>();
            Bump(targets, target.Id, 3);
            var key = $"{trigger.Id}|{target.Id}";
            if (!_actionChoices.TryGetValue(key, out var choices)) _actionChoices[key] = choices = new Dictionary<string, int>();
            Bump(choices, "ON", 3);
            AddActivity("Demo seed", $"{trigger.DisplayName} -> {target.DisplayName}", "3 historical occurrences loaded");
        }
        return $"Loaded history: after {trigger.DisplayName} alerts, operator turned {target.DisplayName} ON (3 times).";
    }

    public List<LearnedPattern> GetPatterns(int take = 5)
    {
        lock (_lock)
        {
            return _assoc
                .SelectMany(t => t.Value.Select(v => new { Trigger = t.Key, Target = v.Key, v.Value }))
                .Select(x => new LearnedPattern(Label(x.Trigger), Label(x.Target), x.Value,
                    Math.Round(Math.Min(1.0, (double)x.Value / Math.Max(1, _triggerCounts.GetValueOrDefault(x.Trigger))), 2)))
                .OrderByDescending(p => p.Count).ThenByDescending(p => p.Confidence)
                .Take(take).ToList();
        }
    }

    public List<ActivityEntry> RecentActivity(int take = 8)
    {
        lock (_lock) return _activity.AsEnumerable().Reverse().Take(take).ToList();
    }

    public List<Suggestion> GetSuggestions()
    {
        var suggestions = new List<Suggestion>();
        var all = _sensors.GetAll();
        var active = _alerts.ActiveSnapshot();

        lock (_lock)
        {
            foreach (var (triggerId, kind) in active)
            {
                if (!_assoc.TryGetValue(triggerId, out var targets)) continue;
                var denominator = Math.Max(1, _triggerCounts.GetValueOrDefault(triggerId));

                foreach (var (targetId, count) in targets.OrderByDescending(t => t.Value))
                {
                    if (count < MinSupport) continue;
                    var confidence = Math.Min(1.0, (double)count / denominator);
                    var action = _actionChoices.GetValueOrDefault($"{triggerId}|{targetId}")?
                        .OrderByDescending(a => a.Value).First().Key ?? "search";
                    var evidence = $"{Label(triggerId)} is {kind}. After this alert you acted on {Label(targetId)} {count} time(s) ({confidence:P0}).";

                    suggestions.Add(action is "ON" or "OFF"
                        ? new Suggestion("Command", $"Turn {Label(targetId)} {action}", evidence, targetId, action, confidence)
                        : new Suggestion("Investigate", $"Check {Label(targetId)}", evidence, targetId, null, confidence));
                }
            }
        }

        foreach (var sensor in all.Where(s => s.Category != SensorCategory.Actuator))
        {
            var trend = TrendInsight(sensor);
            if (trend is not null) suggestions.Add(trend);
            var anomaly = AnomalyInsight(sensor);
            if (anomaly is not null) suggestions.Add(anomaly);
        }

        return suggestions
            .GroupBy(s => (s.Kind, s.SensorId, s.CommandAction))
            .Select(g => g.OrderByDescending(s => s.Confidence).First())
            .OrderBy(s => s.Kind is "Command" or "Investigate" ? 0 : 1)
            .ThenByDescending(s => s.Confidence)
            .ToList();
    }

    private Suggestion? TrendInsight(SensorProfile sensor)
    {
        var pts = _telemetry.GetPoints(sensor.Id, 10);
        if (pts.Count < 6 || DateTimeOffset.UtcNow - pts[^1].At > TimeSpan.FromSeconds(10)) return null;
        if (AlertClassifier.Classify(sensor.Category, pts[^1].Value).Status != SensorStatus.Normal) return null;

        var t0 = pts[0].At;
        var xs = pts.Select(p => (p.At - t0).TotalSeconds).ToArray();
        var ys = pts.Select(p => p.Value).ToArray();
        var (slope, intercept, r2) = Fit(xs, ys);
        if (r2 < 0.8 || Math.Abs(slope) < 1e-6) return null;

        var last = ys[^1];
        const double horizon = 90;
        var projected = intercept + slope * (xs[^1] + horizon);

        double? threshold = null;
        string unit = "";
        if (sensor.Category == SensorCategory.Environmental)
        {
            unit = "%";
            if (slope < 0 && projected <= AlertClassifier.EnvWarningLow) threshold = AlertClassifier.EnvWarningLow;
            else if (slope > 0 && projected >= AlertClassifier.EnvWarningHigh) threshold = AlertClassifier.EnvWarningHigh;
        }
        else if (sensor.Category == SensorCategory.PowerConsumption)
        {
            unit = " W";
            if (slope > 0 && projected >= AlertClassifier.PowerWarning) threshold = AlertClassifier.PowerWarning;
        }

        if (threshold is not { } limit) return null;
        var seconds = Math.Max(0, (limit - last) / slope);
        return new Suggestion("Trend",
            $"{sensor.DisplayName} is heading for {limit:0}{unit}",
            $"Currently {last:0.#}{unit}, moving {Math.Abs(slope):0.##}{unit}/s. Projected to cross the warning line in about {seconds:0}s (fit R² {r2:0.00}).",
            sensor.Id, null, r2);
    }

    private Suggestion? AnomalyInsight(SensorProfile sensor)
    {
        var pts = _telemetry.GetPoints(sensor.Id, 20);
        if (pts.Count < 10 || DateTimeOffset.UtcNow - pts[^1].At > TimeSpan.FromSeconds(10)) return null;
        if (AlertClassifier.Classify(sensor.Category, pts[^1].Value).Status != SensorStatus.Normal) return null;

        var baseline = pts.Take(pts.Count - 1).Select(p => p.Value).ToArray();
        var mean = baseline.Average();
        var std = Math.Sqrt(baseline.Sum(v => (v - mean) * (v - mean)) / baseline.Length);
        if (std < 1e-6) return null;

        var z = (pts[^1].Value - mean) / std;
        if (Math.Abs(z) < 3) return null;
        return new Suggestion("Anomaly", $"{sensor.DisplayName} reading is out of character",
            $"Latest value {pts[^1].Value:0.#} is {Math.Abs(z):0.0} standard deviations from its recent average ({mean:0.#}).",
            sensor.Id, null, Math.Min(1, Math.Abs(z) / 6));
    }

    private static (double Slope, double Intercept, double R2) Fit(double[] x, double[] y)
    {
        var n = x.Length;
        double mx = x.Average(), my = y.Average();
        double sxx = 0, sxy = 0, syy = 0;
        for (var i = 0; i < n; i++)
        {
            sxx += (x[i] - mx) * (x[i] - mx);
            sxy += (x[i] - mx) * (y[i] - my);
            syy += (y[i] - my) * (y[i] - my);
        }
        if (sxx == 0 || syy == 0) return (0, my, 0);
        var slope = sxy / sxx;
        return (slope, my - slope * mx, (sxy * sxy) / (sxx * syy));
    }

    private string Label(string sensorId) => _sensors.Get(sensorId)?.DisplayName ?? sensorId;

    private void AddActivity(string kind, string subject, string note)
    {
        _activity.Add(new ActivityEntry(DateTimeOffset.UtcNow, kind, subject, note));
        if (_activity.Count > 100) _activity.RemoveAt(0);
    }

    private static void Bump(Dictionary<string, int> map, string key, int by) => map[key] = map.GetValueOrDefault(key) + by;
}
