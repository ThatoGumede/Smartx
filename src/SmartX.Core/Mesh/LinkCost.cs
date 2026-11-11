namespace SmartX.Core.Mesh;

/// <summary>
/// Turns the signal strength a node measures for a neighbour into a routing cost. Free-space path loss
/// gives RSSI = -30 - 20*log10(distance), so distance can be recovered from RSSI. Transmit energy needed
/// to cover a link grows roughly with distance squared, so cost = d^2/100 + 1, where the +1 is a fixed
/// per-hop overhead that stops the router preferring many tiny hops. Lower cost means cheaper in power.
/// </summary>
public static class LinkCost
{
    public static double DistanceFromRssi(double rssi) => Math.Pow(10, (-rssi - 30) / 20.0);

    public static double FromRssi(double rssi) => Math.Round(Math.Pow(DistanceFromRssi(rssi), 2) / 100.0 + 1.0, 2);
}
