# Smart-X IoT Mesh Ecosystem — Part 2: Real-Time Processing & Device Management

This builds on my Part 1 ingestion gateway. The API (`SmartX.Api`) now puts every incoming
reading through a two-lane queue before it is stored, tracks live devices and alert states in
dedicated collections, supports manual override commands with undo, and suggests actions based on
what the operator has done before. The dashboard (`SmartX.Client`) gains a Real-Time Command Stream
module, and a new console app (`SmartX.DeviceSimulator`) feeds the API the way real ESP32 devices
would: over HTTP, never by touching the API's memory directly.

```
smartx/
├── SmartX.sln
├── docker-compose.yml
├── start-all.cmd            # Windows: opens API, simulator and client in three windows
└── src/
    ├── SmartX.Core/            # domain types: TelemetryPacket<T>, PowerReading, AlertClassifier, DeviceCommand
    ├── SmartX.Api/             # Minimal API, queues, stores, alert registry, action engine
    ├── SmartX.Client/          # Razor Pages dashboard (browser JS talks to the API)
    └── SmartX.DeviceSimulator/ # simulated ESP32 fleet posting to /api/ingest
```

## Running it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). On Windows the quickest route is to
double-click `start-all.cmd` in the repo root, which opens the three windows below and then the browser.
Doing it by hand takes three terminals, started in this order:

```
cd src\SmartX.Api               &  dotnet run     (http://localhost:5050)
cd src\SmartX.DeviceSimulator   &  dotnet run     (registers 8 devices, then reports every 3 seconds)
cd src\SmartX.Client            &  dotnet run     (http://localhost:5100)
```

Open http://localhost:5100. With Docker instead, `docker compose up --build` starts all three
containers on the same ports. The simulator reaches the API by service name inside the Docker network,
while the client's `ApiBaseUrl` stays `http://localhost:5050` because its calls come from your browser.

## Where each Part 2 requirement lives

| Requirement | Where |
|---|---|
| Navigation menu, async navigation without losing state | `Pages/Shared/_Layout.cshtml`, `wwwroot/js/nav.js` |
| Command Stream page, overrides, search and filters | `Pages/CommandStream.cshtml`, `wwwroot/js/commandstream.js`, `GET /api/stream/snapshot` |
| `Queue<T>` for incoming telemetry (FIFO) | `Services/TelemetryPipeline.cs`, standard lane |
| `PriorityQueue` for critical alerts | `Services/TelemetryPipeline.cs`, priority lane |
| `Stack<T>` of override commands with Undo | `Services/CommandService.cs` |
| `Dictionary` of live devices keyed by MAC | `Services/SensorStore.cs`, used by `POST /api/ingest` |
| `SortedDictionary` of history by timestamp | `Services/TelemetryStore.cs` |
| `HashSet<T>` of active/duplicate alert states | `Services/AlertRegistry.cs` |
| Predictive engine, "Suggested Actions" | `Services/ActionEngine.cs`, shown in the Command Stream sidebar |

Navigation is handled by `nav.js`: clicking a menu link fetches the page in the background and swaps only
the main content, so the window, the navbar and the saved filters survive. Page timers are stopped when you
leave and restarted when you return. If the background fetch ever fails it falls back to a normal page load.
Filters are kept in `sessionStorage`, so they also survive a refresh.

## How the queue works

`POST /api/ingest` looks the device up by MAC, wraps the reading in the matching `TelemetryPacket<T>`, and
enqueues it, answering `202 Accepted` straight away. A background service drains the queues. Readings that
fall in the critical band (power at or above 2800 W, moisture at or below 10% or at or above 95%) go into a
`PriorityQueue` ordered by how far past the limit they are. That lane is always served before the standard
`Queue<T>`, so a critical packet does not wait behind routine ones. All threshold bands live in
`SmartX.Core/Alerts/AlertClassifier.cs`, which the dashboard colours and the alert registry also use.

## Testing the queue, dictionary, set and stack implementations

The Command Stream page has a Test lab panel that runs these through the same API a device uses.

**Simulate a high-priority alert.**
1. Click Pause processing. The pipeline panel shows PAUSED.
2. Click Queue 30 routine packets, then Inject critical power spike. Standard queue climbs; priority queue shows 1.
3. Click Resume processing. In the pipeline panel the critical reading shows lane `priority` and a lower
   `#` number than the routine packets, even though it was sent last.

The same thing from PowerShell:

```powershell
$api = "http://localhost:5050"
Invoke-RestMethod -Method Post "$api/api/pipeline/pause"
1..10 | ForEach-Object { Invoke-RestMethod -Method Post "$api/api/ingest" -ContentType application/json -Body '{"macAddress":"24:6F:28:AA:01:01","dataType":"float","number":45,"unit":"%"}' }
Invoke-RestMethod -Method Post "$api/api/ingest" -ContentType application/json -Body '{"macAddress":"24:6F:28:AA:02:01","dataType":"int","number":3100,"unit":"W"}'
Invoke-RestMethod "$api/api/pipeline/stats"          # standardDepth 10 or more, priorityDepth 1
Invoke-RestMethod -Method Post "$api/api/pipeline/resume"
(Invoke-RestMethod "$api/api/pipeline/stats").recent | Select-Object order, lane, value   # newest first
```

**Dictionary.** Reporting from a MAC that is not registered returns 404, and registering the same MAC twice
returns 409. Both are decided by the MAC-keyed dictionary, not a scan. MACs are case-insensitive.

**SortedDictionary.** Post a backfilled batch with `POST /api/telemetry/{sensorId}/batch`, body
`{"unit":"%","batches":[[31.1,32.2],[33.3]]}`. Its readings carry older timestamps than the live ones, yet
`GET /api/telemetry/{sensorId}` returns everything in timestamp order and the sensor's current value is unchanged.

**HashSet.** Close the simulator window and wait about 15 seconds. `GET /api/alerts` lists one active
Disconnected alert per device, while `suppressedDuplicates` keeps rising: the monitor re-reports every silent
device every two seconds and the set rejects the repeats. Start the simulator again and the alerts clear.

**Stack.** On Command Stream, turn Pump A ON, then use All actuators ON, then Undo last command. The bulk
command is reverted as one step, Pump A stays ON, and the history shows the undo.

## Predictive engine

It learns from three signals. First, a weighted graph of "device raised an alert, operator then searched for
or commanded another device within two minutes". After a pair has been seen twice, the next alert on the first
device produces a suggestion such as "Moisture Sensor B is Critical: turn Pump A ON", with an Apply button.
Second, a least-squares trend over each sensor's recent readings flags a device that is still normal but
projected to cross a warning line within 90 seconds. Third, a z-score check flags readings far outside a
sensor's own recent behaviour. Devices named in a suggestion are outlined on the tiles.

To see it quickly, click Load sample pattern history in the Test lab. This pre-loads three occurrences of
"Moisture Sensor B alerts, operator turns Pump A ON", and is labelled as demo data in the activity log. The
simulator drives Moisture Sensor B through a 90-second cycle (steady, ramp down, critical dip, recovery): the
trend suggestion appears about 45 seconds in, and the Pump A suggestion appears when the critical dip starts.
Without the sample history, search for Pump A or press ON during two dips and the engine learns it for real.

## API additions

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/ingest` | Device reading by MAC (`dataType` float, int or bool); returns 202 and the lane used |
| GET | `/api/devices/{mac}/desired-state` | What an actuator has been commanded to do |
| GET | `/api/stream/snapshot?q=&category=&status=` | Filtered devices with sparkline data |
| GET | `/api/pipeline/stats` | Queue depths, counts, recently processed packets |
| POST | `/api/pipeline/pause`, `/api/pipeline/resume` | Hold or release processing (for testing) |
| GET | `/api/alerts` | Active alerts, recent events, duplicates suppressed |
| POST | `/api/commands`, `/api/commands/all`, `/api/commands/undo` | Override one actuator, all actuators, or undo |
| GET | `/api/commands/history` | Command history and undo depth |
| GET | `/api/insights` | Suggested actions, learned patterns, recent activity |
| POST | `/api/activity/search`, `/api/insights/seed-demo` | Report a search; load sample pattern history |

The Part 1 endpoints still work. The id-based telemetry routes now return 202 because they also go through the queue.

## Notes

State is in memory, so restarting the API clears devices, history and learned patterns; the simulator
re-registers its fleet on its next start. The mock data is simulated, but it reaches the API only over HTTP.
There is no authentication yet.
I tested the API, the simulator and the page scripts end to end on Linux with .NET 10, including a headless run
of the Command Stream script. I have not run the Docker images or `start-all.cmd` on Windows myself.
