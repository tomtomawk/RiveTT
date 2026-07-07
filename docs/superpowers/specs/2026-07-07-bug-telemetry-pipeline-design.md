# Bug Telemetry & Assisted-Fix Pipeline - Design

**Date:** 2026-07-07
**Status:** SUPERSEDED — implementation reference is `2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md` (hardened after independent review + code verification). This file remains as historical context for the decisions taken.
**Affects:** RevitCortex.Core (new Telemetry namespace), RevitCortex.Plugin (CortexRouter hook, SendSupportReport, Settings UI), RevitCortex.Server (bridge failure reporting), new `ingest-worker/` (Cloudflare Worker), new `triage/` (rctriage skill + scripts), known-issues.json schema, release flow
**Targets:** Revit 2023-2027 (`Debug R24` + `Debug R25` minimum before commit; Core code must be net48-compatible)

## Problem

RevitCortex is heading toward paid distribution. Today the bug loop is entirely manual:

- Errors die in the customer's local `audit.jsonl`; the developer never learns a bug exists unless the customer notices, clicks the ribbon "support report" button, and sends the Outlook draft.
- Performance bottlenecks (slow tools, oversized responses) are recorded locally (audit v2 `duration_ms` / `response_bytes`) but never aggregated across installations.
- Known-issue matching (`known-issues.json` + rclog) runs only when a ZIP report happens to arrive.
- Fixing a bug requires manually reconstructing context from whatever the customer sent.

For paying customers this is too slow and too lossy. The goal: automate (1) collection of detailed bug/bottleneck data, (2) delivery, and (3) as much of the resolution loop as is safe — while respecting GDPR (customers are Italian/EU AEC firms whose models are confidential).

## Decisions Taken (with user, 2026-07-07)

1. **Transport:** own cloud ingest endpoint (not Sentry, not GitHub-relay, not SMTP).
2. **Consent model:** two-tier — anonymous error/bottleneck telemetry sent automatically once the user confirms the first-run consent dialog (checkbox pre-selected, revocable anytime in Settings); full diagnostic ZIP only on explicit per-report user consent.
3. **Resolution automation:** automatic triage + assisted fix. Claude Code proposes tested patches as PRs; the human reviews and merges. Never auto-merge, never auto-release.
4. **Hosting:** Cloudflare Workers + D1 (events) + R2 (ZIPs), on `ingest.revitcortex.dev`. Chosen approach: "thin edge, local brain" — Worker stays minimal, triage intelligence runs on the developer's machine as a scheduled Claude Code session.

## Architecture Overview

```
Customer Revit                     Cloudflare                    Developer PC
┌──────────────────┐   events    ┌─────────────────┐  admin API ┌──────────────────┐
│ Plugin + Server  │ ──────────► │ ingest Worker   │ ◄───────── │ rctriage         │
│ ErrorReporter    │   ZIP (opt) │ D1 + R2         │            │ (scheduled       │
│ + SendSupport    │ ──────────► │ known-issues    │            │  Claude Code)    │
│   Report upload  │ ◄────────── │ match response  │            └───────┬──────────┘
└──────────────────┘  "fixed in                                         │ issue + PR
        ▲              vX" toast                                        ▼
        │                                                      ┌──────────────────┐
        │              release updates known-issues.json       │ GitHub (private) │
        └───────────────────────────────────────────────────── │ review → merge → │
                                                               │ release vX+1     │
                                                               └──────────────────┘
```

Three subsystems with clear boundaries:

- **A. Client capture** (RevitCortex.Core + Plugin + Server): fingerprint failures and bottlenecks, queue, batch-send. Depends on nothing new; hooks into the single point where `CortexRouter` already calls `AuditLogger.LogWithPerf`.
- **B. Ingest Worker** (`ingest-worker/`, TypeScript): receive events and ZIPs, dedup by fingerprint, answer with known-issue matches. Depends on D1/R2 and a synced copy of `known-issues.json`.
- **C. Triage pipeline** (`triage/` + scheduled session): pull new fingerprints, open GitHub issues, attempt reproduction + patch, open PRs. Depends on Worker admin API and `gh` CLI.

## A. Client Capture

### ErrorReporter (RevitCortex.Core/Telemetry/)

New components, all net48-compatible (no records, no init-only, no GetValueOrDefault):

- `TelemetryEvent` — one failure or bottleneck occurrence:
  - `eventId` (GUID, client-generated → server idempotency)
  - `fingerprint` (16 hex chars)
  - `kind` (`error` | `bottleneck`)
  - `tool`, `errorCode`, `sanitizedMessage` (≤200 chars)
  - `pluginVersion`, `revitVersion`, `os`, `locale`
  - `durationMs`, `responseBytes`
  - `installationId` (random GUID created on first run, stored in settings.json)
  - `ts` (ISO 8601 UTC), `schemaVersion` (1)
- `ErrorFingerprinter` — SHA256 of `tool + "|" + errorCode + "|" + NormalizedMessage`, truncated to 16 hex chars. Normalization: lowercase; strip digits, file paths, GUIDs, and Revit element IDs; collapse whitespace. "Element 12345 not found" and "Element 99 not found" produce the same fingerprint.
- `MessageSanitizer` — same normalization pipeline applied to the human-readable message before it leaves the machine (paths, GUIDs, numbers replaced with placeholders). Reused by fingerprinter and event payload.
- `TelemetryQueue` — append events to `~/.revitcortex/telemetry-queue.jsonl`. Cap 5 MB, drop-oldest on overflow. Thread-safe (same lock pattern as AuditLogger).
- `TelemetrySender` — background flush: every 5 minutes, or when 20 events are pending, or at shutdown. HTTP POST with 5 s timeout. On failure events stay queued (offline-safe). Parses the response and raises a `KnownIssueMatched` callback for the plugin UI layer.
- `TelemetryConfig` — reads consent + thresholds from settings.json: `EnableTelemetry` (bool), `BottleneckDurationMs` (default 10000), `BottleneckResponseBytes` (default 512000), `ZipPromptFailureThreshold` (default 3), `TelemetryEndpoint` (default `https://ingest.revitcortex.dev`).

### Hook points

- **CortexRouter** (plugin): immediately after the existing `AuditLogger.LogWithPerf` call — `ErrorReporter.Record(...)` for every failure, and for successes exceeding bottleneck thresholds. Zero per-tool instrumentation; every registered tool is covered by the single router hook.
- **RevitCortex.Server**: bridge-level failures (connection loss, malformed frames, timeouts) recorded through the same Core reporter, with `tool = "_bridge"`.
- **Plugin startup errors**: `RevitCortexApp.OnStartup` catch blocks record `tool = "_startup"` events.

Consent off ⇒ `ErrorReporter.Record` is a complete no-op (events are not even queued).

### Repeated-failure ZIP prompt

Per session, when the same fingerprint fails for the Nth time (default 3), show a TaskDialog once: "Questo errore si è ripetuto N volte. Vuoi inviare un report diagnostico completo per aiutarci a risolverlo?" Accept → run the existing `SendSupportReport` ZIP build, upload to the Worker (see below), tagging the report with the triggering fingerprint. Decline → do not ask again for that fingerprint this session.

### SendSupportReport changes

- Primary path becomes HTTPS upload to `POST /v1/reports` (multipart, ≤25 MB). Success → TaskDialog "Report inviato, grazie".
- Outlook COM draft becomes the fallback when upload fails (offline, proxy, endpoint down). Explorer folder-open remains the last resort.
- ZIP content unchanged (audit.jsonl, usage-mcp.db, settings.json, latest journal ≤10 MB, context.txt). `context.txt` gains `installation_id` and `fingerprint` (when prompted by repeated failure) as machine-readable keys.

### Customer-visible surface (everything they ever see)

1. First-run consent dialog: what is sent (anonymous error data), what is never sent (model data), toggle later in Settings. `EnableTelemetry` setting + Settings UI checkbox.
2. Non-invasive known-issue toast/badge: "Errore noto, risolto nella vX — aggiorna dalle impostazioni" (driven by the `KnownIssueMatched` callback; localized via existing `Localization`).
3. Occasional repeated-failure ZIP prompt (above).

## B. Ingest Worker (`ingest-worker/`)

TypeScript Cloudflare Worker, deployed with wrangler to `ingest.revitcortex.dev`. EU location hints on D1/R2.

### Public endpoints (called by clients)

- `POST /v1/events` — body: `{ events: TelemetryEvent[] }` (≤100 events, ≤256 KB). Strict schema validation (garbage → 400). Idempotent upsert by `eventId`. Updates `fingerprints` aggregate (first_seen, last_seen, occurrences, distinct installations, versions seen). Response: `{ accepted: n, knownIssues: [{ fingerprint, issueId, status, fixVersion }] }` — computed by matching submitted fingerprints (and tool+errorCode fallback) against the synced known-issues.
- `POST /v1/reports` — multipart ZIP ≤25 MB + metadata (installationId, optional fingerprint). Stores to R2 `reports/<yyyy-mm>/<id>.zip`; index row in D1.
- Shared anti-noise key: clients send `X-RC-Key` (embedded constant). Understood to be extractable — it filters drive-by spam, it is not security. Real protection = rate limiting (per installationId and per IP) + size caps.

### Admin endpoints (bearer token, secret in Worker env)

- `GET /v1/admin/fingerprints?since=<ts>` — new/updated fingerprints with stats and associated report IDs.
- `GET /v1/admin/reports/<id>` — short-lived signed R2 download URL.
- `GET /v1/admin/stats?window=7d` — perf aggregates (p50/p95 duration, response bytes, occurrence counts) per tool × pluginVersion, for the bottleneck digest.

### D1 schema (4 tables)

- `events` (eventId PK, fingerprint, kind, tool, errorCode, message, versions, installationId, durationMs, responseBytes, ts) — retention 180 days (scheduled purge via cron trigger).
- `fingerprints` (fingerprint PK, tool, errorCode, firstSeen, lastSeen, occurrences, installCount, lastVersions, sampleMessage).
- `reports` (id PK, r2Key, installationId, fingerprint NULL, size, ts) — R2 objects auto-deleted after 90 days (lifecycle rule); purge row on cron.
- `installations` (installationId PK, firstSeen, lastSeen, lastPluginVersion, lastRevitVersion) — future licensing hook.

### known-issues sync

Source of truth stays `known-issues.json` in the main repo — but the main repo is **private** (its raw URLs 404 publicly), so the release flow publishes a copy to the public `revitcortex-releases` repo alongside `latest.json`, exactly like the UpdateChecker's existing feed. The Worker fetches that public copy with a 15-minute cache; a copy is also bundled at deploy time as fallback. Consequence: `title`/`notes` fields must be written public-safe (changelog tone, no internals). Schema extension (backward-compatible): each issue gains optional `"fingerprints": ["a3f9c2e1b0d47f68", ...]` for exact matching; `tool` + `error_code` + `reporter_version_max` remain as fallback matching.

## C. Triage Pipeline (`triage/` + scheduled Claude Code session)

A new `rctriage` skill + supporting PowerShell/Python scripts, run by a scheduled Claude Code session every morning (and on demand). Admin token read from `~/.revitcortex/triage.json` (never in repo).

Per run:

1. **Pull** `GET /v1/admin/fingerprints?since=<last run>` (checkpoint stored in `~/.revitcortex/triage.json`).
2. **Dedup vs GitHub**: search issues labeled `field-bug` for the fingerprint marker (`<!-- rc-fingerprint: ... -->` in body). Existing issue → comment with updated stats only.
3. **Open issues**: one per new fingerprint — title `[field] <tool>: <sanitized message>`, body with tool, error code, versions, occurrence timeline, perf stats, signed ZIP link (expires — noted in body), fingerprint marker. Labels: `field-bug` + `auto-triage`.
4. **Assisted analysis** (cap: 3 per run, cost control): for each new issue, an analysis session downloads the ZIP if present, reads audit/context, locates the tool code, and attempts an xUnit reproduction:
   - Reproducible → failing test + patch on branch `fix/RC-<issue#>`, build `Debug R25` AND `Debug R24`, run tests, open PR referencing the issue with a confidence note.
   - Not reproducible → label `needs-repro` + comment describing what was attempted and what is missing. No speculative PRs.
5. **Close the loop**: on release, the release flow appends the entry to `known-issues.json` (id, fingerprints, fix_version, reporter_version_max). Worker picks it up within its cache window; clients get the "fixed in vX" toast on their next telemetry roundtrip; UpdateChecker handles the update itself.
6. **Perf digest** (Mondays only): aggregate `/v1/admin/stats`, open/update a single `perf-digest` issue with p95 per tool/version and top bottleneck offenders.

Guardrails: never merges, never pushes to main, never releases. Worker unreachable → log and exit (no aggressive retries). Every run appends a summary line to `~/.revitcortex/triage-runs.jsonl` (audit-log principle: the file is the source of truth).

## Error Handling of the Pipeline Itself

- **Golden rule (client):** telemetry must never crash or slow Revit. Every ErrorReporter entry point is fully wrapped; failures degrade to no-op. No UI-thread work; 5 s HTTP timeouts; disk queue capped.
- **Worker:** strict validation (4xx), idempotent writes (eventId), 5xx → client retries on next flush. `/v1/` prefix + `schemaVersion` field allow old clients to keep working for years.
- **Triage:** dry-run mode (`--dry-run`) prints intended actions without creating issues/PRs; per-run caps; checkpoint file prevents reprocessing.

## Privacy / GDPR

- Automatic events never contain: document titles, file paths, tool inputs, parameter names, usernames. Sanitizer strips paths/GUIDs/numbers before anything leaves the machine.
- installationId is a random GUID (pseudonymous, not tied to identity).
- ZIPs contain project data → explicit per-report consent, 90-day retention with auto-delete, admin-token-only access, EU location hint.
- First-run consent dialog + `EnableTelemetry` toggle + privacy note for the EULA (text written during implementation).

## Testing

- **Unit (xUnit, `Debug R25` + `Debug R24`):** fingerprint stability (element-ID/path/GUID variants collapse; distinct errors don't), sanitizer output, queue cap/rotation/thread-safety, batching triggers, consent gating (no-op when disabled), known-issues matching (shared model with fingerprints + fallback).
- **Worker (vitest + miniflare):** schema validation, idempotency, fingerprint upsert aggregation, known-issue matching, size caps, auth on admin routes.
- **Integration:** `wrangler dev` locally + plugin `TelemetryEndpoint` pointed at localhost → end-to-end event batch and ZIP upload.
- **Live:** manual smoke in Revit (trigger a failing tool 3×, verify queue, flush, prompt, upload).
- **Triage:** `--dry-run` against seeded Worker data.

## Out of Scope (explicit)

- Licensing/activation (the `installations` table is the hook; separate project).
- Customer-facing status portal / developer web dashboard (admin API suffices for now).
- Silent auto-update (UpdateChecker notification already exists).
- Third-party telemetry services (Sentry etc. — rejected: GDPR data-processor and Revit DLL-conflict risk).
- rclog changes beyond the known-issues schema extension.

## Key Parameters (validated with user)

| Parameter | Value |
|---|---|
| Bottleneck thresholds | 10 s duration / 500 KB response (configurable) |
| ZIP prompt trigger | 3rd identical-fingerprint failure per session |
| Consent | first-run dialog, default proposed ON, Settings toggle |
| ZIP cap | 25 MB |
| Retention | events 180 days, ZIPs 90 days |
| Event store | Cloudflare D1 |
| Analysis cap | 3 per triage run |
| Perf digest | weekly (Monday) |
| Admin token location | `~/.revitcortex/triage.json` |
