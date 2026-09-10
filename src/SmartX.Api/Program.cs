using System.Text.Json.Serialization;
using SmartX.Api.Endpoints;
using SmartX.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string ClientCorsPolicy = "SmartXClient";

builder.Services.AddCors(options =>
{
    options.AddPolicy(ClientCorsPolicy, policy =>
    {
        // Origins the Razor Pages client is served from (local dev + Docker compose service name).
        var allowedOrigins = builder.Configuration.GetSection("AllowedClientOrigins").Get<string[]>()
            ?? ["http://localhost:5100"];

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Sensor category / status enums are transported as readable strings ("Environmental",
    // "Critical") rather than raw integers, which keeps the client's JS trivial.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Singletons: the whole point of these stores is to hold state in memory for the lifetime of
// the gateway process, shared between every request and the background mock-data generator.
builder.Services.AddSingleton<SensorStore>();
builder.Services.AddSingleton<TelemetryStore>();
builder.Services.AddSingleton<MediaStorageService>();
builder.Services.AddSingleton<PowerComparisonService>();
builder.Services.AddHostedService<MockTelemetryBackgroundService>();

var app = builder.Build();

app.UseCors(ClientCorsPolicy);

app.MapGet("/", () => Results.Ok(new
{
    service = "Smart-X Ingestion & Validation Gateway",
    status = "running",
    endpoints = new[]
    {
        "/api/sensors", "/api/sensors/{id}/media", "/api/telemetry/{sensorId}/{float|int|bool|batch}",
        "/api/dashboard/state", "/api/power/compare/{a}/{b}", "/api/deployment/validate",
    },
}));

app.MapSensorEndpoints();
app.MapMediaEndpoints();
app.MapTelemetryEndpoints();
app.MapDeploymentEndpoints();
app.MapDashboardEndpoints();
app.MapPowerEndpoints();

app.Run();
