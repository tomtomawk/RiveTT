# Dev License Backend — Realistic Local Licensing for Test/Demo

**Date:** 2026-07-08
**Branch:** `feature/licensing-phase1`
**Status:** Approved + revised against the evaluation spec (2026-07-08)
**Evaluation spec:** `docs/superpowers/specs/2026-07-08-dev-license-backend-evaluation-design.md`
(reviewed the first draft against the real code; found the `CORTEX-EXPIRED`→Grace bug
and the dead dev-branch wiring — both corrected below)

## Purpose

Make the Fase-1 licensing flow *behave like a real activation* for local testing and
demos, **without** a real licensing server. Today `FakeLicenseBackend` accepts any key
and always returns "active", and its RSA keypair is regenerated on every Revit start
(so the license does not survive a restart — the "N1" note).

We want a demo backend that:

1. **Whitelist** — only specific keys activate; unknown keys are rejected.
2. **Node-lock** — a key binds to the first machine that activates it; a different
   machine is refused.
3. **Survives restart (fix N1)** — once activated, the license stays valid across
   Revit restarts.

This is a **temporary bridge**. Fase 2 replaces it with a real backend (Keygen.sh +
Stripe), at which point this code is deleted. It is therefore confined to **Debug
builds only** — it must never ship in a distribution build (a persisted private signing
key in a release binary would let anyone mint licenses).

### Explicitly out of scope
- The real Keygen/Stripe backend (Fase 2).
- Limiting *which* tools are available when unlicensed (a separate pre-distribution
  phase — tracked as its own task).
- The sales-channel decision (Autodesk App Store vs Keygen+Stripe direct) — a strategic
  decision tracked separately, to be made before Fase 2.

## Why not an off-the-shelf library?

Licensing has two sides. The **server side** (issue keys, validate, node-lock, seats,
revoke, the permanent signing key) will be **Keygen.sh** in Fase 2 — we do not build it.
The **client side** (when to block writes, where to store the token, verify the
signature, grace/anti-rollback, the Revit-styled UI) is inherently app-specific and is
already built and tested (`LicenseManager`, `LicenseGate`, `LicenseWindow`, verifier,
store). `DevLicenseBackend` only *mimics Keygen's responses* until Keygen is wired in —
it is a stand-in for a not-yet-built API, ~150 lines, not a reinvented server.

## Architecture

A **new** class `DevLicenseBackend : ILicenseBackend` in `RevitCortex.Core/Licensing/`.
The existing `FakeLicenseBackend` is **left untouched** — 696 unit tests depend on its
permissive behavior. `ILicenseBackend` is **not** modified. `DevLicenseBackend`
implements its two methods:
- `Activate(licenseKey, fingerprintHashes)` — validate key against the whitelist, apply
  node-lock, and on success mint a signed wire token (same wire format as
  `FakeLicenseBackend`) carrying the whitelist's state + expiry.
- `Validate(wireToken)` — echo a parseable token (online-refresh path; dev needs no
  server round-trip).

Two injected collaborators (fakeable in tests):
- `IDevKeyStore` — load/save the RSA keypair from a local file; exposes the public half.
- `IDevNodeLockStore` — load/save the `key → first fingerprint` map.

### Bootstrap selection — the D4 override (corrected)

**Critical correction (from the evaluation spec).** `LicenseBootstrap.Init` today
*early-returns* for `env.IsDev` and installs a transparent always-Active gate — it never
builds a `LicenseManager`. If we only touched the non-dev branch, `deploy-dev.ps1` (which
sets a dev profile) would take the early-return and **never reach `DevLicenseBackend`** —
the whole feature would be dead code in the exact environment we test in (same class of
"dead wiring" as the earlier F1 bug).

Therefore the selection is by **build configuration, not by profile**:

```
LicenseBootstrap.Init(env)
  try
  #if DEBUG
     // Debug: a REAL manager + DevLicenseBackend, EVEN for the dev profile.
     // D4 (IsDev => transparent) is deliberately SUSPENDED in Debug builds so the
     // gate can actually be exercised live. Debug builds never ship to customers.
     keyStore = new FileDevKeyStore(Path.Combine(env.RootFolder, "dev-license-key.json"))
     nodeLock = new FileDevNodeLockStore(Path.Combine(env.RootFolder, "dev-node-lock.json"))
     pub      = keyStore.PublicOnly()
     verifier = new LicenseTokenVerifier(pub.Modulus, pub.Exponent)
     backend  = new DevLicenseBackend(keyStore, nodeLock)
     manager  = new LicenseManager(store, fingerprint, verifier, clock, backend)
     manager.Refresh()
     Gate = new LicenseGate(() => manager.State, isDev: false)   // isDev:false → gate really evaluates
     Manager = manager; Backend = backend; Fingerprint = fingerprint
  #else
     // Release BEFORE Fase 2: fail-closed-honest. No FakeLicenseBackend (it accepts any
     // key — a fake licensing authority in a production binary). Gate stays null → NO
     // gating → the app runs full, exactly like today's prod 1.0.49, but WITHOUT pretending
     // to have licensing. Real enforcement arrives with Keygen (Fase 2).
     Gate = null; Manager = null; Backend = null; Fingerprint = null
  #endif
  catch { Gate = null; Manager = null; Backend = null; Fingerprint = null }
```

Consequences:
- **Debug (incl. `deploy-dev.ps1`)** → real gate, real `DevLicenseBackend`; the dev
  profile is no longer transparent. This is what makes the live key/state/block test
  possible.
- **Release before Fase 2** → gate null, no `FakeLicenseBackend` wired; app runs
  unrestricted but carries no fake licensing. (The `_fakeKey`/`EmbeddedPublicKey`
  static fields become unused in Release; keep them for Fase 2 or `#if DEBUG` them —
  the plan decides.)
- **`FakeLicenseBackend` stays in the codebase** purely for the 696 unit tests; it is no
  longer wired into the running plugin in either configuration.

The N1 key point holds: in Debug the signer (`DevLicenseBackend`) and the verifier read
the **same** persisted RSA keypair, so a token minted in one session verifies in the
next → the license survives restart.

## Files on disk (Debug-only, never in the repo)

Both are **JSON** (not XML — the evaluation spec is right: JSON avoids older XML crypto
serialization edge cases across net48/net8; the codebase already handles RSA only via
`RSAParameters` byte arrays, never `ToXmlString`). Both live in the profile folder
(`env.RootFolder`, e.g. `~/.revitcortex-dev` for the dev profile), created lazily:

- **`dev-license-key.json`** — the RSA keypair (the "stamp"), stored as base64
  `RSAParameters` fields (modulus, exponent, d, p, q, dp, dq, inverseQ). First Debug run
  generates and saves it; later runs reload it. Signer and verifier both read it → same
  stamp → license survives restart. **Corrupt file handling:** on parse failure, rename
  it to `dev-license-key.json.bad` (best-effort) and regenerate; existing debug tokens
  stop verifying, which is acceptable (local demo state). Delete to reset the stamp.
- **`dev-node-lock.json`** — `{ "format": 1, "locks": { key: firstFingerprint } }`.
  First activation records `key → this machine's fingerprint`. Same fingerprint → OK;
  different → refused. Corrupt/missing → treated as empty. Delete to reset the node-lock.

**Save-failure policy (from evaluation spec):** if the node-lock *write* fails during
activation, the activation itself fails with a readable error — accepting a lock without
persisting it would make the demo inconsistent across restarts. Writes are atomic
(temp-file replace), mirroring `FileLicenseStore`.

Deleting both files (plus `license.json`) resets the demo to a clean slate.

## Whitelist (keys → state)

A fixed map in the Debug-only code, exercising every state by simply changing the key:

| Key entered          | Token state | Token expiry    | State AFTER activation | Demonstrates                          |
|----------------------|-------------|-----------------|------------------------|---------------------------------------|
| `CORTEX-ACTIVE-2026` | active      | now + 1 year    | **Active**             | Full license → writes unlocked        |
| `CORTEX-TRIAL-14`    | trial       | now + 14 days   | **Trial**              | Trial → writes unlocked, "Trial" banner |
| `CORTEX-GRACE`       | active      | now − 1 day     | **Grace**              | Offline grace → writes STILL unlocked |
| *(anything else)*    | —           | —               | activation failure     | Refused: "invalid license key"        |

**Correction (from the evaluation spec) — the earlier `CORTEX-EXPIRED` was wrong.**
`LicenseManager.Activate()` always stores `lastOnlineCheckUtc = now`. `Evaluate()` then
returns **Grace** for an expired token when `now − lastOnlineCheckUtc ≤ GraceWindow`
(10 days). So a token expired-in-the-past *activated right now* evaluates as **Grace**,
and `LicenseGate.Allows()` **permits writes in Grace**. A key literally named
"EXPIRED" that leaves writes enabled would be a misleading demo. So:

- The whitelist key is **`CORTEX-GRACE`** (honest: expired token → Grace → writes still
  allowed, which is the real offline-grace behavior).
- **Hard `Expired`** (writes blocked) is demonstrated **only via a test fixture** — a
  stored `license.json` whose `lastOnlineCheckUtc` is older than the 10-day
  `GraceWindow`, which `Evaluate()` then resolves to `Expired`. This needs **no change**
  to `LicenseManager`. (Live hard-Expired in the UI is out of scope for this bridge; it
  is fully covered by the existing `LicenseManager` tests + this fixture test.)

Keys/states are readable constants, easy to extend. Expiry is computed from an injected
`Func<DateTime> nowUtc` (default `() => DateTime.UtcNow`) so tests are deterministic.

## Gate message — localized + clearer

Today the block message is hard-coded in English in `CortexRouter.cs:194-196`:
`"License expired or invalid — write tool '{toolName}' is blocked"`.

`CortexRouter`, `LicenseGate`, and `Localization` all live in `RevitCortex.Plugin`
(verified), so the router can call `Localization.T(...)` directly — no cross-project
plumbing needed. New localized keys (IT/EN):

- IT: *"Licenza non attiva: RevitCortex Premium funziona in sola lettura. I comandi di
  modifica sono disattivati finché non attivi una licenza valida (RevitCortex > Licenza
  e account)."*
- EN: *"License not active: RevitCortex Premium is running in read-only mode. Editing
  commands are disabled until you activate a valid license (RevitCortex > License &
  Account)."*

The `{toolName}` may be interpolated into a detail line. Keys carry the "read-only"
assurance the user asked for.

## Testing (TDD — each test red first, then green)

- `DevLicenseBackend`:
  - `CORTEX-ACTIVE-2026` → active token, +1 year
  - `CORTEX-TRIAL-14` → trial token, +14 days
  - `CORTEX-GRACE` → active token expired 1 day ago
  - unknown key → Fail
  - empty fingerprint → Fail
- Node-lock:
  - first activation records the fingerprint
  - same fingerprint → OK
  - different fingerprint → Fail ("already activated on another machine")
- Persistence stores:
  - `FileDevKeyStore`: generate+persist on first call; reload identical on second
    instance; a token minted by instance A verifies with instance B's public key
    (simulates restart / N1); corrupt file → renamed `.bad` + regenerated
  - `FileDevNodeLockStore`: bind→get round-trips; persists across instances; corrupt
    file → empty, no crash
- State evaluation via the REAL `LicenseManager` (integration):
  - after activating `CORTEX-GRACE`, `manager.State == Grace` (NOT Expired) — this is
    the honest behavior the corrected whitelist depends on
  - hard `Expired` fixture: a stored `license.json` with `lastOnlineCheckUtc` older than
    `GraceWindow` → `manager.State == Expired` → gate blocks writes (no manager change)
- Gate message localization: `Localization.T("license.gate_blocked", tool)` is
  translated (≠ raw key) and interpolates the tool name — **locale-independent** assert
  (tests may run under "it" on this machine)
- Bootstrap (Debug): `Init` builds `Manager`, `Backend`, `Fingerprint`, and a
  **non-dev** `LicenseGate` even for a dev-profile `env`
- Build **R25 and R24** green (both mandatory); **Release R25** green (the `#else`
  fail-closed path compiles)

## Not touched (scope guard)

`ILicenseBackend`, `LicenseManager`, `LicenseGate` (decision logic), `LicenseTokenVerifier`,
`FileLicenseStore`, and the 696 existing tests. `FakeLicenseBackend` **stays in the
codebase** for those tests but is **no longer wired** into the running plugin in either
configuration. No tool-count limiting (separate task).

## Release-safety checklist (from evaluation spec)

- [ ] `DevLicenseBackend` + private-key persistence compile/select only under `#if DEBUG`.
- [ ] No debug private-key file is ever created by Release code.
- [ ] No private signing key is embedded as a Release resource/constant.
- [ ] Release before Fase 2 is **fail-closed-honest**: gate null, no `FakeLicenseBackend`
      wired (no fake authority in a production binary).
- [ ] Fase 2 replaces the backend with a server-held signing key (Keygen).
- [ ] The sales-channel / distribution decision (Autodesk vs Keygen+Stripe) is tracked
      separately and precedes public packaging.

## Security note

The persisted private signing key is why this whole backend is `#if DEBUG`-gated. In a
Release build the key never exists on the client (and no fake backend is wired at all).
Fase 2's real backend keeps the private key server-side (Keygen), which also resolves N1
permanently (the signing stamp is fixed and server-held, so a stored token stays valid
across restarts with no re-activation).
