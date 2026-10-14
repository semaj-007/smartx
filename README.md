# Smart X Console

Smart X is a hybrid IoT ecosystem simulation for distributed environments (hydroponic
farms, automated real-estate utility trackers, smart-grid installations)
built on **.NET 10**: an ASP.NET Core Web API ingestion layer, a shared
class library, and a Windows Forms client-facing dashboard.

## IoT Research & Design Rationale

Real ESP32-class field nodes publish small, frequent, _heterogeneous_
payloads, a capacitive soil-moisture sensor reports a float percentage,
a smart meter reports an integer wattage, a solenoid valve reports a
bool open/closed state. A naive gateway stringifies every reading into
one generic envelope and re-parses it downstream, which is exactly the
"type-coercion overhead" the brief warns against. Smart-X avoids that by
giving every reading its own closed generic `TelemetryPacket<T>` and
routing it through a shared `ITelemetryPacket` contract, so the value
keeps its native CLR type end-to-end.

Other design choices are drawn from how field gateways actually
behave:

- **Jagged history batches** — a gateway with intermittent connectivity
  flushes an uneven number of buffered readings per upload session, so
  `TelemetryHistoryStore` models that with a jagged `double[][]` rather
  than a fixed rectangular array, before transferring each batch into an
  optimised `List<double>` for querying.
- **Nested deployment hierarchies** — smart-grid and building-management
  systems commonly organise devices as Facility → Zone → Sub-Zone →
  Device (mirroring IEC 61850-style substation modelling). Validating
  that every node in that hierarchy is correctly configured is naturally
  recursive, since a facility's validity depends on its zones', which
  depend on their sub-zones', and so on.

## Folder Structure

```
SmartX.sln
├── SmartX.Core/           Class library — shared domain models & logic
│   ├── Models/
│   │   ├── SensorCategory.cs        enum: Soil Moisture, Power Meter, ValveState, Temperature, Humidity, LightLevel
│   │   ├── ITelemetryPacket.cs      non-generic contract for TelemetryPacket<T>
│   │   ├── TelemetryPacket.cs       TelemetryPacket<T> where T : struct
│   │   ├── SensorReading.cs         OPERATOR OVERLOADING
│   │   ├── SensorRegistration.cs    a registered sensor's deployment record
│   │   └── DeviceNode.cs            a node in the nested deployment tree
│   └── Services/
│       ├── DeploymentTreeValidator.cs   RECURSION — Validate / FindNode / CountDevices
│       ├── TelemetryHistoryStore.cs     ADVANCED ARRAYS — jagged double[][] → List<List<double>>
│       ├── MockTelemetrySeeder.cs       heavy mock data seeding
│       └── TelemetryStore.cs            thread-safe in-memory store
│
├── SmartX.Api/             ASP.NET Core Web API — the ingestion layer
│   ├── Program.cs                   startup, seeding, fixed port 5080
│   ├── Dtos/TelemetryIngestDto.cs
│   └── Controllers/
│       ├── TelemetryController.cs   ingest / recent / history / aggregate
│       ├── SensorsController.cs     register / list / attachment upload
│       └── DeploymentController.cs  tree / validate
│
└── SmartX.Desktop/         Windows Forms — the client-facing dashboard
    ├── Program.cs
    ├── ApiClient.cs                 HttpClient wrapper (System.Text.Json)
    ├── Dtos/                        SensorRow, TelemetryRow
    ├── Controls/
    │   └── PulseGaugeControl.cs     DYNAMIC ENGAGEMENT FEATURE — live radar pulse
    └── Forms/
        ├── MainMenuForm.cs          landing page, 3 pillars (2 disabled)
        └── TelemetryForm.cs         registration + grids + pulse + tools
```

## Technical requirement mapping

| Requirement                                                          | Where                                                                                                                                                                          |
| -------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Generics** — reusable wrapper avoiding boxing/unboxing             | `SmartX.Core/Models/TelemetryPacket.cs` — `TelemetryPacket<T> where T : struct`, used as `TelemetryPacket<float>`, `TelemetryPacket<int>`, `TelemetryPacket<bool>`             |
| **Operator overloading** — aggregate/delta on sensor data            | `SmartX.Core/Models/SensorReading.cs` — `+`, `-`, `>`, `<`, `==`, `!=`; exercised by `TelemetryController.AggregatePower()`                                                    |
| **Advanced arrays/lists** — jagged raw batches → optimised `List<T>` | `SmartX.Core/Services/TelemetryHistoryStore.cs`                                                                                                                                |
| **Recursion** — nested deployment-tree validation                    | `SmartX.Core/Services/DeploymentTreeValidator.cs` — validates `Facility A → Zone 1 → Sub-Zone B → device` style trees                                                          |
| **Dynamic engagement feature** (not a progress bar)                  | `SmartX.Desktop/Controls/PulseGaugeControl.cs` — an animated radar sweep with a live packets/sec readout, driven by real ingestion events                                      |
| **Sensor payload management** (MAC, location, category)              | `SmartX.Api/Controllers/SensorsController.cs` + the "Register Sensor" panel in `TelemetryForm.cs`                                                                              |
| **Media/log attachment** via `OpenFileDialog` + multipart API upload | `TelemetryForm.PickAttachment()` (OpenFileDialog) → `ApiClient.UploadAttachmentAsync()` → `SensorsController.UploadAttachment()` (`IFormFile`, saved under `App_Data/uploads`) |
| **Simulated device data**                                          | `SmartX.Core/Services/MockTelemetrySeeder.cs` — 14 registered demo devices; live telemetry enters through HTTP simulation or physical devices                                              |

---

## How to build & run

**Requirements:** Windows, Visual Studio 2022 (17.8+) or later with the
".NET desktop development" workload, and the **.NET 10 SDK**. The first
build needs internet access once, for NuGet to restore the standard
ASP.NET Core / WinForms SDK packages.

### Option A — Visual Studio

1. Open `SmartX.sln`.
2. Right-click the solution → **Set Startup Projects…** → _Multiple
   startup projects_ → set **SmartX.Api** and **SmartX.Desktop** both to
   **Start**, with the API listed first.
3. Press **F5**. The API window opens first (listening on
   `http://localhost:5080`) and seeds its mock data; the dashboard opens
   moments later and connects automatically.

### Option B — command line

```
cd SmartX.Api
dotnet run
```

In a second terminal:

```
cd SmartX.Desktop
dotnet run
```

The dashboard polls the API every 3 seconds, so registrations, uploads,
and mock readings sent from either side show up automatically — no
manual refresh needed.

## API quick reference

| Method | Route                                | Purpose                                                |
| ------ | ------------------------------------ | ------------------------------------------------------ |
| GET    | `/api/sensors`                       | list all sensors                                       |
| POST   | `/api/sensors/register`              | register a sensor `{ macAddress, location, category }` |
| POST   | `/api/sensors/{deviceId}/attachment` | multipart file upload                                  |
| GET    | `/api/telemetry?count=50`            | recent packets                                         |
| POST   | `/api/telemetry/ingest`              | `{ deviceId, category, valueType, value, unit }`       |
| GET    | `/api/telemetry/history/{deviceId}`  | jagged-array batch history                             |
| GET    | `/api/telemetry/aggregate`           | operator-overload demo                                 |
| GET    | `/api/deployment/tree`               | full deployment tree                                   |
| GET    | `/api/deployment/validate`           | recursive validation                                   |

## Notes & limitations

- Storage is entirely **in-memory** for this simulation phase (restarting
  `SmartX.Api` resets all data and re-seeds).
- Sensor ingestion and the real-time command stream are active modules.
  Network topology and mesh routing remain scheduled for a future part.
- Uploaded attachments are written to `SmartX.Api/App_Data/uploads/`.


## Real-time command stream (Part 2)

Open **Real-Time Command Stream & History** from the desktop main menu. Both active
modules stay alive when you return to the menu, retaining inputs, filters, selection,
and API state. The mesh module remains marked as future work. Network operations are
asynchronous, polling never overlaps, and hidden modules pause refreshes.

Choose **Start Simulation** to run the opt-in virtual fleet, or send physical-device
requests to `POST /api/telemetry/ingest`. The API starts with 14 deterministic device
registrations (including `MOISTURE-B`, `PUMP-A`, and `VALVE-A`), and **no injected
telemetry**. The simulator fetches device states and posts every generated reading
through HTTP, just like an ESP32. It generates moisture decline/recovery and power
spikes to demonstrate insights. Stop it to observe disconnect alerts after 15 seconds.
Telemetry from a device clears its disconnect alert. There is no database requirement.

The dashboard refreshes every 750 ms. Search by device ID, MAC, location or category,
combine a category filter with **Alerts only**, and select a device to view its
chronological numeric timeline. Queue depths, processing totals, active alerts,
priority markers and command history are visible. Actuator state is explicitly
simulated: **Turn on/off** works on PowerMeter pumps/relays and ValveState devices;
**All valves off** captures each valve's previous state for a single stack undo.
Reported readings and desired override state are separate: after a manual command or
undo, incoming telemetry updates the reading without overwriting the desired state.
Commands and undo are shared between operators in this in-memory prototype; no
physical-device transport or acknowledgement is claimed. Restarting resets all state.

### Processing structures

| Structure | Runtime responsibility |
| --- | --- |
| `Queue<ITelemetryPacket>` | Bounded normal FIFO; a background worker drains batches every 20 ms. Full queues return HTTP 429 with Retry-After. |
| `PriorityQueue` | Critical moisture below 20% or power at/above 2000 W bypasses waiting normal packets and is processed on ingestion. |
| `Stack` | Stores manual command snapshots; undo restores the latest command's affected devices, including bulk actions. |
| `Dictionary` | Unique device ID/MAC lookup and live state; duplicate registrations are rejected. |
| `SortedDictionary` | Timestamp-keyed timeline buckets preserve equal timestamps and expose bounded chronological histories. |
| `HashSet` | Unique active device/error pairs, including disconnects; repeated packets do not duplicate an alert. |

The structures are synchronized for concurrent API access. Dashboard results are
snapshots, rather than mutable collection references. Retention limits bound queues,
telemetry, operator histories and learned pattern storage.

### Suggested actions and automated insights

The action engine records **explicit** search intent and manual commands; background
polls never count as operator searches. It learns associations between a sensor's recent alert, searches for related actuators,
and subsequent actuator commands in a two-minute window. After
at least three distinct alert episodes, a matching current condition can suggest that command,
with supporting evidence and confidence displayed in the dashboard. Recent numeric
history also supports downward moisture/upward power trend warnings before critical
thresholds. These are heuristic recommendations, not trained-model probabilities or
guaranteed predictions. Suggested actions always require an operator click; they do
not automatically change actuator state. Use the dashboard to search `MOISTURE-B`,
then `PUMP-A` and issue pump commands during low moisture to demonstrate learning.

### Additional API routes

| Method | Route | Purpose |
| --- | --- | --- |
| GET | `/api/realtime/dashboard?query=&category=&alertsOnly=false` | Live states, telemetry feed, commands, metrics and insights |
| GET | `/api/realtime/devices/{deviceId}/timeline?count=200` | Chronological real telemetry; count 1–1000 |
| POST | `/api/realtime/searches` | Record explicit `{ query }` intent |
| POST | `/api/realtime/commands` | `{ deviceId, action }`, action TurnOn/TurnOff/AllValvesOff; bulk deviceId `*` |
| POST | `/api/realtime/commands/undo` | Restore latest command; HTTP 409 when empty |
| GET | `/api/realtime/simulation` | Running state, successful request count and failure diagnostics |
| POST | `/api/realtime/simulation/start` | Start the opt-in HTTP device fleet |
| POST | `/api/realtime/simulation/stop` | Stop and cancel the current simulation cycle |

Ingestion returns **202 Accepted**. Normal packets appear after worker processing;
critical packets are already processed when the request returns. The category must
match the registration, with bool for ValveState, int for PowerMeter/LightLevel, and
finite invariant-culture float for other categories. Unknown devices return 404;
invalid payloads return 400. Example:

```sh
curl -X POST http://localhost:5080/api/telemetry/ingest \
  -H 'Content-Type: application/json' \
  -d '{"deviceId":"MOISTURE-B","category":"SoilMoisture","valueType":"float","value":"18.5","unit":"%"}'
```

### Build and validation

On Windows, use the .NET 10 SDK and run the API and desktop as described above.
`SMARTX_API_URL` can override the desktop API base URL. On Linux, the API can run and
the desktop can cross-compile, but Windows Forms requires Windows to execute:

```sh
dotnet build SmartX.sln --artifacts-path /tmp/smartx-build -p:EnableWindowsTargeting=true
dotnet run --project tests/SmartX.BehaviorTests --artifacts-path /tmp/smartx-tests
python3 tests/smoke_api.py
```

Run HTTP checks against a dedicated running API instance; they register test devices,
issue commands, train suggestions and start/stop the simulator. Set `SMARTX_API_URL`
for a different API URL. The dependency-free Core runner tests processing order,
alerts, retention, undo and predictions without requiring Windows. Build artifacts
are redirected because this repository contains tracked historical bin/obj files.
