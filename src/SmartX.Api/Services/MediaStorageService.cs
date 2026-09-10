using SmartX.Core.Sensors;

namespace SmartX.Api.Services;

/// <summary>
/// Saves device configuration files, deployment photos and hardware logs uploaded through the
/// multipart endpoint to disk, under a per-sensor folder, and returns the metadata record that
/// gets attached to the sensor's profile.
/// </summary>
public class MediaStorageService
{
    private readonly string _rootPath;

    public MediaStorageService(IWebHostEnvironment env)
    {
        _rootPath = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<MediaAttachment> SaveAsync(string sensorId, IFormFile file, CancellationToken ct)
    {
        var sensorFolder = Path.Combine(_rootPath, sensorId);
        Directory.CreateDirectory(sensorFolder);

        var attachment = new MediaAttachment
        {
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            SizeBytes = file.Length,
        };

        var storedName = $"{attachment.Id}_{attachment.FileName}";
        var fullPath = Path.Combine(sensorFolder, storedName);

        await using (var stream = File.Create(fullPath))
        {
            await file.CopyToAsync(stream, ct);
        }

        attachment.StoragePath = Path.Combine(sensorId, storedName);
        return attachment;
    }

    public string ResolvePhysicalPath(string storagePath) => Path.Combine(_rootPath, storagePath);
}
