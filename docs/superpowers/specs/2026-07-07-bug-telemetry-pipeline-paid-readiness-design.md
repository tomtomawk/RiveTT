# Bug Telemetry & Assisted-Fix Pipeline - Paid Readiness Design

**Date:** 2026-07-07
**Status:** Draft for review
**Relationship to prior spec:** Hardened replacement for `2026-07-07-bug-telemetry-pipeline-design.md`. The prior spec remains useful as historical context, but this document is the implementation reference when the two conflict.
**Affects:** RevitCortex.Core (Telemetry namespace), RevitCortex.Plugin (router hook, UI notifier, support report flow, Settings UI), RevitCortex.Server (bridge failure reporting), `ingest-worker/`, `triage/`, `known-issues.json`, release flow, privacy/EULA text.
**Targets:** Revit 2023-2027. Any C# changes must build at minimum `Debug R24` (net48) and `Debug R25` (net8). Core code must stay netstandard2.0/net48-compatible: no records, no init-only setters, no `Dictionary.GetValueOrDefault`, no range/index syntax.

## Purpose

RevitCortex is preparing for paid distribution. Paying customers need faster bug discovery, better bottleneck visibility, and a shorter path from field failure to tested fix. The original telemetry design had the right direction, but the independent review found privacy, consent, threading, and false-positive risks that must be corrected before implementation.

This design keeps the same goal:

1. Collect detailed enough error and performance signals to identify recurring field issues.
2. Deliver those signals reliably without slowing or crashing Revit.
3. Automate triage and assisted fixes where safe.
4. Protect AEC customer data, especially confidential models and EU/GDPR-sensitive context.

The guiding product rule is:

> Automatic telemetry may identify a class of failure, never the customer's model, file system, people, or project.

## Independent Review Findings

### P0 - Consent model must be corrected

The previous design used first-run consent with a pre-selected checkbox. That is not acceptable as a consent mechanism for EU customers. The implementation must require a clear affirmative action. Defaults:

- `EnableTelemetry = false`
- first-run dialog shows two equal choices: enable / keep disabled
- no events are queued before enablement
- withdrawal in Settings is as easy as enabling

Telemetry must be described as **pseudonymous**, not anonymous, because `installationId` creates a stable identifier. The privacy copy must say what is sent, why it is sent, retention, how to disable it, and who controls the endpoint.

### P0 - Diagnostic ZIPs are high-risk data

The existing support report includes `audit.jsonl`, `usage-mcp.db`, `settings.json`, latest Revit journal, and `context.txt`. Current `context.txt` includes username, machine name, document title, and document path. That is appropriate only for explicit diagnostic support, not for automatic or lightly-consented upload.

The new flow must split reports into:

- **Redacted support bundle:** safe default for upload and automated triage.
- **Full diagnostic bundle:** explicit per-report consent only, with a preview of included files and a separate "allow AI-assisted analysis" choice.

No full ZIP should be handed to Claude Code or another AI analysis step unless the user explicitly allowed AI-assisted analysis for that report.

### P1 - Sanitization must be stricter than path/GUID/number removal

The previous sanitizer removed paths, GUIDs, numbers, and Revit element IDs, but still risked leaking parameter names, custom WBS fields, document titles embedded in messages, usernames, and quoted strings.

Automatic events should not transmit free-form tool inputs, parameter names, document titles, usernames, machine names, file paths, or raw Revit exception messages. The safest default is to send hashes/classes, not message text.

### P1 - Revit UI threading needs an explicit owner

The router hook runs on socket/background paths in normal MCP use. It cannot show TaskDialogs directly. Repeated-failure prompts and known-issue notices must be marshalled through a plugin UI component that runs on the Revit/WPF UI thread.

Core telemetry code must never depend on Autodesk.Revit.UI.

### P1 - Exception capture must be route-wide

The router currently audits normal `CortexResult` failures, and the `ToolExecutionHandler` converts some tool exceptions to `Unknown`. Exceptions thrown outside that path can still bypass the audit/telemetry hook and surface as JSON-RPC internal errors.

The implementation must ensure every routed invocation ends in a structured `CortexResult`, then audit, then optional telemetry. Failures in telemetry itself must never affect the tool result.

### P1 - Known-issue matching must avoid customer-visible false positives

Exact fingerprint matches are safe enough for customer-facing "fixed in version X" notices. Fallback matching by `tool + error_code + reporter_version_max` is useful for internal triage, but too broad for customer-facing notifications.

Customer notifications must be based on exact fingerprint matches only.

## Revised Decisions

1. **Transport:** keep own Cloudflare ingest endpoint (`https://ingest.revitcortex.dev`) rather than Sentry, GitHub relay, or SMTP.
2. **Consent:** telemetry is opt-in, disabled by default, and requires an affirmative user action. Full reports and AI-assisted report analysis each require explicit per-report consent.
3. **Automatic event payload:** pseudonymous, minimal, no free-form inputs, no raw exception messages, no document/model identifiers.
4. **Reports:** redacted bundle is default; full bundle is exceptional and clearly labelled.
5. **Known issues:** exact fingerprint drives customer notices; broad fallback is internal only.
6. **Resolution automation:** Claude Code may propose tested PRs, never auto-merge, never release, and never inspect full customer bundles unless explicitly permitted.
7. **Legal readiness:** privacy/EULA text is part of the release gate, not a later marketing task.

## Architecture

```
Customer Revit                         Cloudflare                    Developer PC
Plugin + Server   -- minimal events --> ingest Worker  -- admin -->  rctriage
TelemetryQueue    -- redacted report -> D1 + R2                   GitHub issue/PR
UI Notifier       <-- exact known issue response

Full report upload and AI analysis are separate explicit consent paths.
```

Subsystem boundaries:

- **Core telemetry:** fingerprinting, strict sanitization, queue, sender, configuration. No Revit UI dependency.
- **Plugin telemetry integration:** router hook, startup capture, repeated-failure counters, UI prompts, support report builder/uploader.
- **Server bridge capture:** connection loss, timeout, malformed frame classes, recorded locally and sent only when telemetry is enabled.
- **Worker:** validation, deduplication, aggregate storage, exact known-issue matching, report storage.
- **Triage:** developer-side pull, GitHub issue creation, optional report retrieval, assisted patch PRs.

## Client Capture

### Settings

Extend `~/.revitcortex/settings.json` through merge-write, preserving existing keys:

```json
{
  "EnableTelemetry": false,
  "TelemetryConsentVersion": "2026-07-07",
  "TelemetryConsentAnswered": false,
  "TelemetryEndpoint": "https://ingest.revitcortex.dev",
  "InstallationId": "generated-guid",
  "BottleneckDurationMs": 10000,
  "BottleneckResponseBytes": 512000,
  "ZipPromptFailureThreshold": 3
}
```

Rules:

- `EnableTelemetry` defaults to `false`.
- `InstallationId` is generated only when needed and is not tied to user identity.
- If telemetry is disabled, `ErrorReporter.Record` is a complete no-op and does not queue events.
- Settings UI must allow disabling telemetry with one click.
- If `TelemetryConsentVersion` changes, the user is asked again before automatic telemetry resumes.

### TelemetryEvent schema

Automatic events use a minimal schema:

```json
{
  "schemaVersion": 1,
  "eventId": "guid",
  "installationId": "guid",
  "kind": "error|bottleneck",
  "fingerprint": "16hex",
  "tool": "get_element_parameters",
  "errorCode": "InvalidInput",
  "failureStage": "router|dispatcher|tool|socket|bridge|startup|shutdown",
  "messageClass": "parameter_missing|exception|timeout|permission|unknown",
  "messageOrigin": "templated|exception",
  "sanitizedMessage": "element _ does not exist in the active document",
  "pluginVersion": "1.0.39",
  "revitVersion": "2025",
  "target": "R25",
  "osMajor": "Windows 11",
  "locale": "it",
  "durationMs": 1234,
  "responseBytes": 4567,
  "ts": "2026-07-07T10:30:00Z"
}
```

Automatic events must not include:

- tool input JSON
- `input_summary`
- raw exception message
- stack trace
- document title
- document path
- username or machine name
- parameter names or values
- family names, type names, category display names, or workset names
- Revit element IDs, UniqueIds, GUIDs, or model coordinates

`tool`, `errorCode`, Revit major version, plugin version, duration, and response size are allowed because they are needed for product reliability and do not identify a project by themselves.

### messageOrigin and sanitizedMessage (amendment 2026-07-07)

Hash-only messages would leave triage blind on message content until a report arrives. RevitCortex's own `CortexResult.Fail` messages are developer-controlled templates ("Element {id} does not exist..."), which after strict sanitization are provably safe. Rule:

- `messageOrigin = templated`: the failure message was produced by RevitCortex tool code (structured `CortexResult.Fail`, `errorCode != Unknown`) AND the strict sanitizer verdict is "safe" (no residual quoted strings, path-like tokens, parameter-like tokens, email/user patterns after normalization). Only then `sanitizedMessage` (≤200 chars) is included.
- `messageOrigin = exception`: raw exception text (router `Unknown` wrap, or any message failing the safe verdict — including templated messages that embed `ex.Message`). `sanitizedMessage` is null; only `messageClass` travels.
- **Fail-closed:** any doubt in classification or sanitization → no text.

### Fingerprinting

`ErrorFingerprinter` computes a stable fingerprint from:

```text
tool | errorCode | failureStage | messageClass | normalizedMessage
```

`normalizedMessage` is used locally for hashing only. It is not transmitted. Normalization must:

- lowercase
- remove Windows and UNC paths
- remove emails, usernames, machine names when pattern-detectable
- remove GUIDs and Revit UniqueIds
- remove integer/decimal values
- remove quoted strings
- replace custom parameter-like tokens (`WBS_*`, `Code_*`, `Ifc*`, etc.) with placeholders
- collapse whitespace

If normalization cannot prove a message is safe, the event sends only `messageClass` (no message text; see messageOrigin rule above). No `messageHash` field: the normalized message already feeds the fingerprint, so a separate hash adds no dedup power.

### Message classes

Add a small classifier so telemetry remains useful without raw messages:

- `parameter_missing`
- `invalid_category`
- `permission_denied`
- `read_only_block`
- `timeout`
- `cancelled`
- `transaction_failed`
- `connection_failed`
- `parse_error`
- `exception`
- `unknown`

The classifier may use raw local messages internally, then discard them.

### Queue and sender

`TelemetryQueue`:

- path: `~/.revitcortex/telemetry-queue.jsonl`
- cap: 5 MB
- overflow policy: drop oldest complete lines
- thread-safe lock, same spirit as `AuditLogger`
- malformed queued lines are skipped and removed on next successful compaction

`TelemetrySender`:

- runs off the Revit UI thread
- flushes every 5 minutes, at 20 pending events, and best-effort on shutdown
- HTTP timeout: 5 seconds
- TLS 1.2 enabled for net48
- failures leave events queued
- all public entry points are wrapped; telemetry failures degrade to no-op
- no aggressive retry loop

## Plugin Integration

### Router hook

`CortexRouter.Route` should become the single normal capture point:

1. Validate tool availability and read-only mode.
2. Execute tool through the existing dispatcher path.
3. Convert any uncaught exception into `CortexResult<object>.Fail(CortexErrorCode.Unknown, safeMessage)`.
4. Estimate response bytes.
5. Write `AuditLogger.LogWithPerf`.
6. Call `ErrorReporter.Record` for failures and threshold-exceeding successes.
7. Return the original `CortexResult`.

Telemetry must never change the returned result.

### Exception handling

The router and socket layers must avoid raw JSON-RPC `-32603` escaping without a structured audit trail. When an internal exception occurs:

- local trace may include detailed exception information
- audit stores a truncated safe message as today
- telemetry stores only class/hash/fingerprint
- user-facing result remains structured and helpful

### Repeated-failure prompts

The repeated-failure prompt is owned by `TelemetryUiNotifier` in the plugin layer.

Rules:

- Core raises an event/callback like `RepeatedFailureDetected(fingerprint, count)`.
- Plugin marshals prompt display to the WPF/Revit UI thread.
- Prompt appears once per fingerprint per Revit session.
- Prompt does not appear while a modal Revit operation is active.
- Decline means do not ask again for that fingerprint during the session.
- Accept opens the support report consent flow.

Suggested text:

```text
Questo errore si e' ripetuto 3 volte.
Puoi inviare un report diagnostico a RevitCortex Support per aiutarci a risolverlo.

Il report puo' contenere informazioni del progetto. Prima dell'invio potrai scegliere
tra report redatto e report completo.
```

### Known-issue notices

The Worker may return exact known-issue matches. The plugin shows a non-invasive toast/badge, not a blocking dialog.

Customer-visible notices require:

- exact fingerprint match
- public-safe `title`
- `status = fixed`
- `fixVersion` greater than current plugin version

Fallback matches are internal only and must not trigger a "fixed" notice.

## Support Report Flow

### Report levels

The support command offers two report levels.

#### Redacted support bundle (default)

Contents:

- `context-redacted.json`
- last N audit entries with `input_summary` removed or replaced by structured field counts, and `error_message` passed through the same strict sanitizer used for telemetry (raw Revit messages can embed document titles — same leak channel as automatic events)
- telemetry event samples for triggering fingerprint
- plugin version, Revit version, locale, target Rxx
- settings allowlist: `ReadOnlyMode`, `EnableDynamo`, `EnableCodeExecution`, telemetry thresholds, support keep count
- optional aggregated token/performance summary, not raw `usage-mcp.db`

Excluded:

- document title and path
- username and machine name
- raw Revit journal
- full `settings.json`
- raw `usage-mcp.db`
- tool input values
- model data

#### Full diagnostic bundle

Contents may include the existing files:

- `audit.jsonl`
- `usage-mcp.db`
- `settings.json`
- latest journal capped at 10 MB
- `context.txt`

Before upload, show a preview listing exact file names and a clear warning:

```text
Il report completo puo' contenere nomi di file, percorsi, nome utente, nome macchina,
titolo del modello, parametri e altri dati del progetto. Usalo solo se vuoi aprire
un caso di supporto diagnostico completo.
```

The user must explicitly choose full report. It is never selected by default.

### AI-assisted analysis consent

A separate checkbox appears in the report consent flow:

```text
Consento l'analisi assistita da AI di questo report per tentare una riproduzione
e proporre una correzione. Posso inviare il report anche senza questa opzione.
```

Default: unchecked.

If unchecked:

- report can be stored for human support
- triage script may use metadata and redacted summaries
- Claude Code must not read full report contents

If checked:

- triage script may download and inspect the report for reproduction
- PRs still require human review

### Upload behavior

Primary path:

- `POST /v1/reports`
- multipart ZIP, max 25 MB
- metadata: `installationId`, `fingerprint`, `reportLevel`, `allowAiAnalysis`, `pluginVersion`, `revitVersion`

Fallback:

1. Outlook draft with local ZIP attached.
2. Explorer folder-open if Outlook unavailable.

Upload failure must not delete the local ZIP.

## Ingest Worker

### Public endpoints

`POST /v1/events`

- body: `{ "events": TelemetryEvent[] }`
- max 100 events
- max 256 KB
- schema validation rejects unknown large/free-form fields
- idempotent by `eventId`
- updates fingerprint aggregates
- returns exact known issue matches only:

```json
{
  "accepted": 3,
  "knownIssues": [
    {
      "fingerprint": "a3f9c2e1b0d47f68",
      "issueId": "RC-014",
      "status": "fixed",
      "fixVersion": "1.0.42",
      "publicTitle": "Timeout fixed in clash review on large models"
    }
  ]
}
```

`POST /v1/reports`

- multipart ZIP max 25 MB
- stores to R2
- indexes metadata in D1
- `allowAiAnalysis` and `reportLevel` are required metadata on every report upload; requests missing either are rejected (400) — no server-side default is ever inferred

Anti-noise:

- `X-RC-Key` embedded client key for basic noise filtering only
- rate limiting by IP and `installationId`
- size caps
- strict schema allowlist

### Admin endpoints

Bearer token from Worker secret.

- `GET /v1/admin/fingerprints?since=<ts>`
- `GET /v1/admin/reports/<id>/metadata`
- `POST /v1/admin/reports/<id>/signed-url` (short-lived, generated only on demand)
- `GET /v1/admin/stats?window=7d`

Do not paste signed URLs into durable GitHub issue bodies. Store `reportId` instead; the triage script fetches signed URLs at analysis time.

### D1 schema

`events`

- `eventId` PK
- `fingerprint`
- `kind`
- `tool`
- `errorCode`
- `failureStage`
- `messageClass`
- `messageOrigin`
- `sanitizedMessage` (nullable, templated-origin only)
- `pluginVersion`
- `revitVersion`
- `target`
- `installationId`
- `durationMs`
- `responseBytes`
- `ts`

Retention: 180 days.

`fingerprints`

- `fingerprint` PK
- aggregate fields: firstSeen, lastSeen, occurrences, installCount, versionsSeen, p50/p95 duration, p50/p95 response bytes
- `sampleSanitizedMessage` (templated-origin only, else null) — gives triage issues human-readable text without a report
- no raw message sample

`reports`

- `id` PK
- `r2Key`
- `installationId`
- `fingerprint`
- `reportLevel`
- `allowAiAnalysis`
- `size`
- `pluginVersion`
- `revitVersion`
- `ts`

Retention:

- redacted reports: 90 days
- full reports: 30 days by default
- longer retention only when tied to an explicit active support case

`installations`

- `installationId` PK
- firstSeen, lastSeen, lastPluginVersion, lastRevitVersion
- future licensing hook, not used for activation in this project

## known-issues.json

The public release copy must be safe to expose. Use public-safe fields:

```json
{
  "id": "RC-014",
  "status": "fixed",
  "fix_version": "1.0.42",
  "fix_date": "2026-08-01",
  "reporter_version_max": "1.0.41",
  "fingerprints": ["a3f9c2e1b0d47f68"],
  "tool": "workflow_clash_review",
  "error_code": "Timeout",
  "title": "Clash review timeout on large models",
  "notes": "Fixed timeout handling and added clearer retry guidance."
}
```

Rules:

- `fingerprints` exact match drives customer notification.
- `tool + error_code + reporter_version_max` is internal possible-match only.
- **rclog compatibility (amendment 2026-07-07):** field names stay `title`/`notes` — rclog's `known_issues.py` reads exactly those (a `public_title` rename would silently blank rclog output). Schema changes are additive-only: `id`, `tool`, `status` are mandatory per issue, and in current rclog one malformed item makes the whole list load as empty (broad except). `fingerprints` is additive and ignored by current rclog. The Worker API response may expose the field as `publicTitle`; it maps from `title`.
- `title`/`notes` content in the public repo must not contain customer names, internal stack traces, private repo paths, or model details — changelog tone.
- Release tooling validates that any fixed field issue with customer notification has at least one fingerprint.

## Triage Pipeline

`rctriage` runs on the developer machine, not inside the Worker.

Per run:

1. Pull changed fingerprints since checkpoint.
2. Deduplicate GitHub issues by `<!-- rc-fingerprint: ... -->`.
3. Open or update one issue per fingerprint.
4. Include aggregate stats, affected versions, and `reportId`s only.
5. Do not include signed URLs in issue bodies.
6. Analyze up to 3 issues per run.
7. Download reports only when:
   - report exists
   - admin token is available
   - report metadata allows the intended analysis
   - for full reports, `allowAiAnalysis = true`
8. Attempt reproduction with xUnit or a targeted local harness.
9. If reproducible, create failing test, patch, build `Debug R25` and `Debug R24`, run tests, open PR.
10. If not reproducible, label `needs-repro` and document what evidence is missing.

Guardrails:

- never auto-merge
- never push to main
- never release
- no speculative PRs without repro or strong code-level proof
- every run appends to `~/.revitcortex/triage-runs.jsonl`
- dry-run mode is required and tested before first live run

## Performance Bottleneck Handling

Successful calls can emit bottleneck events only when telemetry is enabled and thresholds are exceeded:

- duration >= `BottleneckDurationMs` (default 10000)
- or responseBytes >= `BottleneckResponseBytes` (default 512000)

Payload remains minimal. Performance digest groups by:

- tool
- pluginVersion
- Revit major version
- p50/p95 duration
- p50/p95 response bytes
- occurrence count
- install count

Perf digest issues must not include installation IDs unless needed for deduplication, and then only in a private hidden marker or local triage cache.

## Privacy and Legal Release Gate

Before shipping to paid users:

- privacy notice exists and is linked from Settings
- EULA/support terms describe telemetry and support report handling
- consent dialog text is reviewed
- withdrawal path is tested
- data retention job is deployed and tested
- admin token handling is documented
- Cloudflare DPA / data processing setup is documented for EU customers
- full-report AI analysis requires explicit per-report consent
- no automatic event contains model or personal identifiers in tests

Terminology:

- use "pseudonymous telemetry"
- do not call `installationId` anonymous
- do not promise EU-only storage beyond what the provider configuration actually guarantees

## Testing

### Unit tests

Run for at least `Debug R25` and `Debug R24` when C# changes are made.

- `EnableTelemetry` default false
- no queue writes when disabled
- `InstallationId` generation and persistence
- consent version re-prompt logic
- fingerprint stability for element IDs, GUIDs, paths, quoted strings, parameter-like tokens
- sanitizer removes paths, emails, usernames, machine names, quoted strings, parameter-like names
- event serializer rejects/freezes unknown free-form fields
- queue cap and drop-oldest behavior
- sender retry and malformed-line compaction
- router exception path still audits and records telemetry class
- exact known-issue match vs fallback possible-match

### Worker tests

Use vitest + miniflare.

- event schema validation
- rejection of raw message/input fields
- idempotent upsert by eventId
- fingerprint aggregation
- exact known-issue response
- fallback not returned to public client
- report metadata validation
- report level retention policy
- admin auth
- rate/size caps

### Integration tests

- `wrangler dev` local endpoint
- plugin pointed at localhost telemetry endpoint
- disabled telemetry produces no requests
- enabled telemetry sends minimal batch
- repeated failure triggers UI notifier on UI thread
- redacted report upload
- full report upload only after explicit consent
- known issue response produces non-blocking notice

### Live Revit smoke

- trigger a structured `InvalidInput` failure three times
- verify local counter and prompt
- send redacted report
- inspect uploaded bundle for forbidden fields
- simulate worker outage and verify Revit remains responsive
- run one heavy successful tool above threshold and verify bottleneck event

## Rollout Plan

### Phase 0 - Spec and privacy copy

- approve this design
- write first-run consent copy
- write Settings privacy copy
- write report-level consent copy
- update `docs/SECURITY.md` with the new outbound telemetry surface
- update product technical spec (`RevitCortex-SpecificaTecnica-2026-06-16.md`, OneDrive product folder): RNF-01 "local-first" exception list gains pseudonymous telemetry (opt-in, no BIM data — same standard as the update-checker exception) and RF-07 gains the report-upload path

### Phase 1 - Local client capture

- settings
- fingerprinting
- sanitizer
- queue
- sender abstraction with test endpoint
- router hook and route-wide exception capture
- no production endpoint enabled yet

### Phase 2 - Worker events

- Cloudflare Worker
- D1 schema
- event ingestion
- exact known-issue matching
- admin stats
- retention cron

### Phase 3 - Support report hardening

- redacted bundle
- full bundle preview
- upload endpoint
- Outlook/explorer fallback preserved
- AI analysis consent metadata

### Phase 4 - Triage automation

- `rctriage` scripts
- GitHub issue dedup
- dry-run mode
- report metadata checks
- assisted PR creation with R25/R24 build gates

### Phase 5 - Paid release gate

- live smoke with real Revit
- privacy scan of sample events and reports
- Worker deployment review
- update release checklist
- publish public-safe `known-issues.json`

## Acceptance Criteria

The pipeline is ready for paid distribution only when:

- no automatic telemetry occurs before affirmative opt-in
- disabling telemetry immediately stops queueing and sending
- automatic event payloads contain no raw inputs, raw messages, paths, document names, usernames, machine names, parameter names, or element identifiers
- adversarial sanitizer tests (document titles, parameter names, usernames, quoted strings embedded in messages) pass with fail-closed suppression; exception-origin events never carry message text
- route-wide exceptions produce structured audit and telemetry classes
- repeated-failure prompts are UI-thread safe
- redacted report is the default
- full report requires explicit consent and visible file preview
- AI-assisted analysis of full reports requires separate explicit consent
- customer known-issue notices require exact fingerprint match
- fallback matching is internal only
- event retention and report retention are enforced
- C# changes build in `Debug R25` and `Debug R24`
- worker tests and dry-run triage pass

## Out of Scope

- Licensing/activation beyond the `installations` future hook
- customer-facing status portal
- silent auto-update
- third-party telemetry processors
- automatic merge or automatic release
- remote execution of fixes on customer machines

## Implementation Notes

- **Branch hygiene:** implement on a dedicated branch cut from `main` (NOT `feature/dynamo-integration`, where the spec was first drafted), in an isolated git worktree — a parallel workstream (generative-model-creation, spec+plan of the same date in the OneDrive product folder) targets the same source clone, and concurrent sessions share the git index.
- Prefer small classes with clear boundaries: `TelemetryConfig`, `TelemetryEvent`, `ErrorFingerprinter`, `MessageClassifier`, `MessageSanitizer`, `TelemetryQueue`, `TelemetrySender`, `TelemetryUiNotifier`.
- Keep Core free of Revit UI references.
- Use merge-write for settings so existing `ReadOnlyMode`, `EnableDynamo`, `EnableCodeExecution`, `DisabledTools`, and support settings are preserved.
- Do not add write tools with read-only prefixes.
- Do not skip `AuditLogger.LogWithPerf`; telemetry builds on audit, it does not replace it.
- Avoid adding new dependencies that complicate Revit net48 loading.
