# Smart-X IoT Mesh Ecosystem — Part 1: Ingestion & Validation Gateway

Portfolio of Evidence — Part 1, Task 2 (Implementation, 80 marks).

A .NET 10 Minimal API ingestion gateway (`SmartX.Api`) and a Razor Pages configuration
dashboard (`SmartX.Client`), sharing a domain class library (`SmartX.Core`), built for the
Smart-X hybrid IoT mesh scenario.

## Architecture

```
smartx/
├── SmartX.sln
├── docker-compose.yml
├── src/
│   ├── SmartX.Core/     # Domain library: no ASP.NET dependency, fully unit-testable
│   ├── SmartX.Api/      # ASP.NET Core Minimal API — the ingestion/validation gateway
│   └── SmartX.Client/   # ASP.NET Core Razor Pages — the configuration dashboard UI
└── README.md
```

The Client is a **separate process** from the API and talks to it purely over HTTP, from
JavaScript running in the browser (`wwwroot/js/*.js`) — there is no server-to-server proxying.
This was a deliberate choice: it keeps the "frontend communicates with the API layer to push
and retrieve data" requirement unambiguous, and it means the two pieces can be deployed,
scaled, or replaced independently, matching the brief's *Web-First* architecture option
(ASP.NET Core Minimal API backend + a browser-based frontend).

## Why Razor Pages rather than Blazor/React

The functional requirements were implementable equally well with any of the permitted
frontend choices. Razor Pages was chosen specifically so the **entire solution compiles and
runs from a single `dotnet` toolchain with zero external frontend build steps** (no `npm
install`, no WebAssembly download) — directly serving the "if the code does not compile and
run, no marks are awarded" requirement. Bootstrap is vendored locally under `wwwroot/lib`
(added by the project template), so the UI has no CDN dependency either.

## Where each required C# concept lives

| Requirement | File | Notes |
|---|---|---|
| **Generics** | `SmartX.Core/Telemetry/TelemetryPacket.cs` | `TelemetryPacket<T>` is a `readonly struct` constrained to `where T : struct`, so `TelemetryPacket<float>`, `<int>` and `<bool>` are distinct value types with no boxing. Instantiated for all three sensor payload types in `SmartX.Api/Endpoints/TelemetryEndpoints.cs`. |
| **Operator overloading** | `SmartX.Core/Power/PowerReading.cs` | Overloads `+`, `-`, `==`, `!=`, `<`, `>`, `<=`, `>=` on `PowerReading`. Exercised via `GET /api/power/compare/{a}/{b}` in `SmartX.Api/Services/PowerComparisonService.cs`. |
| **Advanced arrays (jagged) + List\<T\>** | `SmartX.Core/Telemetry/TelemetryBatchBuffer.cs` | Buffers historical batches as `float[][]` (each batch can be a different length), then `FlattenToPackets()` transfers them into a `List<TelemetryPacket<float>>`. Wired up via `POST /api/telemetry/{sensorId}/batch`. |
| **Recursion** | `SmartX.Core/Deployment/DeploymentValidator.cs` | `ValidateRecursive` walks a `DeploymentNode` tree (Facility → Zone → SubZone → Node) tier by tier, recursing into every child. Exposed via `POST /api/deployment/validate`. |

## Where each functional requirement lives

| Requirement | Implementation |
|---|---|
| Startup landing page, 3 pillars (1 active, 2 disabled) | `SmartX.Client/Pages/Index.cshtml` |
| Web API ingestion layer | `SmartX.Api` (all endpoints under `/api/...`) |
| Sensor payload registration (MAC, location, category) | `POST /api/sensors`, UI at `SmartX.Client/Pages/Sensors.cshtml` |
| Media/log attachment via multipart upload | `POST /api/sensors/{id}/media`, `SmartX.Api/Services/MediaStorageService.cs` |
| Dynamic engagement feature (not a progress bar) | `SmartX.Client/Pages/Dashboard.cshtml` + `wwwroot/js/dashboard.js` + `wwwroot/css/site.css` — pre-attentive colour/outline-coded tiles with a "heartbeat" pulse tied to data freshness, implementing the strategy justified in the Part 1 research report |
| Heavy mock telemetry seeding, under load | `SmartX.Api/Services/MockTelemetryBackgroundService.cs` — seeds 5 default sensors and generates a new reading for **every** registered sensor (including ones you add yourself) every 3 seconds, with periodic simulated spikes and disconnects |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (`dotnet --version` should report `10.x`)
- (Optional) Docker + Docker Compose, if you want to run it containerised
- No database, no external NuGet feeds beyond the default nuget.org are required — both
  projects only depend on the ASP.NET Core shared framework

## Running locally (no Docker)

Open **two terminals** — the API and the Client are independent processes.

**Terminal 1 — API (ingestion gateway):**
```bash
cd src/SmartX.Api
dotnet restore
dotnet build
dotnet run
# Listens on http://localhost:5050
```

**Terminal 2 — Client (configuration dashboard):**
```bash
cd src/SmartX.Client
dotnet restore
dotnet build
dotnet run
# Listens on http://localhost:5100
```

Then open **http://localhost:5100** in a browser. Within a few seconds of the API starting,
its background service seeds 5 default sensors, so the Dashboard page should show tiles
immediately.

Or, to build/restore the whole solution in one step from the repository root:
```bash
dotnet restore SmartX.sln
dotnet build SmartX.sln
```
(you still need to `dotnet run` the API and Client separately, in their own terminals, since
they are two independently-hosted processes.)

## Running with Docker Compose

```bash
docker compose up --build
```
This builds and starts both containers:
- API published at **http://localhost:5050**
- Client published at **http://localhost:5100**

Stop with `docker compose down` (add `-v` to also clear the named upload volume).

> The Client's `ApiBaseUrl` is deliberately the **host-published** API URL
> (`http://localhost:5050`), not an internal Docker service name — every API call is made
> client-side by JavaScript running in your browser, which is outside the Docker network and
> can only reach containers via their published ports.

## Trying it out / demo script

1. Open **http://localhost:5100** — the landing page shows the three architectural pillars;
   only *Sensor Data Ingestion & Telemetry* is active.
2. Open **Live Dashboard** — five default sensors are already reporting mock telemetry, colour
   coded (green/amber/red) with a live heartbeat pulse; watch for one to grey out
   ("Disconnected") or flash red ("Critical") over the next minute or two.
3. Open **Sensor Ingestion** — register a new sensor (MAC address, location, category). It
   appears in the table immediately, and starts appearing on the Dashboard within ~3 seconds
   as the background mock service picks it up.
4. Attach a file (a config file, a photo, a log) to any sensor via the **Attach** control.
5. In the **Deployment Tree Validation** panel, click **Load Sample Tree** then **Validate** to
   see the recursive validator pass; edit a node's `tier` value to something out of order and
   validate again to see it fail with a specific error.
6. Inspect `GET http://localhost:5050/api/power/compare/{meterIdA}/{meterIdB}` (swap in two of
   the seeded `PowerConsumption` sensor IDs from `GET /api/sensors`) to see the overloaded
   operator output (`aggregate`, `delta`, `aIsHigher`, `areEqual`).

## API reference (summary)

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/sensors` | Register a sensor profile |
| `GET` | `/api/sensors` | List all sensors |
| `GET` | `/api/sensors/{id}` | Get one sensor |
| `DELETE` | `/api/sensors/{id}` | Remove a sensor |
| `POST` | `/api/sensors/{id}/media` | Multipart file upload (config/photo/log) |
| `GET` | `/api/sensors/{id}/media` | List attachments |
| `GET` | `/api/sensors/{id}/media/{mediaId}/download` | Download an attachment |
| `POST` | `/api/telemetry/{sensorId}/float` | Ingest a `TelemetryPacket<float>` reading |
| `POST` | `/api/telemetry/{sensorId}/int` | Ingest a `TelemetryPacket<int>` reading |
| `POST` | `/api/telemetry/{sensorId}/bool` | Ingest a `TelemetryPacket<bool>` reading |
| `POST` | `/api/telemetry/{sensorId}/batch` | Ingest a jagged-array batch (`float[][]`) |
| `GET` | `/api/telemetry/{sensorId}?take=50` | Recent telemetry history |
| `POST` | `/api/deployment/validate` | Recursively validate a deployment tree |
| `GET` | `/api/deployment/sample` | A ready-made example deployment tree |
| `GET` | `/api/dashboard/state` | Pre-attentive status snapshot per sensor |
| `GET` | `/api/power/compare/{a}/{b}` | Overloaded-operator power comparison |

## Design notes / known simplifications

- **Storage is in-memory** (`ConcurrentDictionary`-backed singletons), by design: the brief
  emphasises a lightweight, resource-constrained gateway, and Part 1 does not require
  persistence across restarts. This is the natural seam to swap in EF Core / SQLite / a
  time-series store in a later part without touching `SmartX.Core`.
- **No authentication** is implemented in Part 1 — the brief does not call for it yet, and the
  ingestion endpoints are meant to be reachable by simulated/physical sensors without a login
  flow. Uploaded files are capped at 20 MB per request as a basic resource-abuse guard.
- Mock telemetry is generated **continuously in the background**, not just once on startup —
  this was chosen so the pre-attentive dashboard actually has something to be pre-attentive
  *about* during a live demo, rather than sitting static.

## Repository / commit history

This repository's commit history is incremental and organised by milestone (Core →
API → Client → Docker → docs), each commit left in a state that builds and runs, so the
history itself documents how the four required C# concepts and the functional requirements
were layered in.
