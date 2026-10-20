using SmartX.Core.Commands;
using SmartX.Core.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Manual override commands for actuators (pumps, relays, valves). Every issued command is pushed onto
/// a <see cref="Stack{T}"/>; Undo pops the top entry and restores the previous state of everything it
/// changed - including bulk commands such as "shut down all valves", which are a single stack entry.
/// A separate list keeps the full, ordered history (including the undos) for display.
/// </summary>
public class CommandService(SensorStore sensors, ActionEngine engine)
{
    private readonly Dictionary<string, bool> _desired = new();   // actuator id -> desired ON/OFF
    private readonly Stack<DeviceCommand> _undoStack = new();
    private readonly List<DeviceCommand> _history = new();
    private readonly object _lock = new();

    public int UndoDepth { get { lock (_lock) return _undoStack.Count; } }

    public bool GetDesiredState(string sensorId)
    {
        lock (_lock) return _desired.GetValueOrDefault(sensorId);
    }

    public (DeviceCommand? Command, string? Error) Issue(string sensorId, bool on)
    {
        var sensor = sensors.Get(sensorId);
        if (sensor is null) return (null, $"Device '{sensorId}' is not registered.");
        if (sensor.Category != SensorCategory.Actuator) return (null, $"{sensor.DisplayName} is not an actuator and cannot take commands.");

        DeviceCommand command;
        lock (_lock)
        {
            var previous = _desired.GetValueOrDefault(sensorId);
            _desired[sensorId] = on;
            command = new DeviceCommand
            {
                Description = $"{sensor.DisplayName} {(on ? "ON" : "OFF")}",
                Changes = [new StateChange(sensorId, sensor.DisplayName, previous, on)],
            };
            _undoStack.Push(command);
            _history.Add(command);
        }

        engine.RecordCommand(sensorId, on ? "ON" : "OFF");
        return (command, null);
    }

    /// <summary>Sets every actuator to the same state in one undoable command.</summary>
    public (DeviceCommand? Command, string? Error) IssueAll(bool on)
    {
        var actuators = sensors.GetAll().Where(s => s.Category == SensorCategory.Actuator).ToList();
        if (actuators.Count == 0) return (null, "No actuators are registered.");

        lock (_lock)
        {
            var changes = actuators
                .Select(a => new StateChange(a.Id, a.DisplayName, _desired.GetValueOrDefault(a.Id), on))
                .Where(c => c.Previous != c.Current)
                .ToList();

            if (changes.Count == 0) return (null, $"All actuators are already {(on ? "ON" : "OFF")}.");

            foreach (var c in changes) _desired[c.SensorId] = on;
            var command = new DeviceCommand
            {
                Description = $"All actuators {(on ? "ON" : "OFF")} ({changes.Count} device(s))",
                Changes = changes,
            };
            _undoStack.Push(command);
            _history.Add(command);
            return (command, null);
        }
    }

    public (DeviceCommand? Command, string? Error) Undo()
    {
        lock (_lock)
        {
            if (!_undoStack.TryPop(out var original)) return (null, "There is nothing to undo.");

            foreach (var c in original.Changes) _desired[c.SensorId] = c.Previous;
            original.Undone = true;

            var undo = new DeviceCommand
            {
                Description = $"UNDO: {original.Description}",
                IsUndo = true,
                Changes = original.Changes.Select(c => new StateChange(c.SensorId, c.Label, c.Current, c.Previous)).ToList(),
            };
            _history.Add(undo);
            return (undo, null);
        }
    }

    public List<DeviceCommand> History(int take = 20)
    {
        lock (_lock) return _history.AsEnumerable().Reverse().Take(take).ToList();
    }
}
