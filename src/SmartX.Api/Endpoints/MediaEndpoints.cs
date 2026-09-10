using SmartX.Api.Services;

namespace SmartX.Api.Endpoints;

public static class MediaEndpoints
{
    public static void MapMediaEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sensors/{sensorId}/media").WithTags("Media");

        // Optimised multipart file uploader: config files, deployment photos, hardware logs.
        group.MapPost("/", async (string sensorId, IFormFile? file, SensorStore sensorStore,
            MediaStorageService storage, CancellationToken ct) =>
        {
            if (file is null || file.Length == 0)
                return Results.BadRequest("No file was supplied.");

            if (sensorStore.Get(sensorId) is null)
                return Results.NotFound($"Sensor '{sensorId}' is not registered.");

            const long maxBytes = 20 * 1024 * 1024; // 20 MB cap keeps the gateway resource-friendly
            if (file.Length > maxBytes)
                return Results.BadRequest("File exceeds the 20 MB upload limit.");

            var attachment = await storage.SaveAsync(sensorId, file, ct);
            sensorStore.AddAttachment(sensorId, attachment);

            return Results.Created($"/api/sensors/{sensorId}/media/{attachment.Id}", attachment);
        }).DisableAntiforgery();

        group.MapGet("/", (string sensorId, SensorStore sensorStore) =>
        {
            var sensor = sensorStore.Get(sensorId);
            return sensor is null ? Results.NotFound() : Results.Ok(sensor.Attachments);
        });

        group.MapGet("/{mediaId}/download", (string sensorId, string mediaId,
            SensorStore sensorStore, MediaStorageService storage) =>
        {
            var sensor = sensorStore.Get(sensorId);
            var attachment = sensor?.Attachments.FirstOrDefault(a => a.Id == mediaId);
            if (attachment is null)
                return Results.NotFound();

            var path = storage.ResolvePhysicalPath(attachment.StoragePath);
            if (!File.Exists(path))
                return Results.NotFound();

            return Results.File(path, attachment.ContentType, attachment.FileName);
        });
    }
}
