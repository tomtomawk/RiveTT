# Dev License Backend — Realistic Local Licensing for Test/Demo

**Date:** 2026-07-08
**Branch:** `feature/licensing-phase1`
**Status:** Approved (brainstorming), ready for implementation plan

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
permissive behavior. `LicenseBootstrap` selects the demo backend **only** under
`#if DEBUG`; Release keeps today's wiring (Fase 2 will swap in `KeygenLicenseBackend`).

`ILicenseBackend` is **not** modified. `DevLicenseBackend` implements its two methods:
- `Activate(licenseKey, fingerprintHashes)` — validate key against the whitelist, apply
  node-lock, and on success mint a signed wire token (same wire format as
  `FakeLicenseBackend`) carrying the whitelist's state + expiry.
- `Validate(wireToken)` — echo a parseable token (online-refresh path; dev needs no
  server round-trip).

Two injected collaborators (fakeable in tests):
- `IDevKeyStore` — load/save the RSA keypair from a local file; exposes the public half.
- `IDevNodeLockStore` — load/save the `key → first fingerprint` map.

```
LicenseBootstrap.Init(env)  [non-dev branch]
  #if DEBUG
     keyStore = new FileDevKeyStore(env.RootFolder)
     nodeLock = new FileDevNodeLockStore(env.RootFolder)
     backend  = new DevLicenseBackend(keyStore, nodeLock)
     verifier = new LicenseTokenVerifier(keyStore.PublicKey.Modulus, .Exponent)  // same keypair
  #else
     backend  = new FakeLicenseBackend(_fakeKey)                                  // unchanged
     verifier = new LicenseTokenVerifier(EmbeddedPublicKey.Modulus, .Exponent)
  #endif
  manager = new LicenseManager(store, fingerprint, verifier, clock, backend)
```

The key point for N1: in Debug the signer (`DevLicenseBackend`) and the verifier read
the **same** RSA keypair from the same file, so a token minted in one session verifies
in the next → the license survives restart.

## Files on disk (Debug-only, never in the repo)

Both live in the profile folder (`~/.revitcortex/`), created lazily:

- **`dev-license-key.xml`** — the RSA keypair (the "stamp"). First Debug run generates
  and saves it; later runs reload it. Signer and verifier both read it → same stamp →
  license survives restart. Delete this file to reset the stamp.
- **`dev-node-lock.json`** — the `key → first-fingerprint` map. First activation of a
  key records `key → this machine's fingerprint`. Re-activation with the same
  fingerprint → OK; a different fingerprint → refused. Delete this file to reset the
  node-lock.

Deleting both files resets the demo to a clean slate.

## Whitelist (keys → state)

A fixed map in the Debug-only code, exercising every state by simply changing the key:

| Key entered          | State  | Expiry            | Demonstrates                                   |
|----------------------|--------|-------------------|------------------------------------------------|
| `CORTEX-ACTIVE-2026` | active | +1 year           | Full license → writes unlocked                 |
| `CORTEX-TRIAL-14`    | trial  | +14 days          | Trial period → writes unlocked, "Trial" banner |
| `CORTEX-EXPIRED`     | active | already expired   | Expired → writes blocked, red banner, grace    |
| *(anything else)*    | —      | —                 | Refused: "invalid license key"                 |

`CORTEX-EXPIRED` mints a token expired in the past; `LicenseManager` (already real)
evaluates it as Expired or Grace depending on age, so the grace window is demonstrable
live. Keys/states are readable constants, easy to extend.

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
  - valid key → token with the expected state
  - unknown key → Fail
  - `CORTEX-TRIAL-14` → trial, +14 days
  - `CORTEX-EXPIRED` → expired token
- Node-lock:
  - first activation records the fingerprint
  - same fingerprint → OK
  - different fingerprint → Fail ("already activated on another machine")
- Key persistence:
  - two backend instances reading the same key file sign/verify consistently
    (simulates a restart)
- Gate message localization: router returns the localized string for the current locale
- Build **R25 and R24** green (both mandatory)

## Not touched (scope guard)

`FakeLicenseBackend`, `LicenseManager`, `LicenseGate` (decision logic), verifier, store,
and the 696 existing tests. No tool-count limiting (separate task). The `#else` /
Release path is unchanged from today.

## Security note

The persisted private signing key is why this whole backend is `#if DEBUG`-gated. In a
Release/distribution build the key never exists on the client; Fase 2's real backend
keeps the private key server-side (Keygen), which also resolves N1 permanently (the
signing stamp is fixed and server-held, so a stored token stays valid across restarts
with no re-activation).
