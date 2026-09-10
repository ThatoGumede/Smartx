namespace SmartX.Core.Dashboard;

/// <summary>
/// The four pre-attentive states a telemetry tile can render as on the configuration
/// dashboard, directly implementing the engagement strategy chosen in the Part 1 research
/// report (real-time visual feedback via pre-attentive colour/outline encoding rather than a
/// generic progress bar).
/// </summary>
public enum SensorStatus
{
    /// <summary>Reading within its normal band and a recent heartbeat - rendered green.</summary>
    Normal,

    /// <summary>Reading approaching its threshold - rendered amber.</summary>
    Warning,

    /// <summary>Reading has breached its threshold - rendered red, the pop-out colour.</summary>
    Critical,

    /// <summary>No heartbeat within the expected publish interval - rendered grey/dashed.</summary>
    Disconnected
}

/// <summary>Snapshot the dashboard polls every few seconds for one sensor tile.</summary>
public class SensorStateSnapshot
{
    public required string SensorId { get; set; }
    public required string DisplayLabel { get; set; }
    public SensorStatus Status { get; set; }
    public string LatestValue { get; set; } = "-";
    public DateTimeOffset? LastSeen { get; set; }
}
