namespace SmartX.Core.Sensors;

/// <summary>
/// Metadata for a physical device configuration file, deployment photo, or hardware log
/// attached to a specific sensor profile via the multipart upload endpoint.
/// </summary>
public class MediaAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Relative path under the API's upload root where the file bytes are stored.</summary>
    public string StoragePath { get; set; } = string.Empty;
}
