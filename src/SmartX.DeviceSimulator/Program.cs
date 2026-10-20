using System.Net.Http.Json;

// Simulated ESP32 fleet. Everything goes to the gateway over HTTP, exactly as physical devices would:
// register once, then report readings by MAC address to POST /api/ingest. Actuators also poll
// GET /api/devices/{mac}/desired-state to find out what the operator has commanded.

var baseUrl = Environment.GetEnvironmentVariable("SMARTX_API_URL") ?? (args.Length > 0 ? args[0] : "http://localhost:5050");
var intervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("SMARTX_TICK_SECONDS"), out var s) ? s : 3;

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(5) };
var rng = new Random();

Console.WriteLine($"Smart-X device simulator -> {baseUrl} (tick every {intervalSeconds}s)");

await WaitForGateway();
await RegisterFleet();

var tick = 0;
var quietUntil = new Dictionary<string, DateTime>();

while (true)
{
    tick++;
    int sent = 0, quiet = 0, failed = 0;
    try
    {
        var devices = await http.GetFromJsonAsync<List<Device>>("/api/sensors") ?? [];
        foreach (var d in devices)
        {
            // Occasionally a node drops off the mesh for 20-40 seconds.
            if (quietUntil.TryGetValue(d.MacAddress, out var until) && DateTime.UtcNow < until) { quiet++; continue; }
            if (d.Name is not ("Moisture Sensor B" or "Pump A") && rng.NextDouble() < 0.015)
            {
                quietUntil[d.MacAddress] = DateTime.UtcNow.AddSeconds(rng.Next(20, 41));
                quiet++;
                continue;
            }

            var reading = await BuildReading(d, tick);
            var response = await http.PostAsJsonAsync("/api/ingest", reading);
            if (response.IsSuccessStatusCode) sent++; else failed++;
        }
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"[{DateTime.Now:T}] gateway unreachable: {ex.Message}");
    }

    Console.WriteLine($"[{DateTime.Now:T}] tick {tick}: sent {sent}, silent {quiet}, rejected {failed}");
    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds));
}

async Task WaitForGateway()
{
    while (true)
    {
        try { (await http.GetAsync("/")).EnsureSuccessStatusCode(); return; }
        catch { Console.WriteLine("Waiting for the gateway..."); await Task.Delay(2000); }
    }
}

async Task RegisterFleet()
{
    var existing = (await http.GetFromJsonAsync<List<Device>>("/api/sensors") ?? []).Select(d => d.MacAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var fleet = new (string Mac, string Name, string Location, string Category)[]
    {
        ("24:6F:28:AA:01:01", "Moisture Sensor A", "Facility A / Zone 1 / Sub-Zone A / Node 1", "Environmental"),
        ("24:6F:28:AA:01:02", "Moisture Sensor B", "Facility A / Zone 1 / Sub-Zone B / Node 2", "Environmental"),
        ("24:6F:28:AA:01:03", "Humidity Node C", "Facility A / Zone 1 / Sub-Zone B / Node 3", "Environmental"),
        ("24:6F:28:AA:02:01", "Smart Meter 1", "Facility A / Zone 2 / Sub-Zone A / Node 1", "PowerConsumption"),
        ("24:6F:28:AA:02:02", "Smart Meter 2", "Facility A / Zone 2 / Sub-Zone A / Node 2", "PowerConsumption"),
        ("24:6F:28:AA:03:01", "Pump A", "Facility A / Zone 3 / Sub-Zone A / Node 1", "Actuator"),
        ("24:6F:28:AA:03:02", "Smart Relay 1", "Facility A / Zone 3 / Sub-Zone A / Node 2", "Actuator"),
        ("24:6F:28:AA:03:03", "Valve 1", "Facility A / Zone 3 / Sub-Zone B / Node 1", "Actuator"),
    };

    foreach (var f in fleet.Where(f => !existing.Contains(f.Mac)))
    {
        var r = await http.PostAsJsonAsync("/api/sensors", new { macAddress = f.Mac, name = f.Name, deploymentLocation = f.Location, category = f.Category });
        Console.WriteLine($"Registered {f.Name} ({f.Mac}): {(int)r.StatusCode}");
    }
}

async Task<object> BuildReading(Device d, int tickNumber)
{
    switch (d.Category)
    {
        case "Environmental":
            double moisture;
            if (d.Name == "Moisture Sensor B")
            {
                // Scripted 30-tick cycle: steady, a ramp down to a critical dip, a short hold, then recovery.
                var t = tickNumber % 30;
                moisture = t < 10 ? 48 + rng.NextDouble() * 4
                         : t < 20 ? 50 - (t - 9) * 4.4 + rng.NextDouble()
                         : t < 24 ? 6 + rng.NextDouble() * 2
                         : 8 + (t - 23) * 6.5 + rng.NextDouble();
            }
            else
            {
                moisture = rng.NextDouble() < 0.03 ? 96 + rng.NextDouble() * 4 : 38 + rng.NextDouble() * 20;
            }
            return new { macAddress = d.MacAddress, dataType = "float", number = Math.Round(moisture, 1), unit = "%" };

        case "PowerConsumption":
            var watts = rng.NextDouble() < 0.04 ? 2850 + rng.Next(0, 300) : 900 + rng.Next(0, 1100);
            return new { macAddress = d.MacAddress, dataType = "int", number = (double)watts, unit = "W" };

        default:
            var desired = await http.GetFromJsonAsync<DesiredState>($"/api/devices/{Uri.EscapeDataString(d.MacAddress)}/desired-state");
            return new { macAddress = d.MacAddress, dataType = "bool", flag = desired?.On ?? false, unit = "state" };
    }
}

record Device(string Id, string MacAddress, string Name, string Category);
record DesiredState(bool On);
