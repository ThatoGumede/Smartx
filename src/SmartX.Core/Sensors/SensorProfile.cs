namespace SmartX.Core.Sensors;

/// <summary>
/// A registered Smart-X sensor / node profile - simulated or attached to a real ESP32
/// device. This is the record the configuration dashboard creates, lists and attaches
/// media files to.
/// </summary>
public class SensorProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required string MacAddress { get; set; }

    /// <summary>Friendly device name, e.g. "Pump A" or "Moisture Sensor B".</summary>
    public string Name { get; set; } = string.Empty;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"{Category} {Id}" : Name;

    /// <summary>Human-readable deployment location, e.g. "Facility A / Zone 1 / Sub-Zone B / Node 4".</summary>
    public required string DeploymentLocation { get; set; }

    public SensorCategory Category { get; set; }
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeen { get; set; }
    public List<MediaAttachment> Attachments { get; set; } = [];
}
