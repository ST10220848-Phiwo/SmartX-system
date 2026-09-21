Smart-X Telemetry Gateway! 

Ingestion API and configuration console for the Smart-X hybrid IoT ecosystem: a
distributed ESP32 fleet publishing multi-typed telemetry to a .NET 10 gateway, with
React, .NET MAUI and WPF shells sharing one API contract.

| Deliverable | State | Lands in |
|---|---|---|
| 1. Sensor data ingestion and telemetry | Live | PR-1 to PR-4 |
| 2. Real-time command stream and history | Gated off, answers `501` | PR-5 |
| 3. Network topology and mesh routing | Gated off, answers `501` | PR-6 |

Deliverables two and three ship their **route surface** now, behind feature flags. The
endpoints appear in OpenAPI marked as planned and return a `501` problem document naming
the PR they arrive in. Client shells can therefore be written against the final contract
before the server side exists, and enabling a deliverable is a config change rather than
a client rewrite.

How to Run?
```bash
cp .env.example .env
docker compose up --build
```

- Console: <http://localhost:5173>
- Gateway: <http://localhost:8080>
- OpenAPI: <http://localhost:8080/openapi/v1.json>

The seeder is on by default in development, so the console has traffic the moment it
starts. There is no physical fleet yet; that is the point of the seeder.

Optional profiles, declared now so the topology does not shift later:

```bash
docker compose --profile mqtt up       # Mosquitto broker, for PR-2
docker compose --profile storage up    # TimescaleDB, for PR-3
docker compose --profile load up       # 2000 simulated devices at 5 Hz
```

Without Docker:

```bash
dotnet run --project src/SmartX.Api          # gateway on :8080
cd web && npm install && npm run dev         # console on :5173, proxying /api
```

Layout

```
src/SmartX.Core        Telemetry primitives: tagged union, ring buffer, registries
src/SmartX.Contracts   Wire DTOs shared by the API and the MAUI/WPF shells
src/SmartX.Ingest      Bounded queue, validation, pipeline, mock seeder
src/SmartX.Api         Minimal API surface, feature gates, health checks
web/                   React console (PR-1: gateway harness)
```

Telemetry types are carried, not converted:

`SignalValue` is a 16-byte tagged union: float, integer and boolean payloads overlapped
at offset 0, discriminated by a byte at offset 8. A reading never boxes, never allocates,
and never loses its type on the way through the system.

The alternatives were all worse. `object` costs a heap allocation and a pointer chase per
reading. A string that gets parsed on read moves format errors from the edge into every
consumer. A `double` for everything silently mangles counters above 2^53 and destroys the
difference between `0`/`1` and `false`/`true`, which the alert rules depend on.

Because the struct is blittable and fixed-size, a ring buffer of 4096 readings is one
contiguous allocation rather than 4096 objects for the GC to walk.

A reading whose kind disagrees with its registered signal descriptor is **rejected**, with
a code the console displays, not converted. There is exactly one exception — JSON cannot
tell `42` from `42.0`, so a whole number arriving on a float signal is reinterpreted once,
at the edge, and refused outright if it is too large to represent exactly.

Load is shed at the edge, not absorbed

Ingest goes through a bounded `Channel`. Bounded on purpose: an unbounded queue turns a
traffic spike into an out-of-memory kill, which is a worse failure than refusing work. A
saturated queue answers `429`, and the container memory limit in `compose.yaml` is set so
that a regression here shows up in testing rather than in production.

Faults are injectable:

`POST /api/v1/seed/fault` arms a fault class against the simulated fleet: `Spike`,
`Drift`, `Dropout`, `FlatLine`, `SiteWideOutage`, `TypeViolation`, `Flood`. The seeder
pushes through the same queue an HTTP or MQTT producer uses, so a load run genuinely
exercises validation, backpressure and the ring buffers.

`SiteWideOutage` is the one that matters locally. South African grid interruptions drop a
whole site at once, and the console has to collapse that into a single event rather than a
wall of alerts — the correlated-disconnect requirement from the research report.

Verifying it works:

`src/SmartX.Api/SmartX.Api.http` runs the interesting cases, including the two that should
fail: a boolean sent to a float signal (`type_mismatch`) and a quoted number (`400`).

```bash
dotnet test
```

The tests assert the things the design claims: that `SignalValue` is still 16 bytes, that
`Reading` is still 32, that `true` and `1` are not equal, that 2^53+1 survives a round
trip, and that a concurrent reader never observes a torn reading.
No MQTT, no anomaly scoring, no push channel, no durable storage. Devices push over HTTP
and the console polls on a one-second timer. Those are PR-2 through PR-4, and the seams
they attach to — the queue, the ring buffers, the `Suspect` device status, the `/hubs/`
route in nginx — are in place.

The console in `web/` is a gateway harness, not the operator console. The annotated
anomaly canvas the research report argues for replaces it in PR-4.

