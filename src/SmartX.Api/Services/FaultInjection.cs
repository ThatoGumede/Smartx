namespace SmartX.Api.Services;

/// <summary>
/// Test-lab switch for demos: the simulator polls this set and stays silent for any MAC in it, so a node
/// "fails" the way a real one would (it simply stops reporting). Nothing here edits a status directly;
/// the gateway still has to notice the silence on its own.
/// </summary>
public class FaultInjection
{
    private readonly HashSet<string> _silenced = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Silence(string mac) { lock (_lock) _silenced.Add(mac); }
    public void Restore(string mac) { lock (_lock) _silenced.Remove(mac); }
    public List<string> Current() { lock (_lock) return _silenced.ToList(); }
}
