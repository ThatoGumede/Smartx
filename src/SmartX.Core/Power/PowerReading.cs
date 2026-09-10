namespace SmartX.Core.Power;

/// <summary>
/// Represents a single smart-meter wattage reading. Operators are overloaded so calling
/// code can combine or compare two meters exactly the way the brief describes:
/// <c>Meter3 = Meter1 + Meter2</c> to get the aggregate load of two meters, or
/// <c>Meter1 &gt; Meter2</c> to flag which of two zones is currently drawing more power.
/// </summary>
public readonly struct PowerReading : IEquatable<PowerReading>, IComparable<PowerReading>
{
    public string MeterId { get; }
    public double Watts { get; }
    public DateTimeOffset Timestamp { get; }

    public PowerReading(string meterId, double watts, DateTimeOffset? timestamp = null)
    {
        MeterId = meterId ?? string.Empty;
        Watts = watts;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }

    /// <summary>Aggregates the load of two meters, e.g. combining two smart plugs on one circuit.</summary>
    public static PowerReading operator +(PowerReading a, PowerReading b) =>
        new($"{a.MeterId}+{b.MeterId}", a.Watts + b.Watts);

    /// <summary>Computes the delta between two meters, e.g. drop-off across a distribution point.</summary>
    public static PowerReading operator -(PowerReading a, PowerReading b) =>
        new($"{a.MeterId}-{b.MeterId}", a.Watts - b.Watts);

    public static bool operator ==(PowerReading a, PowerReading b) => a.Equals(b);
    public static bool operator !=(PowerReading a, PowerReading b) => !a.Equals(b);

    /// <summary>True when meter <paramref name="a"/> is currently drawing more than meter <paramref name="b"/>.</summary>
    public static bool operator >(PowerReading a, PowerReading b) => a.Watts > b.Watts;
    public static bool operator <(PowerReading a, PowerReading b) => a.Watts < b.Watts;
    public static bool operator >=(PowerReading a, PowerReading b) => a.Watts >= b.Watts;
    public static bool operator <=(PowerReading a, PowerReading b) => a.Watts <= b.Watts;

    public bool Equals(PowerReading other) =>
        MeterId == other.MeterId && Watts.Equals(other.Watts);

    public override bool Equals(object? obj) => obj is PowerReading other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(MeterId, Watts);

    public int CompareTo(PowerReading other) => Watts.CompareTo(other.Watts);

    public override string ToString() => $"{MeterId}: {Watts:0.##} W";
}
