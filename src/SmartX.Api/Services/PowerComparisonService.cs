using SmartX.Core.Power;

namespace SmartX.Api.Services;

/// <summary>
/// Bridges the generic telemetry store back into the <see cref="PowerReading"/> value type so
/// the /api/power endpoints can demonstrate the overloaded +, -, and comparison operators on
/// real (mock) meter data, e.g. aggregating two smart meters' load or flagging an imbalance
/// between two zones.
/// </summary>
public class PowerComparisonService(TelemetryStore telemetry)
{
    public PowerReading? GetLatestReading(string sensorId)
    {
        var latest = telemetry.GetLatest(sensorId);
        if (latest is null || latest.NumericValue is null)
            return null;

        return new PowerReading(sensorId, latest.NumericValue.Value, latest.Timestamp);
    }

    /// <summary>
    /// Returns the aggregate (operator +), delta (operator -) and a boolean comparison
    /// (operator &gt;) between two meters' most recent readings.
    /// </summary>
    public PowerComparisonResult? Compare(string meterIdA, string meterIdB)
    {
        var a = GetLatestReading(meterIdA);
        var b = GetLatestReading(meterIdB);
        if (a is null || b is null)
            return null;

        PowerReading readingA = a.Value;
        PowerReading readingB = b.Value;

        return new PowerComparisonResult(
            Aggregate: (readingA + readingB).Watts,   // operator +
            Delta: (readingA - readingB).Watts,       // operator -
            AIsHigher: readingA > readingB,           // operator >
            AreEqual: readingA == readingB             // operator ==
        );
    }
}

public record PowerComparisonResult(double Aggregate, double Delta, bool AIsHigher, bool AreEqual);
