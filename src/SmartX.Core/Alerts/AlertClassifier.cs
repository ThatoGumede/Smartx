using SmartX.Core.Dashboard;
using SmartX.Core.Sensors;

namespace SmartX.Core.Alerts;

/// <summary>Result of classifying one reading: its status band and a severity score used to order the priority queue.</summary>
public readonly record struct Classification(SensorStatus Status, double Severity);

/// <summary>
/// Single source of truth for threshold bands. The dashboard colours, the alert registry and the
/// priority queue lane all call this, so a reading can never be "critical" in one place and
/// "normal" in another.
/// </summary>
public static class AlertClassifier
{
    public const double EnvWarningLow = 20, EnvCriticalLow = 10;
    public const double EnvWarningHigh = 80, EnvCriticalHigh = 95;
    public const double PowerWarning = 2000, PowerCritical = 2800;

    public static Classification Classify(SensorCategory category, double? value)
    {
        if (value is not { } v)
            return new(SensorStatus.Normal, 0);

        switch (category)
        {
            case SensorCategory.Environmental:
                if (v >= EnvCriticalHigh) return new(SensorStatus.Critical, 1 + (v - EnvCriticalHigh) / EnvCriticalHigh);
                if (v <= EnvCriticalLow) return new(SensorStatus.Critical, 1 + (EnvCriticalLow - v) / EnvCriticalLow);
                if (v >= EnvWarningHigh || v <= EnvWarningLow) return new(SensorStatus.Warning, 0.5);
                break;

            case SensorCategory.PowerConsumption:
                if (v >= PowerCritical) return new(SensorStatus.Critical, 1 + (v - PowerCritical) / PowerCritical);
                if (v >= PowerWarning) return new(SensorStatus.Warning, 0.5);
                break;
        }

        return new(SensorStatus.Normal, 0);
    }
}
