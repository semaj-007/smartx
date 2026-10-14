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
| **Heavy mock data seeding**                                          | `SmartX.Core/Services/MockTelemetrySeeder.cs` — 14 sensors × 25 packets each on startup, plus 7 jagged history batches per sensor                                              |

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
- The two disabled pillars on the landing page are placeholders for
  later parts of the Smart-X suite and are out of scope here.
- Uploaded attachments are written to `SmartX.Api/App_Data/uploads/`.
