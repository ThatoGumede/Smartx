namespace SmartX.Core.Deployment;

/// <summary>
/// One node in a nested device-deployment tree, e.g. Facility A -&gt; Zone 1 -&gt; Sub-Zone B
/// -&gt; Node 4. Whole trees are posted to the API and checked with
/// <see cref="DeploymentValidator"/> before a sensor is allowed to reference a location.
/// </summary>
public class DeploymentNode
{
    public required string Name { get; set; }

    /// <summary>Expected values, in order, are Facility, Zone, SubZone, Node.</summary>
    public required string Tier { get; set; }

    public bool HasSensorAssigned { get; set; }
    public List<DeploymentNode> Children { get; set; } = [];
}
