# Smart-X IoT Mesh Ecosystem — Part 1: Ingestion & Validation Gateway

This is my Part 1 implementation for the Smart-X Portfolio of Evidence: a .NET 10 Minimal
API ingestion gateway (`SmartX.Api`) and a Razor Pages configuration dashboard
(`SmartX.Client`), sharing a domain class library (`SmartX.Core`).

## Architecture

```
smartx/
├── SmartX.sln
├── docker-compose.yml
├── src/
│   ├── SmartX.Core/     # domain library — no ASP.NET dependency
│   ├── SmartX.Api/      # ASP.NET Core Minimal API — the ingestion/validation gateway
│   └── SmartX.Client/   # ASP.NET Core Razor Pages — the configuration dashboard UI
└── README.md
```

The client is a separate process from the API and talks to it purely over HTTP, from
JavaScript running in the browser (`wwwroot/js/*.js`). There's no server-to-server
proxying between them. I did this deliberately so the "frontend communicates with the API
layer to push and retrieve data" requirement stays unambiguous, and so the two halves can be
deployed or replaced independently — which fits the brief's Web-First option of an ASP.NET
Core Minimal API backend paired with a browser-based frontend.

**Why Razor Pages instead of Blazor or React.** All three functional requirements could have
been met with any of the permitted frontend choices. I went with Razor Pages because the
whole solution then compiles and runs from a single `dotnet` toolchain, with no separate
frontend build step — no `npm install`, no WebAssembly download. Given that the brief says no
marks are awarded at all if the code doesn't compile and run, I wanted to remove as many moving
parts as I could. Bootstrap is vendored locally under `wwwroot/lib` by the project template, so
there's no CDN dependency either.

## Where the required C# concepts live

| Concept | File | What's there |
|---|---|---|
| Generics | `SmartX.Core/Telemetry/TelemetryPacket.cs` | `TelemetryPacket<T>` is a `readonly struct` constrained to `where T : struct`, so `TelemetryPacket<float>`, `<int>` and `<bool>` are separate value types and nothing gets boxed. Instantiated for all three sensor payload types in `SmartX.Api/Endpoints/TelemetryEndpoints.cs`. |
| Operator overloading | `SmartX.Core/Power/PowerReading.cs` | Overloads `+`, `-`, `==`, `!=`, `<`, `>`, `<=`, `>=` on `PowerReading`. Exercised through `GET /api/power/compare/{a}/{b}` in `SmartX.Api/Services/PowerComparisonService.cs`. |
| Jagged arrays + List\<T\> | `SmartX.Core/Telemetry/TelemetryBatchBuffer.cs` | Historical batches come in as `float[][]` — each batch can be a different length — and `FlattenToPackets()` transfers them into a `List<TelemetryPacket<float>>`. Wired up through `POST /api/telemetry/{sensorId}/batch`. |
| Recursion | `SmartX.Core/Deployment/DeploymentValidator.cs` | `ValidateRecursive` walks a `DeploymentNode` tree (Facility → Zone → SubZone → Node) tier by tier, recursing into every child. Exposed through `POST /api/deployment/validate`. |

## Where the functional requirements live

| Requirement | Implementation |
|---|---|
| Landing page with 3 pillars (1 active, 2 disabled) | `SmartX.Client/Pages/Index.cshtml` |
| Web API ingestion layer | `SmartX.Api`, everything under `/api/...` |
| Sensor payload registration (MAC, location, category) | `POST /api/sensors`, UI at `SmartX.Client/Pages/Sensors.cshtml` |
| Media/log attachment via multipart upload | `POST /api/sensors/{id}/media`, `SmartX.Api/Services/MediaStorageService.cs` |
| Dynamic engagement feature (not a progress bar) | `SmartX.Client/Pages/Dashboard.cshtml` with `wwwroot/js/dashboard.js` and `wwwroot/css/site.css` — pre-attentive colour and outline-coded tiles with a heartbeat pulse tied to data freshness, carrying through the strategy I justified in the Part 1 research report |
| Heavy mock telemetry seeding under load | `SmartX.Api/Services/MockTelemetryBackgroundService.cs` — seeds 5 default sensors and generates a new reading for every registered sensor, including ones added through the UI, every 3 seconds, with occasional simulated spikes and disconnects |

## Getting it running

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) — `dotnet --version`
should report `10.x`. Docker is optional. Neither project needs a database or any NuGet feed
beyond the default nuget.org; both only depend on the ASP.NET Core shared framework.

The API and client are independent processes, so run them in two terminals.

Terminal 1, the ingestion gateway:
```bash
cd src/SmartX.Api
dotnet restore
dotnet build
dotnet run
# listens on http://localhost:5050
```

Terminal 2, the configuration dashboard:
```bash
cd src/SmartX.Client
dotnet restore
dotnet build
dotnet run
# listens on http://localhost:5100
```

Then open http://localhost:5100. The API seeds 5 default sensors a few seconds after
starting, so the dashboard should show tiles straight away.

If you just want to confirm the whole solution builds without running anything, do it from
the repo root:
```bash
dotnet restore SmartX.sln
dotnet build SmartX.sln
```
You'd still `dotnet run` the API and client separately afterwards, since they're two
independently hosted processes.

## Running with Docker Compose

```bash
docker compose up --build
```
This builds and starts both containers — the API published at http://localhost:5050, the
client at http://localhost:5100. Stop with `docker compose down` (add `-v` if you also want
to clear the named upload volume).

One thing worth flagging: the client's `ApiBaseUrl` is set to the host-published API address
(`http://localhost:5050`), not an internal Docker service name. Every API call happens
client-side, from JavaScript running in your browser, and your browser sits outside the
Docker network — it can only reach either container through its published port.

## Trying it out

1. Open http://localhost:5100. The landing page shows the three architectural pillars; only
   *Sensor Data Ingestion & Telemetry* is active.
2. Open Live Dashboard. Five default sensors are already reporting mock telemetry, colour
   coded green/amber/red with a live heartbeat pulse. Give it a minute or two and you should
   see one grey out (disconnected) or flash red (critical).
3. Open Sensor Ingestion and register a new sensor with a MAC address, location, and
   category. It appears in the table immediately, and shows up on the dashboard within about
   3 seconds once the background service picks it up.
4. Attach a file — a config file, a photo, a log, whatever — to any sensor through the Attach
   control.
5. In the Deployment Tree Validation panel, click Load Sample Tree then Validate to see the
   recursive validator pass. Change a node's `tier` value to something out of order and
   validate again to see it fail with a specific error.
6. Hit `GET http://localhost:5050/api/power/compare/{meterIdA}/{meterIdB}` with two of the
   seeded `PowerConsumption` sensor IDs (grab them from `GET /api/sensors`) to see the
   overloaded-operator output: `aggregate`, `delta`, `aIsHigher`, `areEqual`.

## API reference

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/sensors` | Register a sensor profile |
| GET | `/api/sensors` | List all sensors |
| GET | `/api/sensors/{id}` | Get one sensor |
| DELETE | `/api/sensors/{id}` | Remove a sensor |
| POST | `/api/sensors/{id}/media` | Multipart file upload (config/photo/log) |
| GET | `/api/sensors/{id}/media` | List attachments |
| GET | `/api/sensors/{id}/media/{mediaId}/download` | Download an attachment |
| POST | `/api/telemetry/{sensorId}/float` | Ingest a `TelemetryPacket<float>` reading |
| POST | `/api/telemetry/{sensorId}/int` | Ingest a `TelemetryPacket<int>` reading |
| POST | `/api/telemetry/{sensorId}/bool` | Ingest a `TelemetryPacket<bool>` reading |
| POST | `/api/telemetry/{sensorId}/batch` | Ingest a jagged-array batch (`float[][]`) |
| GET | `/api/telemetry/{sensorId}?take=50` | Recent telemetry history |
| POST | `/api/deployment/validate` | Recursively validate a deployment tree |
| GET | `/api/deployment/sample` | A ready-made example deployment tree |
| GET | `/api/dashboard/state` | Pre-attentive status snapshot per sensor |
| GET | `/api/power/compare/{a}/{b}` | Overloaded-operator power comparison |

## Notes on the choices I made

Storage is in-memory, backed by `ConcurrentDictionary` singletons. That's deliberate — the
brief is about a lightweight, resource-constrained gateway, and Part 1 doesn't call for
persistence across restarts. It's also a clean seam: EF Core, SQLite, or a proper
time-series store can slot in later without touching `SmartX.Core`.

There's no authentication in Part 1. The brief doesn't ask for it yet, and the ingestion
endpoints are meant to be reachable by simulated or physical sensors without a login flow.
Uploaded files are capped at 20 MB per request as a basic guard against resource abuse.

Mock telemetry runs continuously in the background rather than being seeded once on startup.
I wanted the pre-attentive dashboard to actually have something to be pre-attentive about
during a live demo, rather than sitting static and green the whole time.

## Commit history

The commit history is organised by milestone — Core, then API, then Client, then Docker,
then docs — and each commit leaves the project in a state that builds and runs. It's six
commits, not the 20+ the brief suggests, because I built this in one sitting rather than
across several. I'd rather say that plainly than pad the history with meaningless commits. As
I extend this into Part 2, I'll commit incrementally as I actually make changes.
