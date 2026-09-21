# Delivery plan

Each PR is independently reviewable and leaves `main` running. Nothing is merged that
breaks the console.

## PR-1 — Ingestion API and gateway scaffold (this PR)

Solution layout, Docker, the telemetry type system, the bounded ingest queue, validation,
per-signal ring buffers, the fleet/device/signal read tiers, the mock seeder with fault
injection, and a React harness that proves the round trip. Deliverables two and three ship
their gated route surface.

**Review focus:** `SignalValue`, `TelemetryValidator`, `IngestQueue`.

## PR-2 — MQTT ingest

MQTTnet client subscribing to `smartx/+/+/+`, feeding the same `IngestQueue` the HTTP
endpoint uses. Last Will and Testament plus a retained status topic per device. Topic
schema and a per-device sequence check for loss detection.

**Depends on:** the queue and envelope shape from PR-1, which do not change.

## PR-3 — Anomaly scoring and liveness

Allocation-free rolling median and MAD per signal over the existing ring buffers. A
`lastSeen` liveness tracker cross-checked against LWT. The alert state machine, including
the `Suspect` state that suppresses flapping and the `CorrelatedOffline` state that
collapses a site-wide grid drop into one event. TimescaleDB writer behind the store
interface.

**Depends on:** ring buffers and `DeviceStatus` from PR-1.

## PR-4 — Push channel and the anomaly canvas

SignalR hub grouped per site, sending deltas rather than raw messages. The React canvas:
expected band, shaded excursions, gaps rendered as breaks rather than flat lines, and the
confirm/dismiss loop that treats a flag as a claim rather than a verdict. The four-tier
drill path becomes navigable, with alerts as deep links.

**Replaces:** the polling harness from PR-1.

## PR-5 — Command stream and history (deliverable 2)

Turns on `SmartX:Features:CommandStream`. Command dispatch with correlation ids,
acknowledgement streaming, and an append-only history log. The routes already exist.

## PR-6 — Topology and mesh routing (deliverable 3)

Turns on `SmartX:Features:MeshTopology`. Adjacency graph of the ESP-MESH fleet, link
quality per edge, and shortest-path routing weighted by link quality and hop count.

## PR-7 — MAUI and WPF shells

Both reference `SmartX.Contracts` directly. MAUI runs a self-hosted local API for offline
operation and reconciles on reconnect; WPF talks to the gateway over the same HTTP and
SignalR contract the browser uses.
