# Dev License Backend - Evaluation Spec

**Date:** 2026-07-08
**Branch:** `feature/licensing-phase1`
**Status:** Draft for review with coding agents
**Related spec:** `docs/superpowers/specs/2026-07-08-dev-license-backend-design.md`

## Purpose

Evaluate and tighten the proposed `DevLicenseBackend` before implementation.

The original direction is sound: use a local debug-only backend that behaves more like
a real licensing authority, while the existing client path (`LicenseManager`,
`LicenseTokenVerifier`, `FileLicenseStore`, `LicenseGate`, `LicenseWindow`) remains
the thing being exercised. This spec exists to make the review concrete and to call out
the integration traps discovered in the current code.

The coding-agent review should answer one question:

> Can we implement a realistic local demo licensing backend without weakening release
> safety, without changing `ILicenseBackend`, and without misleading demos about
> Grace vs Expired behavior?

## Current Facts From Code

These facts should be treated as constraints, not assumptions:

- `LicenseBootstrap.Init(env)` currently exits early for `env.IsDev` and creates a
  transparent always-active `LicenseGate`; it does not create a `LicenseManager`.
- The non-dev path currently wires `FakeLicenseBackend`, whose `Activate()` accepts
  every key.
- `LicenseManager.Activate()` always saves `StoredLicenseState(result.Token, now, now)`.
- `LicenseManager.Evaluate()` returns `Grace` for an expired token when
  `now - lastOnlineCheckUtc <= GraceWindow`.
- `LicenseGate.Allows()` blocks write tools only for `Expired` and `Invalid`.
  `Grace` is intentionally allowed.
- `WindowsFingerprintProvider` currently returns one hashed attribute: MachineGuid.
- `FileLicenseStore` already has the right local-file discipline: separate file,
  best-effort load/save, atomic temp replace, never crash Revit.
- Revit 2023/2024 still compile as `net48`; Revit 2025+ compile as modern .NET.

## Recommendation

Proceed with `DevLicenseBackend`, but revise the design in four places before coding.

1. Replace the dev-profile transparent gate in Debug builds with a real
   `LicenseManager` backed by `DevLicenseBackend`.
2. Do not claim that an activation key can demonstrate hard `Expired` under the
   current `LicenseManager` contract. A freshly activated expired token will enter
   `Grace`, not hard `Expired`.
3. Keep all private-key persistence and node-lock persistence debug-only and local to
   the active profile folder.
4. Add an explicit release-safety review item so no one mistakes this bridge for a
   distributable licensing backend.

## Non-Goals

- Do not build Keygen/Stripe.
- Do not modify `ILicenseBackend`.
- Do not change `LicenseManager` state semantics unless the review explicitly decides
  the demo must show hard `Expired` by entering only a key.
- Do not change read-only tool classification.
- Do not limit tool count or feature tiers.
- Do not make local dev licensing secure against a developer with filesystem access.

## Selector Design To Review

Recommended selector:

```csharp
public static void Init(CortexEnvironment env)
{
    try
    {
#if DEBUG
        BuildDebugLicensingStack(env);     // real manager + DevLicenseBackend
#else
        BuildCurrentPhase1Stack(env);      // unchanged until Fase 2
#endif
    }
    catch
    {
        Gate = null;
        Manager = null;
        Backend = null;
        Fingerprint = null;
    }
}
```

Important behavior:

- In Debug, `env.IsDev` must no longer mean "licensing transparent".
- The debug stack should use `env.RootFolder`, so dev installs use
  `~/.revitcortex-dev` and ordinary debug runs use the current profile root.
- The `LicenseGate` created for the debug stack should use `isDev: false`; otherwise
  `LicenseGate.CurrentState()` always returns `Active` and the demo backend cannot
  exercise blocked writes.
- Release keeps the existing Phase 1 behavior only because Phase 1 is not a
  distribution-ready licensing system. The release/distribution decision is a separate
  gate before public shipping.

Coding-agent review question:

- Should a Release build before Fase 2 fail closed instead of keeping
  `FakeLicenseBackend`? If yes, this belongs in the implementation plan as a separate
  release-safety task.

## Backend Contract

Create `DevLicenseBackend : ILicenseBackend`.

Responsibilities:

- Reject unknown keys.
- For known keys, mint signed wire tokens in exactly the same format verified by
  `LicenseTokenVerifier`: `base64(payloadJsonUtf8) + "." + base64(signature)`.
- Store and enforce `key -> first fingerprint hash` node locks.
- Use the current fingerprint hashes passed to `Activate()` as the token
  `fingerprintHashes`.
- `Validate(wireToken)` may echo a structurally valid token for Phase 1, matching
  the current fake online-refresh behavior.

Avoid:

- Do not reinterpret license state in `DevLicenseBackend`.
- Do not bypass `LicenseManager.Refresh()` or `LicenseGate`.
- Do not store node-lock state in `settings.json`.
- Do not make the local backend responsible for read-only/write classification.

## Local Debug Files

Recommended files:

- `dev-license-key.json`
- `dev-node-lock.json`

Rationale:

- Prefer JSON over XML for the RSA key material. The existing verifier already works
  with `RSAParameters.Modulus` and `RSAParameters.Exponent`, and JSON avoids older
  XML crypto serialization edge cases across `net48` and modern .NET targets.
- Store private key parameters as base64 strings in the debug profile folder only.
- These files are local developer/demo state and must never be committed.

Suggested `dev-license-key.json` shape:

```json
{
  "format": 1,
  "algorithm": "RSA-2048-PKCS1-SHA256",
  "d": "...",
  "dp": "...",
  "dq": "...",
  "exponent": "...",
  "inverseQ": "...",
  "modulus": "...",
  "p": "...",
  "q": "..."
}
```

Suggested `dev-node-lock.json` shape:

```json
{
  "format": 1,
  "locks": {
    "CORTEX-ACTIVE-2026": "first-fingerprint-hash"
  }
}
```

Store behavior:

- Load failure returns empty/new state.
- A malformed/corrupt key file should be renamed with a `.bad` suffix when possible,
  then regenerated. Existing debug tokens become invalid, which is acceptable because
  the files are local demo state.
- Save failure fails the activation safely with a readable error, because accepting a
  node-lock without persisting it would make the demo behavior inconsistent.
- Writes should be atomic, mirroring `FileLicenseStore`.

## Whitelist Design

Recommended keys:

| Key entered | Token state | Expiry | Expected evaluated state after activation |
|---|---|---:|---|
| `CORTEX-ACTIVE-2026` | `active` | now + 1 year | `Active` |
| `CORTEX-TRIAL-14` | `trial` | now + 14 days | `Trial` |
| `CORTEX-GRACE` | `active` | now - 1 day | `Grace` |
| anything else | none | none | activation failure |

Do not include `CORTEX-EXPIRED` as a normal activation-demo key unless the behavior is
renamed or the manager contract changes. Under the current manager, an expired token
activated right now has `lastOnlineCheckUtc = now`, so it evaluates as `Grace`.

If the demo must show hard `Expired`, use one of these explicit options:

1. **Recommended:** keep hard `Expired` as a unit/integration test using a stored
   license fixture whose `lastOnlineCheckUtc` is older than `GraceWindow`.
2. Add a tiny debug-only "seed expired store" helper outside `ILicenseBackend`.
3. Modify `LicenseManager.Activate()` to support backend-provided online-check
   metadata. This is not recommended for the temporary bridge because it changes a
   production-facing contract to serve a demo case.

## Node-Lock Semantics

Use the first current fingerprint hash as the node-lock value.

Activation rules:

- No current fingerprint hashes: fail activation with "machine fingerprint unavailable".
- Key not in whitelist: fail with "invalid license key".
- Key in whitelist and no lock exists: save `key -> firstFingerprintHash`, then mint.
- Key in whitelist and lock equals current first fingerprint: mint.
- Key in whitelist and lock differs: fail with "license key already activated on another machine".

This matches the current Phase 1 fingerprint reality: there is one MachineGuid-derived
hash. If future fingerprints become multi-attribute, token validation may use a subset
rule, but node-lock persistence should stay deterministic.

## Gate Message Localization

The existing hard-coded router message should become localized.

Recommended keys:

- `license.gate_blocked_message`
- `license.gate_blocked_suggestion`

Message intent:

- Explain that Premium is in read-only mode because the license is not active.
- Reassure that read-only commands remain available.
- Tell the user where to activate.
- Optionally include the tool name in the suggestion or detail, not in the headline.

This is a user-facing polish task, not part of `DevLicenseBackend`, but it belongs in
the same review because the demo backend will make the block path visible.

## Testing Requirements

Unit tests:

- valid active key returns a signed token that verifies with the persisted public key.
- unknown key fails.
- trial key evaluates as `Trial`.
- grace key evaluates as `Grace` after normal `LicenseManager.Activate()`.
- first activation writes node-lock state.
- same fingerprint re-activation succeeds.
- different fingerprint re-activation fails.
- missing fingerprint fails.
- two backend instances reading the same key file produce tokens verifiable by the same
  public key, simulating Revit restart.
- malformed/corrupt key file is renamed when possible, regenerated, and old debug
  tokens no longer verify.
- malformed/corrupt node-lock file does not crash Revit.
- localized router license gate message is returned for Italian and English locales.

Integration or bootstrap tests:

- Debug build path creates `Manager`, `Backend`, `Fingerprint`, and a non-dev
  `LicenseGate`.
- Debug dev profile no longer shows the transparent-license path.
- Release build excludes or cannot select `DevLicenseBackend`.
- Existing `FakeLicenseBackend` tests remain unchanged.

Manual smoke test:

- Delete `dev-license-key.json`, `dev-node-lock.json`, and `license.json`.
- Start a Debug dev build.
- Confirm the license window starts as invalid/not activated.
- Activate `CORTEX-ACTIVE-2026`.
- Restart Revit.
- Confirm the license remains active without re-activation.
- Activate `CORTEX-GRACE`.
- Confirm write tools remain allowed because the evaluated state is `Grace`.
- Force an old `lastOnlineCheckUtc` fixture, refresh, and confirm write tools are blocked
  as hard `Expired`.

Required builds after any C# implementation:

```powershell
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

## Release-Safety Checklist

Before implementation is accepted:

- [ ] `DevLicenseBackend` and private-key persistence are compiled or selected only for
      Debug builds.
- [ ] No debug private key file is created by Release code.
- [ ] No private signing key is embedded as a Release resource or constant.
- [ ] The release/distribution story is explicitly tracked before public packaging.
- [ ] Fase 2 still replaces the backend with a server-held signing key.

## Review Prompts For Coding Agents

Ask each coding agent to respond to these prompts:

1. Does this selector design correctly replace the current dev transparent path without
   changing production-facing interfaces?
2. Is there any way `CORTEX-EXPIRED` can honestly demonstrate hard `Expired` without
   modifying `LicenseManager.Activate()` or seeding store metadata?
3. Should Release before Fase 2 fail closed, keep current fake behavior, or be blocked
   only by packaging process?
4. Is JSON `RSAParameters` persistence acceptable across `net48` and `net8`, or is
   there a better local-only format already used in the project?
5. Are there race/concurrency risks in the node-lock store worth handling now?
6. Are the proposed tests enough to protect N1: restart persistence?

## Acceptance Criteria

The design is ready for implementation planning when:

- The team agrees whether Debug means all debug builds or only dev-profile builds.
- The team agrees how hard `Expired` should be demonstrated.
- The team agrees on Release-before-Fase-2 behavior.
- The coding agents find no blocker in cross-target RSA persistence.
- The final implementation plan keeps `ILicenseBackend` unchanged.
