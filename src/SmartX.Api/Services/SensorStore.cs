using System.Collections.Concurrent;
using SmartX.Core.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Thread-safe in-memory registry of sensor profiles. Deliberately not backed by a database:
/// the ingestion gateway needs to stay lightweight on constrained hardware, and a
/// ConcurrentDictionary gives us safe concurrent access from the Minimal API endpoints and
/// the background mock-telemetry generator without any external dependency.
/// </summary>
public class SensorStore
{
    private readonly ConcurrentDictionary<string, SensorProfile> _sensors = new();

    public SensorProfile Register(SensorProfile profile)
    {
        _sensors[profile.Id] = profile;
        return profile;
    }

    public IReadOnlyCollection<SensorProfile> GetAll() =>
        _sensors.Values.OrderBy(s => s.DeploymentLocation).ToList();

    public SensorProfile? Get(string id) => _sensors.GetValueOrDefault(id);

    public bool Remove(string id) => _sensors.TryRemove(id, out _);

    public void Touch(string id, DateTimeOffset seenAt)
    {
        if (_sensors.TryGetValue(id, out var profile))
            profile.LastSeen = seenAt;
    }

    public bool AddAttachment(string sensorId, MediaAttachment attachment)
    {
        if (!_sensors.TryGetValue(sensorId, out var profile))
            return false;

        profile.Attachments.Add(attachment);
        return true;
    }
}
