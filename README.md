# Smart-X IoT Mesh Ecosystem — Part 1: Ingestion & Validation Gateway

My Part 1 implementation for the Smart-X Portfolio of Evidence: a .NET 10 Minimal API
ingestion gateway (`SmartX.Api`) and a Razor Pages configuration dashboard
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
JavaScript running in the browser — no server-to-server proxying. That keeps the
"frontend communicates with the API to push and retrieve data" requirement unambiguous, and
lets the two halves be deployed independently.

I picked Razor Pages over Blazor or React so the whole solution builds and runs from a
single `dotnet` toolchain, no separate frontend build step. Since the brief awards nothing at
all if the code doesn't compile and run, I wanted as few moving parts as possible. Bootstrap
is vendored locally, so there's no CDN dependency either.

## Where the required C# concepts live

| Concept | File | What's there |
|---|---|---|
| Generics | `SmartX.Core/Telemetry/TelemetryPacket.cs` | `TelemetryPacket<T>`, a `readonly struct` constrained to `where T : struct`, so `<float>`, `<int>` and `<bool>` are distinct value types with no boxing. Used in `SmartX.Api/Endpoints/TelemetryEndpoints.cs`. |
| Operator overloading | `SmartX.Core/Power/PowerReading.cs` | `+`, `-`, `==`, `!=`, `<`, `>`, `<=`, `>=` on `PowerReading`. Exercised through `GET /api/power/compare/{a}/{b}`. |
| Jagged arrays + List\<T\> | `SmartX.Core/Telemetry/TelemetryBatchBuffer.cs` | Historical batches arrive as `float[][]`, each batch a different length, then `FlattenToPackets()` transfers them into a `List<TelemetryPacket<float>>`. Wired up through `POST /api/telemetry/{sensorId}/batch`. |
| Recursion | `SmartX.Core/Deployment/DeploymentValidator.cs` | Walks a `DeploymentNode` tree (Facility → Zone → SubZone → Node), recursing into every child. Exposed through `POST /api/deployment/validate`. |

## Where the functional requirements live

| Requirement | Implementation |
|---|---|
| Landing page, 3 pillars (1 active, 2 disabled) | `SmartX.Client/Pages/Index.cshtml` |
| Web API ingestion layer | `SmartX.Api`, everything under `/api/...` |
| Sensor registration (MAC, location, category) | `POST /api/sensors`, UI at `Pages/Sensors.cshtml` |
| Media/log attachment via multipart upload | `POST /api/sensors/{id}/media` |
| Dynamic engagement feature (not a progress bar) | `Pages/Dashboard.cshtml` + `dashboard.js` + `site.css` — pre-attentive colour-coded tiles with a heartbeat pulse tied to data freshness, carrying through the strategy from the Part 1 research report |
| Heavy mock telemetry seeding under load | `MockTelemetryBackgroundService.cs` — seeds 5 sensors and generates a reading for every registered sensor every 3 seconds, with occasional spikes and disconnects |

## Getting it running

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` → `10.x`).
Docker is optional. No database, no NuGet feed beyond nuget.org.

Two terminals, since the API and client are independent processes:

```bash
# Terminal 1 — ingestion gateway
cd src/SmartX.Api
dotnet run
# http://localhost:5050

# Terminal 2 — configuration dashboard
cd src/SmartX.Client
dotnet run
# http://localhost:5100
```

Open http://localhost:5100 — the API seeds 5 default sensors within a few seconds, so the
dashboard shows tiles straight away.

## Running with Docker Compose

```bash
docker compose up --build
```
API at http://localhost:5050, client at http://localhost:5100. The client's `ApiBaseUrl`
points at the host-published API address, not an internal Docker service name, because every
API call happens client-side from the browser, which sits outside the Docker network.

## Trying it out

1. Landing page — three pillars, only *Sensor Data Ingestion & Telemetry* is active.
2. Live Dashboard — five sensors reporting mock telemetry, colour-coded with a heartbeat
   pulse. Give it a minute and one should grey out (disconnected) or flash red (critical).
3. Sensor Ingestion — register a sensor; it appears immediately and starts reporting to the
   dashboard within ~3 seconds.
4. Attach a file to any sensor through the Attach control.
5. Deployment Tree Validation — Load Sample Tree, then Validate. Break a node's `tier` order
   and validate again to see it fail with a specific error.
6. `GET /api/power/compare/{meterIdA}/{meterIdB}` with two `PowerConsumption` sensor IDs
   shows the overloaded-operator output: `aggregate`, `delta`, `aIsHigher`, `areEqual`.

## API reference

| Method | Route | Purpose |
|---|---|---|
| POST | `/api/sensors` | Register a sensor profile |
| GET | `/api/sensors` | List all sensors |
| GET | `/api/sensors/{id}` | Get one sensor |
| DELETE | `/api/sensors/{id}` | Remove a sensor |
| POST | `/api/sensors/{id}/media` | Multipart file upload |
| GET | `/api/sensors/{id}/media` | List attachments |
| GET | `/api/sensors/{id}/media/{mediaId}/download` | Download an attachment |
| POST | `/api/telemetry/{sensorId}/float`, `/int`, `/bool` | Ingest a typed `TelemetryPacket<T>` reading |
| POST | `/api/telemetry/{sensorId}/batch` | Ingest a jagged-array batch (`float[][]`) |
| GET | `/api/telemetry/{sensorId}?take=50` | Recent telemetry history |
| POST | `/api/deployment/validate` | Recursively validate a deployment tree |
| GET | `/api/deployment/sample` | Example deployment tree |
| GET | `/api/dashboard/state` | Pre-attentive status snapshot per sensor |
| GET | `/api/power/compare/{a}/{b}` | Overloaded-operator power comparison |

## Notes on the choices I made

Storage is in-memory (`ConcurrentDictionary` singletons) — the brief wants a lightweight,
resource-constrained gateway, and Part 1 doesn't need persistence across restarts. It's a
clean seam to add EF Core or SQLite later without touching `SmartX.Core`. There's no
authentication yet either, since the ingestion endpoints need to be reachable by simulated or
physical sensors without a login flow — uploads are just capped at 20 MB as a basic guard.
Mock telemetry runs continuously in the background rather than seeding once, so the
dashboard has something to actually be pre-attentive about during a live demo.

## Commit history

Organised by milestone — Core, API, Client, Docker, docs — each one left in a state that
builds and runs. It's six commits, not the 20+ the brief suggests, because I built this in
one sitting. I'd rather say that plainly than pad the history with meaningless commits. I'll
commit incrementally as I extend this into Part 2.
