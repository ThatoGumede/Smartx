namespace SmartX.Core.Commands;

/// <summary>One actuator's state before and after a manual override.</summary>
public record StateChange(string SensorId, string Label, bool Previous, bool Current);

/// <summary>
/// A manual override issued by an operator. A command can touch several devices (e.g. "shut down
/// all valves"), so it carries every change it made - that is what lets Undo revert it in one step.
/// </summary>
public class DeviceCommand
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public required string Description { get; set; }
    public required List<StateChange> Changes { get; set; }
    public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsUndo { get; set; }
    public bool Undone { get; set; }
}
