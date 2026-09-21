# API contract, v1

Base path `/api/v1`. JSON only. All timestamps are Unix milliseconds, UTC.

## Telemetry values on the wire

A value is a **bare JSON scalar**. Its type comes from the JSON token, not from a string
that has to be parsed:

| Wire | Kind | Note |
|---|---|---|
| `true` / `false` | `Boolean` | |
| `231` | `Integer` | Fits `long` |
| `42.7` | `Float` | |
| `"42"` | rejected | A quoted number is a firmware bug, and parsing it would hide that |

JSON cannot distinguish `42` from `42.0`. Float signals that can report whole numbers
should send an explicit `"k": "Float"`. Without it, the gateway reconciles the reading
against the registered signal descriptor and reinterprets integer-on-float once, at the
edge. Nothing downstream repeats that guess.

## Ingest

`POST /api/v1/telemetry`

```json
{
  "readings": [
    { "site": "jhb-north", "dev": "esp32-0417", "sig": "soil_moisture",
      "v": 42.7, "ts": 1757836800000, "seq": 91024, "k": "Float" }
  ]
}
```

Field names are short because an ESP32 pays for every byte it publishes, and at fleet
scale so does the gateway.

| Status | Meaning |
|---|---|
| `202` | Queued. **Not** a durability ack — the endpoint never answers `200` so this cannot be misread. |
| `413` | Batch above `MaxBatchSize`. Split it, or publish over MQTT. |
| `429` | Ingest queue saturated. Back off; honour `Retry-After`. |
| `400` | Malformed body, including a quoted number where a scalar was expected. |

A batch that is partly accepted returns `202` with per-index rejections. A batch where
nothing was accepted returns `429`, so a device does not have to read the body to learn it
should slow down.

### Rejection codes

| Code | Cause |
|---|---|
| `missing_identity` | `site`, `dev` or `sig` absent |
| `undefined_value` | Value was null. Gaps are stored as gaps, never as zero |
| `type_mismatch` | Kind disagrees with the signal descriptor. No conversion attempted |
| `precision_loss` | Integer too large to represent exactly on a float signal |
| `out_of_range` | Non-finite float, or outside the signal's declared plausible range |
| `timestamp_implausible` | Device clock beyond the configured skew window |
| `queue_saturated` | Backpressure |
| `batch_too_large` | Above `MaxBatchSize` |

## Reads

| Route | Tier | Returns |
|---|---|---|
| `GET /fleet/summary` | Fleet | Device and signal counts, per-site status roll-up |
| `GET /devices?site=` | Site | Devices, optionally filtered |
| `GET /devices/{deviceId}` | Device | One device and the signals it publishes |
| `GET /telemetry/{site}/{dev}/{sig}?from=&to=&max=` | Signal | A window of readings |
| `GET /telemetry/signals` | — | Every signal seen, with its fixed kind |
| `GET /ingest/stats` | — | Throughput, rejections, queue pressure |

These four tiers are the drill path the research report argues for: fleet, site, device,
signal. Each answers one question and links to the next.

## Planned routes

`/api/v1/commands/*` and `/api/v1/topology/*` exist and answer `501` with a problem
document carrying `feature`, `status: "planned"` and `plannedIn`. They appear in OpenAPI
so client code can be generated against them today.

## Health

| Route | Question |
|---|---|
| `/health/live` | Is the process up? |
| `/health/ready` | Can this instance still absorb ingest traffic? |

Readiness degrades above 90% queue depth and fails above 98%, so a saturated gateway is
taken out of the load balancer's rotation while it is still serving reads correctly.
