using SmartX.Core.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Live registry of devices. The primary index is a <see cref="Dictionary{TKey,TValue}"/> keyed by
/// MAC address, so when a telemetry packet arrives the owning device is found with an O(1) hash
/// lookup rather than a scan. A second dictionary maps the short API id back to the MAC so the
/// id-based endpoints are also O(1). A plain Dictionary is not thread-safe, so every access
/// goes through one lock.
/// </summary>
public class SensorStore
{
    private readonly Dictionary<string, SensorProfile> _byMac = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _macById = new();
    private readonly object _lock = new();

    public static string NormaliseMac(string mac) => mac.Trim().ToUpperInvariant();

    /// <summary>Registers a device. Returns false (and the existing profile) if the MAC is already registered.</summary>
    public bool TryRegister(SensorProfile profile, out SensorProfile result)
    {
        lock (_lock)
        {
            profile.MacAddress = NormaliseMac(profile.MacAddress);
            if (_byMac.TryGetValue(profile.MacAddress, out var existing))
            {
                result = existing;
                return false;
            }

            _byMac[profile.MacAddress] = profile;
            _macById[profile.Id] = profile.MacAddress;
            result = profile;
            return true;
        }
    }

    /// <summary>O(1) lookup used on the ingest hot path.</summary>
    public SensorProfile? GetByMac(string mac)
    {
        lock (_lock) return _byMac.GetValueOrDefault(NormaliseMac(mac));
    }

    public SensorProfile? Get(string id)
    {
        lock (_lock) return _macById.TryGetValue(id, out var mac) ? _byMac[mac] : null;
    }

    public IReadOnlyList<SensorProfile> GetAll()
    {
        lock (_lock) return _byMac.Values.OrderBy(s => s.DeploymentLocation).ThenBy(s => s.DisplayName).ToList();
    }

    public bool Remove(string id)
    {
        lock (_lock)
        {
            if (!_macById.Remove(id, out var mac)) return false;
            return _byMac.Remove(mac);
        }
    }

    public void Touch(string id, DateTimeOffset seenAt)
    {
        lock (_lock)
        {
            if (_macById.TryGetValue(id, out var mac)) _byMac[mac].LastSeen = seenAt;
        }
    }

    public bool AddAttachment(string sensorId, MediaAttachment attachment)
    {
        lock (_lock)
        {
            if (!_macById.TryGetValue(sensorId, out var mac)) return false;
            _byMac[mac].Attachments.Add(attachment);
            return true;
        }
    }
}
