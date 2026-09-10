namespace SmartX.Core.Telemetry;

/// <summary>
/// Holds sequential historical batches of raw float telemetry (e.g. a burst of soil-moisture
/// samples an ESP32 node uploads after reconnecting from a Wi-Fi dropout) as a <b>jagged
/// array</b> - <c>float[][]</c> - before they are validated and transferred into the
/// optimised <see cref="List{T}"/> collection the rest of the API works with.
///
/// A jagged array (rather than a rectangular <c>float[,]</c>) is the correct fit here
/// because each batch an ESP32 node uploads can legitimately contain a different number of
/// samples - one batch might be a single reading, the next a 200-sample burst recorded while
/// the node was offline. A rectangular array would force every batch to be padded to the
/// length of the largest one, wasting memory on a resource-constrained gateway; a jagged
/// array lets row _i_ (<c>_batches[i]</c>) be exactly as long as batch _i_ needs to be.
/// </summary>
public class TelemetryBatchBuffer
{
    private float[][] _batches = [];

    public int BatchCount => _batches.Length;

    /// <summary>Loads a new set of raw batches, replacing whatever was buffered before.</summary>
    public void LoadBatches(float[][] batches) => _batches = batches ?? [];

    /// <summary>
    /// Walks the jagged array batch-by-batch, sample-by-sample, and produces the flat,
    /// timestamp-ordered <see cref="List{T}"/> of strongly-typed packets that the rest of the
    /// system (storage, dashboard, API responses) consumes. This is the explicit
    /// "jagged array -&gt; optimised List&lt;T&gt;" transfer step required by the brief.
    /// </summary>
    public List<TelemetryPacket<float>> FlattenToPackets(string sensorId, string unit)
    {
        var packets = new List<TelemetryPacket<float>>();
        var baseline = DateTimeOffset.UtcNow;

        for (int batchIndex = 0; batchIndex < _batches.Length; batchIndex++)
        {
            float[] batch = _batches[batchIndex];
            for (int sampleIndex = 0; sampleIndex < batch.Length; sampleIndex++)
            {
                // Older batches are stamped further in the past so the dashboard can still
                // render a sensible chronological order for backfilled data.
                var stampedAt = baseline.AddSeconds(-(_batches.Length - batchIndex) * 5 + sampleIndex * 0.1);
                packets.Add(new TelemetryPacket<float>(sensorId, batch[sampleIndex], unit, stampedAt));
            }
        }

        return packets;
    }

    /// <summary>
    /// Simple aggregate statistics computed directly over the jagged array (without
    /// flattening first) - demonstrates working with the raw two-level structure, e.g. for a
    /// quick "largest batch" or "overall average" summary before committing to List&lt;T&gt;.
    /// </summary>
    public (int totalSamples, float average, int largestBatchSize) Summarise()
    {
        int total = 0;
        double sum = 0;
        int largest = 0;

        foreach (float[] batch in _batches)
        {
            total += batch.Length;
            largest = Math.Max(largest, batch.Length);
            foreach (float sample in batch)
                sum += sample;
        }

        float average = total == 0 ? 0f : (float)(sum / total);
        return (total, average, largest);
    }
}
