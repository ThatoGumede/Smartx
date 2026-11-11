using System.Net.Http.Json;

// Simulated ESP32 fleet. Everything goes to the gateway over HTTP, exactly as physical devices would:
//  - register once,
//  - upload a stored 24-hour log (history backfill),
//  - report neighbour tables (who each node can hear, with RSSI) so the gateway can build the mesh graph,
//  - report readings by MAC to POST /api/ingest,
//  - actuators poll GET /api/devices/{mac}/desired-state to learn what the operator has commanded,
//  - every node checks GET /api/simulation/faults and goes silent if the test lab has "failed" it.

var baseUrl = Environment.GetEnvironmentVariable("SMARTX_API_URL") ?? (args.Length > 0 ? args[0] : "http://localhost:5050");
var intervalSeconds = int.TryParse(Environment.GetEnvironmentVariable("SMARTX_TICK_SECONDS"), out var s) ? s : 3;
const double RadioRangeMetres = 55;

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
var rng = new Random();

// Physical layout in metres, gateway at (0,0). Two nodes can hear each other if they are within radio range.
var fleet = new Spec[]
{
    new("24:6F:28:AA:01:01", "Moisture Sensor A", "Facility A / Zone 1 / Sub-Zone A / Node 1", "Environmental", 25, 15),
    new("24:6F:28:AA:01:02", "Moisture Sensor B", "Facility A / Zone 1 / Sub-Zone B / Node 2", "Environmental", 48, 30),
    new("24:6F:28:AA:01:03", "Humidity Node C", "Facility A / Zone 1 / Sub-Zone B / Node 3", "Environmental", 70, 10),
    new("24:6F:28:AA:01:04", "Moisture Sensor D", "Facility A / Zone 1 / Sub-Zone C / Node 1", "Environmental", 92, 32),
    new("24:6F:28:AA:02:01", "Smart Meter 1", "Facility A / Zone 2 / Sub-Zone A / Node 1", "PowerConsumption", -30, 20),
    new("24:6F:28:AA:02:02", "Smart Meter 2", "Facility A / Zone 2 / Sub-Zone A / Node 2", "PowerConsumption", -55, 42),
    new("24:6F:28:AA:02:03", "Smart Meter 3", "Facility A / Zone 2 / Sub-Zone B / Node 1", "PowerConsumption", -82, 18),
    new("24:6F:28:AA:03:01", "Pump A", "Facility A / Zone 3 / Sub-Zone A / Node 1", "Actuator", 15, -35),
    new("24:6F:28:AA:03:02", "Smart Relay 1", "Facility A / Zone 3 / Sub-Zone A / Node 2", "Actuator", 45, -45),
    new("24:6F:28:AA:03:03", "Valve 1", "Facility A / Zone 3 / Sub-Zone B / Node 1", "Actuator", 78, -38),
    new("24:6F:28:AA:03:04", "Valve 2", "Facility A / Zone 3 / Sub-Zone B / Node 2", "Actuator", -25, -42),
};

Console.WriteLine($"Smart-X device simulator -> {baseUrl} (tick every {intervalSeconds}s)");

await WaitForGateway();
await RegisterFleet();
await UploadHistory();
await ReportLinks();

var tick = 0;
var quietUntil = new Dictionary<string, DateTime>();

while (true)
{
    tick++;
    int sent = 0, quiet = 0, failed = 0;
    try
    {
        var faults = (await http.GetFromJsonAsync<List<string>>("/api/simulation/faults") ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var devices = await http.GetFromJsonAsync<List<Device>>("/api/sensors") ?? [];
        foreach (var d in devices)
        {
            if (faults.Contains(d.MacAddress)) { quiet++; continue; }   // failed from the test lab

            // Occasionally a node drops off the mesh for 20-40 seconds on its own.
            if (quietUntil.TryGetValue(d.MacAddress, out var until) && DateTime.UtcNow < until) { quiet++; continue; }
            if (d.Name is not ("Moisture Sensor B" or "Pump A") && rng.NextDouble() < 0.004)
            {
                quietUntil[d.MacAddress] = DateTime.UtcNow.AddSeconds(rng.Next(20, 41));
                quiet++;
                continue;
            }

            var reading = await BuildReading(d, tick);
            var response = await http.PostAsJsonAsync("/api/ingest", reading);
            if (response.IsSuccessStatusCode) sent++; else failed++;
        }

        if (tick % 20 == 0) await ReportLinks();   // neighbour tables are refreshed now and then
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
    foreach (var f in fleet.Where(f => !existing.Contains(f.Mac)))
    {
        var r = await http.PostAsJsonAsync("/api/sensors", new { macAddress = f.Mac, name = f.Name, deploymentLocation = f.Location, category = f.Category });
        Console.WriteLine($"Registered {f.Name} ({f.Mac}): {(int)r.StatusCode}");
    }
}

// A node that has been collecting readings while offline uploads its stored log in one go.
async Task UploadHistory()
{
    var now = DateTimeOffset.UtcNow;
    foreach (var f in fleet)
    {
        var stats = await http.GetFromJsonAsync<ArchiveStats>($"/api/history/{Uri.EscapeDataString(f.Mac)}/stats");
        if (stats is { Count: >= 100 }) continue;

        var points = new List<object>();
        var on = false;
        for (var i = 1440; i >= 1; i--)
        {
            var t = now.AddMinutes(-i);
            var minuteOfDay = t.Hour * 60 + t.Minute;
            var day = Math.Sin((minuteOfDay - 360) / 1440.0 * 2 * Math.PI);   // peaks around midday
            switch (f.Category)
            {
                case "Environmental":
                    points.Add(new { timestamp = t, number = Math.Round(46 + 9 * day + (rng.NextDouble() - 0.5) * 4, 1) });
                    break;
                case "PowerConsumption":
                    points.Add(new { timestamp = t, number = (double)(int)(1250 + 650 * day + rng.Next(-120, 121)) });
                    break;
                default:
                    if (rng.NextDouble() < 1 / 70.0) on = !on;
                    points.Add(new { timestamp = t, flag = on });
                    break;
            }
        }

        var type = f.Category switch { "Environmental" => "float", "PowerConsumption" => "int", _ => "bool" };
        var unit = f.Category switch { "Environmental" => "%", "PowerConsumption" => "W", _ => "state" };
        var r = await http.PostAsJsonAsync("/api/history/backfill", new { macAddress = f.Mac, dataType = type, unit, readings = points });
        Console.WriteLine($"Uploaded 24h history for {f.Name}: {(int)r.StatusCode}");
    }
}

// Each node reports the neighbours it can hear. RSSI follows free-space path loss: -30 - 20*log10(distance).
async Task ReportLinks()
{
    foreach (var f in fleet)
    {
        var neighbours = new List<object>();
        var toGateway = Math.Sqrt(f.X * f.X + f.Y * f.Y);
        if (toGateway <= RadioRangeMetres) neighbours.Add(new { macAddress = "GATEWAY", rssi = Rssi(toGateway) });

        foreach (var other in fleet.Where(o => o.Mac != f.Mac))
        {
            var d = Math.Sqrt(Math.Pow(f.X - other.X, 2) + Math.Pow(f.Y - other.Y, 2));
            if (d <= RadioRangeMetres) neighbours.Add(new { macAddress = other.Mac, rssi = Rssi(d) });
        }

        await http.PostAsJsonAsync("/api/mesh/links", new { macAddress = f.Mac, neighbours });
    }
    Console.WriteLine("Neighbour tables reported.");
}

static double Rssi(double distanceMetres) => Math.Round(-30 - 20 * Math.Log10(Math.Max(distanceMetres, 1)), 1);

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

record Spec(string Mac, string Name, string Location, string Category, double X, double Y);
record Device(string Id, string MacAddress, string Name, string Category);
record DesiredState(bool On);
record ArchiveStats(int Count);
