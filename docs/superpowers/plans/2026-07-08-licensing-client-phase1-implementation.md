# RevitCortex Licensing Client (Fase 1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the entire client-side entitlement layer (node-lock, license states, offline grace, gentle read-only degradation) with the backend behind an interface so it is fully TDD-testable now and the real backend attaches in Fase 2 without touching logic.

**Architecture:** The state logic lives in `RevitCortex.Core.Licensing` (netstandard2.0, Newtonsoft-only, no Revit/Windows) and is 100% unit-testable via fakes; the hardware collection, atomic file store, anti-rollback clock, router gate, bootstrap and UI live in `RevitCortex.Plugin.Licensing`. A `LicenseTokenVerifier` (RSA-2048, net48-safe key import) feeds a pure `LicenseManager` state machine; a `LicenseGate` translates state into an additive router guard. Faults fail **closed** (Invalid), and licensing never crashes Revit because gating is opt-in at the router (a null gate = no gating) and every I/O path is total (try/catch, never throws).

**Tech Stack:** C# (netstandard2.0 Core + net48/net8/net10 Plugin), RSA-2048, Newtonsoft.Json, xUnit.

---

## Pinned cross-group contracts (canonical — every task cites these identically)

These signatures are authoritative. No task may "adapt at implementation time".

```csharp
// RevitCortex.Core/Licensing/LicenseState.cs
public enum LicenseState { Invalid = 0, Expired = 1, Grace = 2, Trial = 3, Active = 4 }
// XML-doc contract: numeric order is NOT semantically comparable with < / >.
// The only guarantee is default(LicenseState) == Invalid (fail-closed).

// RevitCortex.Core/Licensing/LicenseToken.cs  (PAYLOAD-ONLY class; no signature, no wire token)
public class LicenseToken
{
    public string LicenseId { get; }
    public string State { get; }
    public DateTime ExpiresAtUtc { get; }
    public int SeatLimit { get; }
    public IReadOnlyList<string> FingerprintHashes { get; }
    public DateTime IssuedAtUtc { get; }
    public LicenseToken(string licenseId, string state, DateTime expiresAtUtc, int seatLimit,
                        IReadOnlyList<string> fingerprintHashes, DateTime issuedAtUtc);
    public static LicenseToken? FromJson(string json); // never throws; null on malformed
}

// RevitCortex.Core/Licensing/ILicenseStore.cs
public class StoredLicenseState
{
    public string Token { get; }                 // wire base64: payload.sig
    public DateTime? LastOnlineCheckUtc { get; }
    public DateTime HighWaterMarkUtc { get; }
    public StoredLicenseState(string token, DateTime? lastOnlineCheckUtc, DateTime highWaterMarkUtc);
}
public interface ILicenseStore
{
    StoredLicenseState? Load();        // null on missing/unreadable, never throws
    void Save(StoredLicenseState state);
}

// RevitCortex.Core/Licensing/ISystemClock.cs
public interface ISystemClock { DateTime UtcNow { get; } }

// RevitCortex.Core/Licensing/IFingerprintProvider.cs
public interface IFingerprintProvider { IReadOnlyList<string> GetHashedAttributes(); }

// RevitCortex.Core/Licensing/LicenseTokenVerifier.cs
public class LicenseTokenVerifier
{
    public LicenseTokenVerifier(byte[] modulus, byte[] exponent); // clones the arrays (fix #10)
    public LicenseToken? Verify(string wireToken);
    // Decodes segment-1 base64 and verifies RSA over THOSE VERBATIM BYTES (no re-serialize).
    // net48-safe: RSA.Create() + ImportParameters(RSAParameters{Modulus,Exponent}) + VerifyData.
    // NEVER ImportSubjectPublicKeyInfo / ImportRSAPublicKey.
}

// RevitCortex.Core/Licensing/ILicenseBackend.cs
public interface ILicenseBackend
{
    LicenseActivationResult Activate(string licenseKey, IReadOnlyList<string> fingerprintHashes);
    LicenseActivationResult Validate(string wireToken);
}
public class LicenseActivationResult
{
    public bool Success { get; }
    public string? Token { get; }   // wire token on success
    public string? Error { get; }
    public static LicenseActivationResult Ok(string token);
    public static LicenseActivationResult Fail(string error);
}

// RevitCortex.Core/Licensing/FakeLicenseBackend.cs
// Ctor takes a runtime RSA (RSA.Create(2048)); signs the SAME bytes it puts in segment 1.
// Exposes the public half as RSAParameters (PublicKeyParameters) or Modulus/Exponent — NEVER const.

// RevitCortex.Core/Licensing/LicenseManager.cs  (FULL surface consumed downstream)
public class LicenseManager
{
    public LicenseManager(ILicenseStore store, IFingerprintProvider fingerprint,
                          LicenseTokenVerifier verifier, ISystemClock clock);
    // Pure state machine (spec §4). Also usable statically-shaped via the pure overload:
    public LicenseState Evaluate(LicenseToken? token, DateTime nowUtc, DateTime? lastOnlineCheckUtc,
                                 IReadOnlyList<string> currentFingerprint, DateTime highWaterMarkUtc);
    public LicenseState State { get; }                 // last evaluated (cache); default Invalid
    public DateTime? ExpiresAtUtc { get; }
    public int GraceDaysRemaining { get; }
    public string LicenseIdTruncated { get; }
    public void Refresh();                             // re-read store+clock+fingerprint, update cache
    public LicenseActivationResult Activate(string licenseKey); // backend -> store.Save -> Refresh
    // NO public Store property. Fingerprint containment: token.FingerprintHashes ⊆ current.
    // IsTrustedState(state) == active|trial (re-checked before Grace on an expired token).
}
```

- **Fingerprint containment direction (spec §4.3):** Invalid unless **every** hash in `token.FingerprintHashes` is present in `provider.GetHashedAttributes()` (i.e. `token.FingerprintHashes ⊆ current`). An empty token hash list ⇒ Invalid.
- **Embedded public key** in `LicenseBootstrap` is `static readonly` (never `const`); in Fase 1 it is the public half of `FakeLicenseBackend`'s runtime test keypair, marked "replace with backend key in Fase 2".
- **Grace anchor** is measured from `lastOnlineCheckUtc` (spec §4.6). `lastOnlineCheckUtc == null` ⇒ Expired.

## Invariants every task obeys

- Cross-target Core: no `record` / `init` / `Dictionary.GetValueOrDefault` / `Index`-`Range` (`^`/`..`) / `IAsyncEnumerable` / file-scoped **types** / default interface methods. Use `class` + `{ get; }` + ctor. `string?` and file-scoped **namespaces** are allowed (Core has `<Nullable>enable</Nullable>`, `LangVersion=latest`).
- Each task: (1) failing test with FULL code → (2) run & expect the SPECIFIC failure → (3) minimal impl with FULL code → (4) run & expect PASS with the exact test-case count → (5) build gate `dotnet build -c "Debug R25"` AND `-c "Debug R24"` on `src/RevitCortex.Plugin/RevitCortex.Plugin.csproj` (both 0 errors) → (6) commit staging ONLY the task's files with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer.
- `license.json` path is always `Path.Combine(CortexEnvironment.Current.RootFolder, "license.json")` — NEVER `SettingsFilePath`.
- Fingerprint is **registry-only MachineGuid** in Fase 1. `System.Management`/WMI is NOT added. (WMI BIOS/mobo is an optional future extension, outside the numbered tasks — see the note after Task 8.)
- Fakes (`TestClock`, `FakeFingerprintProvider`, `InMemoryLicenseStore`, `FakeLicenseBackend`) live in `RevitCortex.Core.Licensing` so every downstream group reuses them.

---

### Task 1: LicenseState enum

**Files:**
- Create: `src/RevitCortex.Core/Licensing/LicenseState.cs`
- Test: `src/RevitCortex.Tests/Licensing/LicenseStateTests.cs`

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/LicenseStateTests.cs`:
```csharp
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseStateTests
{
    [Fact]
    public void Enum_HasExactlyFiveMembers()
    {
        Assert.Equal(5, System.Enum.GetNames(typeof(LicenseState)).Length);
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Trial")]
    [InlineData("Grace")]
    [InlineData("Expired")]
    [InlineData("Invalid")]
    public void Enum_DefinesExpectedMember(string member)
    {
        Assert.True(System.Enum.IsDefined(typeof(LicenseState), member));
    }

    [Fact]
    public void Invalid_IsDefaultValue()
    {
        Assert.Equal(LicenseState.Invalid, default(LicenseState));
    }

    [Fact]
    public void NumericValues_ArePinned()
    {
        Assert.Equal(0, (int)LicenseState.Invalid);
        Assert.Equal(1, (int)LicenseState.Expired);
        Assert.Equal(2, (int)LicenseState.Grace);
        Assert.Equal(3, (int)LicenseState.Trial);
        Assert.Equal(4, (int)LicenseState.Active);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~LicenseStateTests"` → compile error: `LicenseState` does not exist in namespace `RevitCortex.Core.Licensing`.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/LicenseState.cs`:
```csharp
namespace RevitCortex.Core.Licensing;

/// <summary>
/// Client-resolved entitlement state. Fail-closed: the default (0) value is Invalid,
/// so an uninitialized or corrupt state is never mistaken for a valid one.
/// NOTE: the numeric order is NOT semantically comparable with &lt; / &gt;; the only
/// contract is default(LicenseState) == Invalid.
/// </summary>
public enum LicenseState
{
    Invalid = 0,
    Expired = 1,
    Grace = 2,
    Trial = 3,
    Active = 4
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **8 passed** (2 plain `[Fact]` + `Invalid_IsDefaultValue` + `NumericValues_ArePinned` + the `[Theory]`'s 5 `[InlineData]` cases), 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both must report 0 errors.
- [ ] **Step 6 — Commit** (stage ONLY this task's files):
```
git add src/RevitCortex.Core/Licensing/LicenseState.cs src/RevitCortex.Tests/Licensing/LicenseStateTests.cs
git commit -m "feat(licensing): add LicenseState enum (fail-closed default Invalid)" -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: LicenseToken model + FromJson (Date-JValue-safe parsing)

**Files:**
- Create: `src/RevitCortex.Core/Licensing/LicenseToken.cs`
- Test: `src/RevitCortex.Tests/Licensing/LicenseTokenTests.cs`

Payload-only class: no signature, no wire token (the raw wire token lives in `StoredLicenseState.Token`, Task 5). Fix #1: `FromJson` parses dates from the JValue `Date` token DIRECTLY — `JObject.Parse` materializes `"…Z"` as a `Date` JValue, so casting to `(string?)` would silently fail the round-trip ("green for the wrong reason"). A raw-ISO-string test pins it.

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/LicenseTokenTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseTokenTests
{
    [Fact]
    public void Ctor_ExposesAllReadonlyProperties()
    {
        var issued = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expires = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fps = new List<string> { "aaa", "bbb" };

        var token = new LicenseToken("lic-123", "active", expires, 5, fps, issued);

        Assert.Equal("lic-123", token.LicenseId);
        Assert.Equal("active", token.State);
        Assert.Equal(expires, token.ExpiresAtUtc);
        Assert.Equal(5, token.SeatLimit);
        Assert.Equal(new[] { "aaa", "bbb" }, token.FingerprintHashes);
        Assert.Equal(issued, token.IssuedAtUtc);
    }

    [Fact]
    public void FromJson_ParsesCanonicalPayload_DatesAsUtc()
    {
        const string json = @"{
            ""licenseId"": ""lic-abc"",
            ""state"": ""trial"",
            ""expiresAtUtc"": ""2026-12-31T23:59:59Z"",
            ""seatLimit"": 3,
            ""fingerprintHashes"": [""h1"", ""h2"", ""h3""],
            ""issuedAtUtc"": ""2026-06-01T00:00:00Z""
        }";

        var token = LicenseToken.FromJson(json);

        Assert.NotNull(token);
        Assert.Equal("lic-abc", token!.LicenseId);
        Assert.Equal("trial", token.State);
        Assert.Equal(DateTimeKind.Utc, token.ExpiresAtUtc.Kind);
        Assert.Equal(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc), token.ExpiresAtUtc);
        Assert.Equal(3, token.SeatLimit);
        Assert.Equal(3, token.FingerprintHashes.Count);
        Assert.Equal(DateTimeKind.Utc, token.IssuedAtUtc.Kind);
        Assert.Equal(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), token.IssuedAtUtc);
    }

    // fix #1 guard: JObject.Parse materializes an ISO string ending in Z as a Date JValue.
    // Reading it as a Date (not via (string?)) must still yield the correct UTC instant.
    [Fact]
    public void FromJson_RawIsoDate_MaterializedAsDateJValue_ParsesToUtc()
    {
        const string json =
            @"{""licenseId"":""x"",""state"":""active"",""expiresAtUtc"":""2026-05-04T03:02:01Z""," +
            @"""seatLimit"":1,""fingerprintHashes"":[""h""],""issuedAtUtc"":""2026-05-01T00:00:00Z""}";

        var token = LicenseToken.FromJson(json);

        Assert.NotNull(token);
        Assert.Equal(new DateTime(2026, 5, 4, 3, 2, 1, DateTimeKind.Utc), token!.ExpiresAtUtc);
        Assert.Equal(DateTimeKind.Utc, token.ExpiresAtUtc.Kind);
    }

    [Fact]
    public void FromJson_MissingFingerprintHashes_YieldsEmptyList()
    {
        const string json = @"{
            ""licenseId"": ""lic-x"",
            ""state"": ""active"",
            ""expiresAtUtc"": ""2026-12-31T23:59:59Z"",
            ""seatLimit"": 1,
            ""issuedAtUtc"": ""2026-06-01T00:00:00Z""
        }";

        var token = LicenseToken.FromJson(json);

        Assert.NotNull(token);
        Assert.NotNull(token!.FingerprintHashes);
        Assert.Empty(token.FingerprintHashes);
    }

    [Fact]
    public void FromJson_InvalidJson_ReturnsNull()
    {
        Assert.Null(LicenseToken.FromJson("not-json{{{"));
    }

    [Fact]
    public void FromJson_EmptyOrNull_ReturnsNull()
    {
        Assert.Null(LicenseToken.FromJson(""));
        Assert.Null(LicenseToken.FromJson(null!));
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~LicenseTokenTests"` → compile error: `LicenseToken` does not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/LicenseToken.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// The signed license payload as understood by the client (PAYLOAD-ONLY: no signature,
/// no wire token — the raw wire token lives in StoredLicenseState.Token). Immutable
/// class (net48-safe: no record/init). FromJson never throws (null on malformed input).
/// </summary>
public class LicenseToken
{
    public string LicenseId { get; }
    public string State { get; }
    public DateTime ExpiresAtUtc { get; }
    public int SeatLimit { get; }
    public IReadOnlyList<string> FingerprintHashes { get; }
    public DateTime IssuedAtUtc { get; }

    public LicenseToken(
        string licenseId,
        string state,
        DateTime expiresAtUtc,
        int seatLimit,
        IReadOnlyList<string> fingerprintHashes,
        DateTime issuedAtUtc)
    {
        LicenseId = licenseId ?? "";
        State = state ?? "";
        ExpiresAtUtc = expiresAtUtc;
        SeatLimit = seatLimit;
        FingerprintHashes = fingerprintHashes ?? new List<string>();
        IssuedAtUtc = issuedAtUtc;
    }

    public static LicenseToken? FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var o = JObject.Parse(json);

            var hashes = new List<string>();
            if (o["fingerprintHashes"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    var s = (string?)t;
                    if (!string.IsNullOrEmpty(s)) hashes.Add(s!);
                }
            }

            return new LicenseToken(
                (string?)o["licenseId"] ?? "",
                (string?)o["state"] ?? "",
                ReadUtc(o["expiresAtUtc"]),
                o["seatLimit"] != null ? (int)o["seatLimit"]! : 0,
                hashes,
                ReadUtc(o["issuedAtUtc"]));
        }
        catch
        {
            return null;
        }
    }

    // fix #1: Newtonsoft materializes an ISO "…Z" string as a Date JValue. Read the Date
    // directly (NOT via (string?)) so the round-trip is correct; fall back to string parse
    // only when the token is not already a Date.
    private static DateTime ReadUtc(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return DateTime.MinValue;
        if (token.Type == JTokenType.Date)
            return ((DateTime)token).ToUniversalTime();

        var s = (string?)token;
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        var dt = DateTime.Parse(
            s,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **7 passed** (7 `[Fact]` methods), 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors (R24/net48 confirms no `record`/`init`/`Index`/`Range` slipped in).
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/LicenseToken.cs src/RevitCortex.Tests/Licensing/LicenseTokenTests.cs
git commit -m "feat(licensing): add LicenseToken payload model + Date-safe FromJson" -m "Immutable class (net48-safe); FromJson never throws and reads ISO dates from the Date JValue directly (avoids the (string?) round-trip footgun) -> UTC." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: ISystemClock + SystemClock + TestClock

**Files:**
- Create: `src/RevitCortex.Core/Licensing/ISystemClock.cs`
- Test: `src/RevitCortex.Tests/Licensing/SystemClockTests.cs`

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/SystemClockTests.cs`:
```csharp
using System;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class SystemClockTests
{
    [Fact]
    public void SystemClock_ReturnsUtcNow_KindUtc()
    {
        Assert.Equal(DateTimeKind.Utc, new SystemClock().UtcNow.Kind);
    }

    [Fact]
    public void SystemClock_IsMonotonicAcrossReads()
    {
        var clock = new SystemClock();
        var a = clock.UtcNow;
        var b = clock.UtcNow;
        Assert.True(b >= a);
    }

    [Fact]
    public void TestClock_ReturnsFixedValue()
    {
        var fixedNow = new DateTime(2026, 5, 4, 3, 2, 1, DateTimeKind.Utc);
        Assert.Equal(fixedNow, new TestClock(fixedNow).UtcNow);
    }

    [Fact]
    public void TestClock_AdvanceMovesTimeForward()
    {
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock(start);
        clock.Advance(TimeSpan.FromHours(48));
        Assert.Equal(start.AddHours(48), clock.UtcNow);
    }

    [Fact]
    public void TestClock_SetOverridesTime()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var target = new DateTime(2030, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        clock.Set(target);
        Assert.Equal(target, clock.UtcNow);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~SystemClockTests"` → compile error: `SystemClock` / `TestClock` / `ISystemClock` do not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/ISystemClock.cs`:
```csharp
using System;

namespace RevitCortex.Core.Licensing;

/// <summary>Abstraction over wall-clock time: deterministic tests + anti-rollback.</summary>
public interface ISystemClock
{
    DateTime UtcNow { get; }
}

/// <summary>Real clock. Always returns a UTC-kind timestamp.</summary>
public class SystemClock : ISystemClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

/// <summary>Mutable clock for tests: fixed, advanceable, settable.</summary>
public class TestClock : ISystemClock
{
    private DateTime _now;

    public TestClock(DateTime now)
    {
        _now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }

    public DateTime UtcNow => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);

    public void Set(DateTime now) => _now = DateTime.SpecifyKind(now, DateTimeKind.Utc);
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **5 passed**, 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/ISystemClock.cs src/RevitCortex.Tests/Licensing/SystemClockTests.cs
git commit -m "feat(licensing): add ISystemClock with SystemClock + TestClock" -m "Deterministic time seam for the state-machine and anti-rollback tests." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: IFingerprintProvider + FakeFingerprintProvider

**Files:**
- Create: `src/RevitCortex.Core/Licensing/IFingerprintProvider.cs`
- Test: `src/RevitCortex.Tests/Licensing/FakeFingerprintProviderTests.cs`

Canonical contract: `IReadOnlyList<string> GetHashedAttributes()` (a flat list of SHA-256 hex hashes, one per collected attribute). The real Windows collector lands in the Plugin (Task 7).

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/FakeFingerprintProviderTests.cs`:
```csharp
using System.Collections.Generic;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FakeFingerprintProviderTests
{
    [Fact]
    public void ReturnsConfiguredHashes()
    {
        var provider = new FakeFingerprintProvider(new[] { "h1", "h2" });
        Assert.Equal(new[] { "h1", "h2" }, provider.GetHashedAttributes());
    }

    [Fact]
    public void EmptyByDefault_NeverNull()
    {
        var hashes = new FakeFingerprintProvider().GetHashedAttributes();
        Assert.NotNull(hashes);
        Assert.Empty(hashes);
    }

    [Fact]
    public void ReturnsReadOnlyListContract()
    {
        IFingerprintProvider provider = new FakeFingerprintProvider(new List<string> { "x" });
        IReadOnlyList<string> hashes = provider.GetHashedAttributes();
        Assert.Single(hashes);
        Assert.Equal("x", hashes[0]);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~FakeFingerprintProviderTests"` → compile error: `IFingerprintProvider` / `FakeFingerprintProvider` do not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/IFingerprintProvider.cs`:
```csharp
using System.Collections.Generic;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Returns the machine fingerprint as a flat list of independently SHA-256-hashed
/// attributes (Fase 1: just MachineGuid). The real Windows collector lives in the
/// Plugin (registry). A missing/unavailable attribute is simply omitted, never a
/// placeholder. Core only depends on this contract.
/// </summary>
public interface IFingerprintProvider
{
    IReadOnlyList<string> GetHashedAttributes();
}

/// <summary>Test/dev provider: returns a fixed set of hashes.</summary>
public class FakeFingerprintProvider : IFingerprintProvider
{
    private readonly List<string> _hashes;

    public FakeFingerprintProvider()
    {
        _hashes = new List<string>();
    }

    public FakeFingerprintProvider(IEnumerable<string> hashes)
    {
        _hashes = new List<string>(hashes);
    }

    public IReadOnlyList<string> GetHashedAttributes() => _hashes;
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **3 passed**, 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/IFingerprintProvider.cs src/RevitCortex.Tests/Licensing/FakeFingerprintProviderTests.cs
git commit -m "feat(licensing): add IFingerprintProvider + FakeFingerprintProvider" -m "Core contract for a flat list of SHA-256 hashed attributes; missing attribute is omitted. Windows collector lands in the Plugin." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: ILicenseStore + StoredLicenseState + InMemoryLicenseStore

**Files:**
- Create: `src/RevitCortex.Core/Licensing/ILicenseStore.cs`
- Test: `src/RevitCortex.Tests/Licensing/InMemoryLicenseStoreTests.cs`

Canonical contract: `StoredLicenseState(string token, DateTime? lastOnlineCheckUtc, DateTime highWaterMarkUtc)`; `ILicenseStore.Save(StoredLicenseState)` (NOT `Save(LicenseToken)`). `Token` is the raw wire string `payload.sig`.

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/InMemoryLicenseStoreTests.cs`:
```csharp
using System;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class InMemoryLicenseStoreTests
{
    [Fact]
    public void Load_ReturnsNull_WhenNothingSaved()
    {
        Assert.Null(new InMemoryLicenseStore().Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new InMemoryLicenseStore();
        var check = new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc);
        var hwm = new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc);

        store.Save(new StoredLicenseState("base64-token", check, hwm));

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("base64-token", loaded!.Token);
        Assert.Equal(check, loaded.LastOnlineCheckUtc);
        Assert.Equal(hwm, loaded.HighWaterMarkUtc);
    }

    [Fact]
    public void Save_OverwritesPreviousState()
    {
        var store = new InMemoryLicenseStore();
        store.Save(new StoredLicenseState("t1", DateTime.UtcNow, DateTime.UtcNow));
        store.Save(new StoredLicenseState("t2", DateTime.UtcNow, DateTime.UtcNow));

        Assert.Equal("t2", store.Load()!.Token);
    }

    [Fact]
    public void StoredLicenseState_AllowsNullLastOnlineCheck()
    {
        var hwm = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var s = new StoredLicenseState("tok", null, hwm);

        Assert.Equal("tok", s.Token);
        Assert.Null(s.LastOnlineCheckUtc);
        Assert.Equal(hwm, s.HighWaterMarkUtc);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~InMemoryLicenseStoreTests"` → compile error: `StoredLicenseState` / `ILicenseStore` / `InMemoryLicenseStore` do not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/ILicenseStore.cs`:
```csharp
using System;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// The persisted client-side state: the raw signed wire token (payload.sig) plus two
/// local grace-metadata timestamps. Immutable class (net48-safe, no record). Only the
/// token is trusted (via its signature); the timestamps are local hints that can only
/// SHORTEN grace (with anti-rollback), never extend it.
/// </summary>
public class StoredLicenseState
{
    public string Token { get; }
    public DateTime? LastOnlineCheckUtc { get; }
    public DateTime HighWaterMarkUtc { get; }

    public StoredLicenseState(string token, DateTime? lastOnlineCheckUtc, DateTime highWaterMarkUtc)
    {
        Token = token ?? "";
        LastOnlineCheckUtc = lastOnlineCheckUtc;
        HighWaterMarkUtc = highWaterMarkUtc;
    }
}

/// <summary>Persistence abstraction for the stored license state.</summary>
public interface ILicenseStore
{
    /// <summary>Returns the stored state, or null if none / unreadable (never throws).</summary>
    StoredLicenseState? Load();

    /// <summary>Persists the state, overwriting any previous one.</summary>
    void Save(StoredLicenseState state);
}

/// <summary>In-memory store for tests and dev.</summary>
public class InMemoryLicenseStore : ILicenseStore
{
    private StoredLicenseState? _state;

    public StoredLicenseState? Load() => _state;

    public void Save(StoredLicenseState state) => _state = state;
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **4 passed**, 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/ILicenseStore.cs src/RevitCortex.Tests/Licensing/InMemoryLicenseStoreTests.cs
git commit -m "feat(licensing): add ILicenseStore + StoredLicenseState + in-memory store" -m "Immutable stored-state DTO (wire token + local grace timestamps); Save takes StoredLicenseState; Load never throws. FileLicenseStore lands as a Plugin task." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: LicenseTokenVerifier (RSA-2048 verify over verbatim segment-1 bytes, net48-safe import)

**Files:**
- Create: `src/RevitCortex.Core/Licensing/LicenseTokenVerifier.cs`
- Test: `src/RevitCortex.Tests/Licensing/LicenseTokenVerifierTests.cs`

**Wire format (pinned; reused by FakeLicenseBackend in Task 8):** `base64(payloadJsonUtf8) + "." + base64(signature)`. The signature is over the **raw UTF-8 payload bytes** (segment-1 pre-base64, VERBATIM — fix #4: the verifier decodes segment-1 base64 and verifies over exactly those bytes, never re-serializing; so no JObject key-order agreement is needed). SHA-256 + `RSASignaturePadding.Pkcs1`.

**net48-safe key import (fix contract + fix #10):** ctor takes raw `byte[] modulus, byte[] exponent`, **clones** them, and imports via `RSA.Create()` + `ImportParameters(RSAParameters{Modulus,Exponent})` + `VerifyData(...)`. NEVER `ImportSubjectPublicKeyInfo`/`ImportRSAPublicKey` (absent on net48/netstandard2.0).

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/LicenseTokenVerifierTests.cs`:
```csharp
using System;
using System.Security.Cryptography;
using System.Text;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseTokenVerifierTests : IDisposable
{
    private readonly RSA _signingKey;
    private readonly byte[] _pubModulus;
    private readonly byte[] _pubExponent;

    public LicenseTokenVerifierTests()
    {
        _signingKey = RSA.Create(2048);
        var pub = _signingKey.ExportParameters(false);
        _pubModulus = pub.Modulus!;
        _pubExponent = pub.Exponent!;
    }

    public void Dispose() => _signingKey.Dispose();

    private const string PayloadJson = @"{
        ""licenseId"": ""lic-verify"",
        ""state"": ""active"",
        ""expiresAtUtc"": ""2027-01-01T00:00:00Z"",
        ""seatLimit"": 2,
        ""fingerprintHashes"": [""fa"", ""fb""],
        ""issuedAtUtc"": ""2026-01-01T00:00:00Z""
    }";

    // base64(payload) + "." + base64(signature); signature over the RAW UTF-8 payload bytes.
    private static string MakeToken(RSA signingKey, string payloadJson)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var sig = signingKey.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(payloadBytes) + "." + Convert.ToBase64String(sig);
    }

    [Fact]
    public void Verify_ValidToken_ReturnsParsedLicense()
    {
        var verifier = new LicenseTokenVerifier(_pubModulus, _pubExponent);
        var result = verifier.Verify(MakeToken(_signingKey, PayloadJson));

        Assert.NotNull(result);
        Assert.Equal("lic-verify", result!.LicenseId);
        Assert.Equal("active", result.State);
        Assert.Equal(2, result.SeatLimit);
        Assert.Equal(new[] { "fa", "fb" }, result.FingerprintHashes);
    }

    [Fact]
    public void Verify_TamperedPayload_ReturnsNull()
    {
        var verifier = new LicenseTokenVerifier(_pubModulus, _pubExponent);
        var token = MakeToken(_signingKey, PayloadJson);
        var parts = token.Split('.');
        var tamperedPayload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(PayloadJson.Replace("lic-verify", "lic-HACKED")));
        Assert.Null(verifier.Verify(tamperedPayload + "." + parts[1]));
    }

    [Fact]
    public void Verify_WrongKey_ReturnsNull()
    {
        using var otherKey = RSA.Create(2048);
        var otherPub = otherKey.ExportParameters(false);
        var verifier = new LicenseTokenVerifier(otherPub.Modulus!, otherPub.Exponent!);
        Assert.Null(verifier.Verify(MakeToken(_signingKey, PayloadJson)));
    }

    [Fact]
    public void Verify_TruncatedSignature_ReturnsNull()
    {
        var verifier = new LicenseTokenVerifier(_pubModulus, _pubExponent);
        var token = MakeToken(_signingKey, PayloadJson);
        Assert.Null(verifier.Verify(token.Substring(0, token.Length - 10)));
    }

    [Fact]
    public void Verify_MissingDotSeparator_ReturnsNull()
    {
        var verifier = new LicenseTokenVerifier(_pubModulus, _pubExponent);
        Assert.Null(verifier.Verify("no-dot-here"));
    }

    [Fact]
    public void Verify_NotBase64_ReturnsNull()
    {
        var verifier = new LicenseTokenVerifier(_pubModulus, _pubExponent);
        Assert.Null(verifier.Verify("!!!not-base64!!!.###also-not###"));
    }

    [Fact]
    public void Verify_NullOrEmpty_ReturnsNull()
    {
        var verifier = new LicenseTokenVerifier(_pubModulus, _pubExponent);
        Assert.Null(verifier.Verify(null!));
        Assert.Null(verifier.Verify(""));
    }

    [Fact]
    public void Verify_ValidSignatureButGarbagePayloadJson_ReturnsNull()
    {
        var verifier = new LicenseTokenVerifier(_pubModulus, _pubExponent);
        Assert.Null(verifier.Verify(MakeToken(_signingKey, "this is signed but not json {{{")));
    }

    // fix #10: ctor clones the arrays -> mutating the caller's buffers afterwards must not
    // change verification behavior.
    [Fact]
    public void Ctor_ClonesKeyArrays_CallerMutationDoesNotAffectVerify()
    {
        var mod = (byte[])_pubModulus.Clone();
        var exp = (byte[])_pubExponent.Clone();
        var verifier = new LicenseTokenVerifier(mod, exp);
        for (int i = 0; i < mod.Length; i++) mod[i] ^= 0xFF; // corrupt caller's copy after ctor
        Assert.NotNull(verifier.Verify(MakeToken(_signingKey, PayloadJson)));
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~LicenseTokenVerifierTests"` → compile error: `LicenseTokenVerifier` does not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/LicenseTokenVerifier.cs`:
```csharp
using System;
using System.Security.Cryptography;
using System.Text;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Verifies a signed license token and parses it into a <see cref="LicenseToken"/>.
///
/// Wire format: base64(payloadUtf8) + "." + base64(signature).
/// Signature: RSA-2048, SHA-256, PKCS#1 v1.5, over the RAW UTF-8 payload bytes
/// (segment 1 decoded from base64 VERBATIM — never re-serialized).
///
/// Cross-target: the public key is supplied as raw Modulus + Exponent and imported via
/// RSAParameters + ImportParameters. This avoids ImportSubjectPublicKeyInfo /
/// ImportRSAPublicKey, which DO NOT exist on net48 / netstandard2.0. Only RSA.Create,
/// ImportParameters and VerifyData are used — present on every target (R23-R27).
///
/// Any malformed/tampered/truncated/wrong-key/non-parseable input returns null; never throws.
/// </summary>
public class LicenseTokenVerifier
{
    private readonly RSAParameters _publicKey;

    public LicenseTokenVerifier(byte[] modulus, byte[] exponent)
    {
        // fix #10: clone the caller's buffers so later mutation can't affect verification.
        _publicKey = new RSAParameters
        {
            Modulus = (byte[])modulus.Clone(),
            Exponent = (byte[])exponent.Clone()
        };
    }

    public LicenseToken? Verify(string wireToken)
    {
        if (string.IsNullOrEmpty(wireToken)) return null;

        var dot = wireToken.IndexOf('.');
        if (dot <= 0 || dot >= wireToken.Length - 1) return null;

        var payloadB64 = wireToken.Substring(0, dot);
        var sigB64 = wireToken.Substring(dot + 1);

        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(payloadB64);
            sigBytes = Convert.FromBase64String(sigB64);
        }
        catch
        {
            return null; // not valid base64
        }

        bool valid;
        try
        {
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(_publicKey);
                valid = rsa.VerifyData(
                    payloadBytes, sigBytes,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }
        catch
        {
            return null; // any crypto failure => invalid, never throw
        }

        if (!valid) return null;

        var json = Encoding.UTF8.GetString(payloadBytes);
        return LicenseToken.FromJson(json); // null if payload isn't valid token JSON
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **9 passed**, 0 failed. Gate check: confirm the verifier verifies over the **transmitted segment-1 bytes**, not a re-serialization (the tampered/garbage-JSON tests prove this).
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors (R24/net48 is the gate that would reject an `ImportSubjectPublicKeyInfo` slip).
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/LicenseTokenVerifier.cs src/RevitCortex.Tests/Licensing/LicenseTokenVerifierTests.cs
git commit -m "feat(licensing): add LicenseTokenVerifier (RSA-2048 verify over verbatim payload bytes)" -m "Verifies base64(payload).base64(sig) with SHA-256/PKCS#1 over the decoded segment-1 bytes (no re-serialize). Public key imported as raw Modulus+Exponent (net48-safe; no ImportSubjectPublicKeyInfo), arrays cloned in ctor. Bad input -> null." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: ILicenseBackend + LicenseActivationResult

**Files:**
- Create: `src/RevitCortex.Core/Licensing/LicenseActivationResult.cs`
- Create: `src/RevitCortex.Core/Licensing/ILicenseBackend.cs`
- Test: `src/RevitCortex.Tests/Licensing/LicenseActivationResultTests.cs`

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/LicenseActivationResultTests.cs`:
```csharp
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseActivationResultTests
{
    [Fact]
    public void Ok_CarriesTokenAndSuccess_NoError()
    {
        var r = LicenseActivationResult.Ok("signed-token-abc");
        Assert.True(r.Success);
        Assert.Equal("signed-token-abc", r.Token);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Fail_CarriesError_NoToken()
    {
        var r = LicenseActivationResult.Fail("invalid license key");
        Assert.False(r.Success);
        Assert.Null(r.Token);
        Assert.Equal("invalid license key", r.Error);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~LicenseActivationResultTests"` → compile error: `LicenseActivationResult` / `ILicenseBackend` do not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/LicenseActivationResult.cs`:
```csharp
namespace RevitCortex.Core.Licensing;

/// <summary>
/// Outcome of an <see cref="ILicenseBackend.Activate"/> / Validate call. Success carries
/// a signed wire token; failure carries a human-readable error. Class (not record) for net48.
/// </summary>
public class LicenseActivationResult
{
    public bool Success { get; }
    public string? Token { get; }
    public string? Error { get; }

    private LicenseActivationResult(bool success, string? token, string? error)
    {
        Success = success;
        Token = token;
        Error = error;
    }

    public static LicenseActivationResult Ok(string token) =>
        new LicenseActivationResult(true, token, null);

    public static LicenseActivationResult Fail(string error) =>
        new LicenseActivationResult(false, null, error);
}
```
Create `src/RevitCortex.Core/Licensing/ILicenseBackend.cs`:
```csharp
using System.Collections.Generic;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Abstraction over the licensing authority (Fase 2: Keygen/Stripe; Fase 1:
/// <see cref="FakeLicenseBackend"/>). The client never trusts anything outside the RSA
/// signature carried inside the returned wire token.
/// </summary>
public interface ILicenseBackend
{
    /// <summary>
    /// Exchanges a license key + the current machine fingerprint hashes for a signed
    /// wire token (base64(payload).base64(sig)) verifiable by LicenseTokenVerifier.
    /// </summary>
    LicenseActivationResult Activate(string licenseKey, IReadOnlyList<string> fingerprintHashes);

    /// <summary>
    /// Re-checks an existing wire token (online-refresh path). Fase 1 echoes a parseable
    /// token; Fase 2 revalidates server-side.
    /// </summary>
    LicenseActivationResult Validate(string wireToken);
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **2 passed**, 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/ILicenseBackend.cs src/RevitCortex.Core/Licensing/LicenseActivationResult.cs src/RevitCortex.Tests/Licensing/LicenseActivationResultTests.cs
git commit -m "feat(licensing): add ILicenseBackend + LicenseActivationResult (Core, Fase 1)" -m "Activate(key, fingerprint) -> signed wire token; Validate(token) for refresh. Result is a class (net48-safe), no record." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: FakeLicenseBackend (runtime keypair, emits verifier-accepted tokens)

**Files:**
- Create: `src/RevitCortex.Core/Licensing/FakeLicenseBackend.cs`
- Test: `src/RevitCortex.Tests/Licensing/FakeLicenseBackendTests.cs`

**Blocking precondition:** Tasks 6 (`LicenseTokenVerifier`), 7 (`ILicenseBackend`, `LicenseActivationResult`), 2 (`LicenseToken`) must be merged. If not, Step 2's failure reads "unresolved: FakeLicenseBackend" plus any of `LicenseTokenVerifier`, `LicenseToken` not yet present.

The backend takes a runtime `RSA` (fix contract: `RSA.Create(2048)`), exposes the public half as `RSAParameters PublicKeyParameters` (NOT const), and signs the SAME bytes it puts in segment 1. **fix #4 + JSON-key alignment:** the payload uses the keys `LicenseToken.FromJson` reads — `licenseId`, `state`, `expiresAtUtc`, `seatLimit`, `fingerprintHashes`, `issuedAtUtc` — so verify→parse round-trips. Signature over the raw UTF-8 payload bytes, SHA-256/PKCS#1.

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/FakeLicenseBackendTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FakeLicenseBackendTests : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);

    public void Dispose() => _key.Dispose();

    private LicenseTokenVerifier VerifierForThisKey()
    {
        var pub = _key.ExportParameters(false);
        return new LicenseTokenVerifier(pub.Modulus!, pub.Exponent!);
    }

    [Fact]
    public void Activate_MintsToken_VerifierAcceptsIt_PayloadRoundTrips()
    {
        var expires = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var issued = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);
        var fps = new List<string> { "hashA", "hashB" };

        var backend = new FakeLicenseBackend(_key)
        {
            LicenseId = "lic-123",
            State = "active",
            ExpiresAtUtc = expires,
            IssuedAtUtc = issued,
            SeatLimit = 3,
            FingerprintHashes = fps,
        };

        var result = backend.Activate("KEY-XYZ", fps);
        Assert.True(result.Success);
        Assert.NotNull(result.Token);

        var token = VerifierForThisKey().Verify(result.Token!);
        Assert.NotNull(token);
        Assert.Equal("lic-123", token!.LicenseId);
        Assert.Equal("active", token.State);
        Assert.Equal(expires, token.ExpiresAtUtc);
        Assert.Equal(issued, token.IssuedAtUtc);
        Assert.Equal(3, token.SeatLimit);
        Assert.Equal(new[] { "hashA", "hashB" }, token.FingerprintHashes);
    }

    [Fact]
    public void Activate_TrialState_ProducesTrialToken()
    {
        var backend = new FakeLicenseBackend(_key)
        {
            LicenseId = "trial-1",
            State = "trial",
            ExpiresAtUtc = new DateTime(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc),
            IssuedAtUtc = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
            SeatLimit = 1,
            FingerprintHashes = new List<string> { "fp1" },
        };

        var token = VerifierForThisKey().Verify(backend.Activate("T", new List<string> { "fp1" }).Token!);
        Assert.Equal("trial", token!.State);
    }

    [Fact]
    public void Activate_UsesFingerprintArgument_WhenNotPreset()
    {
        var backend = new FakeLicenseBackend(_key)
        {
            LicenseId = "lic-fp",
            State = "active",
            ExpiresAtUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IssuedAtUtc = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
            SeatLimit = 5,
            FingerprintHashes = null,
        };

        var token = VerifierForThisKey().Verify(
            backend.Activate("K", new List<string> { "argHash1", "argHash2" }).Token!);
        Assert.Equal(new[] { "argHash1", "argHash2" }, token!.FingerprintHashes);
    }

    [Fact]
    public void Validate_ReturnsSameToken_WhenParseable()
    {
        var backend = new FakeLicenseBackend(_key)
        {
            LicenseId = "lic-v",
            State = "active",
            ExpiresAtUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            IssuedAtUtc = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc),
            SeatLimit = 2,
            FingerprintHashes = new List<string> { "fp" },
        };
        var minted = backend.Activate("K", new List<string> { "fp" }).Token!;

        var revalidated = backend.Validate(minted);
        Assert.True(revalidated.Success);
        Assert.Equal(minted, revalidated.Token);
    }

    [Fact]
    public void PublicKeyParameters_ExposesPublicHalf_NotConst()
    {
        var backend = new FakeLicenseBackend(_key);
        var p = backend.PublicKeyParameters;
        Assert.NotNull(p.Modulus);
        Assert.NotNull(p.Exponent);
        // A verifier built from these parameters accepts a token this backend mints.
        var verifier = new LicenseTokenVerifier(p.Modulus!, p.Exponent!);
        Assert.NotNull(verifier.Verify(backend.Activate("K", new List<string> { "fp" }).Token!));
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~FakeLicenseBackendTests"` → compile error, "unresolved: FakeLicenseBackend" (and, if the precondition tasks were not merged, `LicenseTokenVerifier` / `LicenseToken`).
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/FakeLicenseBackend.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// In-memory <see cref="ILicenseBackend"/> for tests AND dev. Given a runtime RSA
/// private key (RSA.Create(2048)), mints tokens in the exact wire format
/// LicenseTokenVerifier parses: base64(payloadJsonUtf8) + "." + base64(pkcs1-sha256
/// signature over the SAME JSON bytes). The payload keys match LicenseToken.FromJson
/// (licenseId/state/expiresAtUtc/seatLimit/fingerprintHashes/issuedAtUtc) so verify->parse
/// round-trips. Public half exposed as RSAParameters (never const) for the verifier.
/// </summary>
public class FakeLicenseBackend : ILicenseBackend
{
    private readonly RSA _privateKey;

    public FakeLicenseBackend(RSA privateKey)
    {
        _privateKey = privateKey;
    }

    /// <summary>Public half for building a verifier against this backend's key.</summary>
    public RSAParameters PublicKeyParameters => _privateKey.ExportParameters(false);

    public string LicenseId { get; set; } = "fake-license";
    public string State { get; set; } = "active";
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddYears(1);
    public DateTime IssuedAtUtc { get; set; } = DateTime.UtcNow;
    public int SeatLimit { get; set; } = 1;

    /// <summary>When null, Activate embeds the fingerprint hashes passed as argument.</summary>
    public IReadOnlyList<string>? FingerprintHashes { get; set; }

    public LicenseActivationResult Activate(string licenseKey, IReadOnlyList<string> fingerprintHashes)
    {
        var fps = FingerprintHashes ?? fingerprintHashes ?? new List<string>();
        return LicenseActivationResult.Ok(Mint(fps));
    }

    public LicenseActivationResult Validate(string wireToken)
    {
        if (string.IsNullOrEmpty(wireToken) || wireToken.IndexOf('.') < 0)
            return LicenseActivationResult.Fail("malformed token");
        return LicenseActivationResult.Ok(wireToken);
    }

    private string Mint(IReadOnlyList<string> fingerprintHashes)
    {
        var fpArray = new JArray();
        foreach (var h in fingerprintHashes) fpArray.Add(h);

        var payload = new JObject
        {
            ["licenseId"] = LicenseId,
            ["state"] = State,
            ["expiresAtUtc"] = ExpiresAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["seatLimit"] = SeatLimit,
            ["fingerprintHashes"] = fpArray,
            ["issuedAtUtc"] = IssuedAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
        var sig = _privateKey.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(payloadBytes) + "." + Convert.ToBase64String(sig);
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **5 passed**, 0 failed. (If the verifier hashed the base64 text instead of the payload bytes this would fail — that is the guard that pins the shared wire contract with Task 6.)
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/FakeLicenseBackend.cs src/RevitCortex.Tests/Licensing/FakeLicenseBackendTests.cs
git commit -m "feat(licensing): add FakeLicenseBackend that mints verifier-accepted tokens" -m "RSA-2048/SHA-256/PKCS#1 tokens in the wire format LicenseTokenVerifier parses (signature over the payload bytes; keys match LicenseToken.FromJson). Runtime keypair; public half exposed as RSAParameters (never const)." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: LicenseManager (8-point state machine + stateful surface)

**Files:**
- Create: `src/RevitCortex.Core/Licensing/LicenseManager.cs`
- Test: `src/RevitCortex.Tests/Licensing/LicenseManagerTests.cs`

**Blocking precondition (not parenthetical):** Tasks 2 (`LicenseToken`), 1 (`LicenseState`), 3 (`ISystemClock`), 4 (`IFingerprintProvider`), 5 (`ILicenseStore`/`StoredLicenseState`), 6 (`LicenseTokenVerifier`), 7 (`ILicenseBackend`/`LicenseActivationResult`), 8 (`FakeLicenseBackend`) must all be merged. If any is missing, Step 2's failure will read "unresolved:" that type.

**Pinned surface (fix contract, no adaptation):**
- Pure: `LicenseState Evaluate(LicenseToken? token, DateTime nowUtc, DateTime? lastOnlineCheckUtc, IReadOnlyList<string> currentFingerprint, DateTime highWaterMarkUtc)`.
- Stateful: `void Refresh()` (loads store, verifies token, evaluates with clock + fingerprint + hwm, caches State + display fields); `LicenseState State { get; }` (default Invalid); `DateTime? ExpiresAtUtc { get; }`; `int GraceDaysRemaining { get; }`; `string LicenseIdTruncated { get; }`; `LicenseActivationResult Activate(string licenseKey)` (backend → `store.Save(StoredLicenseState)` → Refresh). No public `Store`.
- Ctor: `LicenseManager(ILicenseStore store, IFingerprintProvider fingerprint, LicenseTokenVerifier verifier, ISystemClock clock, ILicenseBackend backend)`.

**Pinned semantics:**
- fix #2: grace anchored on `lastOnlineCheckUtc`; `lastOnlineCheckUtc == null` ⇒ Expired.
- fix #3: `IsTrustedState(token.State)` (active|trial) re-checked BEFORE granting Grace on an expired token; an unknown state ⇒ Invalid even when expired (no asymmetry with the unexpired branch).
- Point 8: rollback (`nowUtc < highWaterMarkUtc − RollbackTolerance`) forces Expired even within the 10-day grace.
- Constants: `GraceWindow = 10 days`, `RollbackTolerance = 1 hour`.

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/LicenseManagerTests.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseManagerTests : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly LicenseTokenVerifier _verifier;
    private readonly FakeLicenseBackend _backend;

    private static readonly DateTime Issued = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Expiry = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    private static readonly List<string> MachineFp = new List<string> { "fpA", "fpB" };

    public LicenseManagerTests()
    {
        var pub = _key.ExportParameters(false);
        _verifier = new LicenseTokenVerifier(pub.Modulus!, pub.Exponent!);
        _backend = new FakeLicenseBackend(_key)
        {
            LicenseId = "lic",
            IssuedAtUtc = Issued,
            ExpiresAtUtc = Expiry,
            SeatLimit = 1,
        };
    }

    public void Dispose() => _key.Dispose();

    // Mint via the fake backend, verify back into a LicenseToken so Evaluate sees the
    // same object graph as production.
    private LicenseToken Token(string state, IReadOnlyList<string>? tokenFingerprints = null)
    {
        _backend.State = state;
        _backend.FingerprintHashes = tokenFingerprints ?? new List<string> { "fpA", "fpB" };
        return _verifier.Verify(_backend.Activate("K", MachineFp).Token!)!;
    }

    private static readonly LicenseManager Mgr = new LicenseManager(
        new InMemoryLicenseStore(), new FakeFingerprintProvider(), null!, new SystemClock(), null!);

    [Fact]
    public void Active_WithinExpiry_ReturnsActive()
    {
        var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(LicenseState.Active,
            Mgr.Evaluate(Token("active"), now, now, MachineFp, now));
    }

    [Fact]
    public void Trial_WithinExpiry_ReturnsTrial()
    {
        var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(LicenseState.Trial,
            Mgr.Evaluate(Token("trial"), now, now, MachineFp, now));
    }

    // fix #2: distinct lastCheck (expiry - 3d) so the anchor is genuinely pinned.
    [Fact]
    public void Expired_WithinGrace_ReturnsGrace()
    {
        var lastCheck = Expiry.AddDays(-3);
        var now = Expiry.AddDays(5);
        Assert.Equal(LicenseState.Grace,
            Mgr.Evaluate(Token("active"), now, lastCheck, MachineFp, now));
    }

    [Fact]
    public void Expired_BeyondGrace_ReturnsExpired()
    {
        var lastCheck = Expiry.AddDays(-3);
        var now = lastCheck.AddDays(11);
        Assert.Equal(LicenseState.Expired,
            Mgr.Evaluate(Token("active"), now, lastCheck, MachineFp, now));
    }

    [Fact]
    public void Expired_AtExactGraceBoundary_ReturnsGrace()
    {
        var lastCheck = Expiry.AddDays(-3);
        var now = lastCheck.AddDays(10);
        Assert.Equal(LicenseState.Grace,
            Mgr.Evaluate(Token("active"), now, lastCheck, MachineFp, now));
    }

    // fix #2: null last-check anchor -> Expired.
    [Fact]
    public void Expired_NullLastOnlineCheck_ReturnsExpired()
    {
        var now = Expiry.AddDays(1);
        Assert.Equal(LicenseState.Expired,
            Mgr.Evaluate(Token("active"), now, null, MachineFp, now));
    }

    // fix #3: unknown state on an EXPIRED token -> Invalid (no grace for untrusted state).
    [Fact]
    public void UnknownState_Expired_ReturnsInvalid_NotGrace()
    {
        var lastCheck = Expiry.AddDays(-3);
        var now = Expiry.AddDays(2);
        Assert.Equal(LicenseState.Invalid,
            Mgr.Evaluate(Token("wibble"), now, lastCheck, MachineFp, now));
    }

    [Fact]
    public void UnknownState_Unexpired_ReturnsInvalid()
    {
        var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(LicenseState.Invalid,
            Mgr.Evaluate(Token("wibble"), now, now, MachineFp, now));
    }

    [Fact]
    public void TamperedToken_VerifierNull_ReturnsInvalid()
    {
        var wire = _backend.Activate("K", MachineFp).Token!;
        var tampered = wire.Substring(0, wire.Length - 4) + "AAAA";
        LicenseToken? verified = _verifier.Verify(tampered);
        Assert.Null(verified);

        var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(LicenseState.Invalid, Mgr.Evaluate(verified, now, now, MachineFp, now));
    }

    // Containment: token.FingerprintHashes must be a subset of current. "DIFFERENT" is
    // in the token but not on the machine -> Invalid.
    [Fact]
    public void FingerprintNotSubset_ReturnsInvalid()
    {
        var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        var token = Token("active", tokenFingerprints: new List<string> { "fpA", "DIFFERENT" });
        Assert.Equal(LicenseState.Invalid, Mgr.Evaluate(token, now, now, MachineFp, now));
    }

    [Fact]
    public void ClockRollback_BeyondTolerance_ForcesExpired()
    {
        var hwm = Expiry.AddDays(3);
        var now = Expiry.AddDays(1);
        var lastCheck = Expiry.AddDays(-3);
        Assert.Equal(LicenseState.Expired,
            Mgr.Evaluate(Token("active"), now, lastCheck, MachineFp, hwm));
    }

    [Fact]
    public void ClockRollback_WithinTolerance_DoesNotForceExpired()
    {
        var hwm = Expiry.AddDays(2);
        var now = hwm.AddMinutes(-30);
        var lastCheck = Expiry.AddDays(-3);
        Assert.Equal(LicenseState.Grace,
            Mgr.Evaluate(Token("active"), now, lastCheck, MachineFp, hwm));
    }

    [Fact]
    public void NoToken_ReturnsInvalid()
    {
        var now = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(LicenseState.Invalid, Mgr.Evaluate(null, now, now, MachineFp, now));
    }

    // Stateful surface: Refresh() loads the store + evaluates; State/display update.
    [Fact]
    public void Refresh_WithStoredActiveToken_SetsStateActive_AndDisplayFields()
    {
        var store = new InMemoryLicenseStore();
        var fp = new FakeFingerprintProvider(MachineFp);
        var clock = new TestClock(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        var manager = new LicenseManager(store, fp, _verifier, clock, _backend);

        _backend.State = "active";
        _backend.LicenseId = "lic-display-1234567890";
        _backend.FingerprintHashes = MachineFp;
        var wire = _backend.Activate("K", MachineFp).Token!;
        store.Save(new StoredLicenseState(wire, clock.UtcNow, clock.UtcNow));

        manager.Refresh();

        Assert.Equal(LicenseState.Active, manager.State);
        Assert.Equal(Expiry, manager.ExpiresAtUtc);
        Assert.StartsWith("lic-disp", manager.LicenseIdTruncated);
        Assert.True(manager.LicenseIdTruncated.Length <= 12);
    }

    [Fact]
    public void State_DefaultsToInvalid_BeforeRefresh()
    {
        var manager = new LicenseManager(
            new InMemoryLicenseStore(), new FakeFingerprintProvider(), _verifier, new SystemClock(), _backend);
        Assert.Equal(LicenseState.Invalid, manager.State);
    }

    // Activate() goes through the backend, persists via the store, and Refreshes.
    [Fact]
    public void Activate_PersistsTokenAndRefreshesToActive()
    {
        var store = new InMemoryLicenseStore();
        var fp = new FakeFingerprintProvider(MachineFp);
        var clock = new TestClock(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        _backend.State = "active";
        _backend.FingerprintHashes = MachineFp;
        var manager = new LicenseManager(store, fp, _verifier, clock, _backend);

        var result = manager.Activate("KEY-123");

        Assert.True(result.Success);
        Assert.NotNull(store.Load());
        Assert.Equal(LicenseState.Active, manager.State);
    }

    // GraceDaysRemaining is 0 when Active, and the remaining whole days when in Grace.
    [Fact]
    public void GraceDaysRemaining_ReportsRemainingWholeDays_WhenGrace()
    {
        var store = new InMemoryLicenseStore();
        var fp = new FakeFingerprintProvider(MachineFp);
        var lastCheck = Expiry.AddDays(-3);
        var clock = new TestClock(Expiry.AddDays(2)); // 2 days past expiry, lastCheck 3d before expiry
        _backend.State = "active";
        _backend.FingerprintHashes = MachineFp;
        var manager = new LicenseManager(store, fp, _verifier, clock, _backend);
        var wire = _backend.Activate("K", MachineFp).Token!;
        store.Save(new StoredLicenseState(wire, lastCheck, clock.UtcNow));

        manager.Refresh();

        Assert.Equal(LicenseState.Grace, manager.State);
        // 10-day window from lastCheck; now is (Expiry+2) = lastCheck+5 -> 5 days used, 5 left.
        Assert.Equal(5, manager.GraceDaysRemaining);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~LicenseManagerTests"` → compile error, "unresolved: LicenseManager" (plus any un-merged precondition type).
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Core/Licensing/LicenseManager.cs`:
```csharp
using System;
using System.Collections.Generic;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Deterministic license state machine (spec §4) plus a small stateful surface for the
/// gate and UI. Pure Evaluate() takes an already-verified token (null = signature failed
/// upstream OR nothing stored). Refresh() loads the store, verifies the wire token,
/// evaluates against clock+fingerprint+hwm, and caches State + display fields.
/// Fail-closed on validity (bad sig / fingerprint-not-subset / unknown state -> Invalid);
/// fail-open within grace (recent expiry -> Grace). Never touches I/O outside Refresh/Activate.
/// </summary>
public class LicenseManager
{
    public static readonly TimeSpan GraceWindow = TimeSpan.FromDays(10);
    public static readonly TimeSpan RollbackTolerance = TimeSpan.FromHours(1);

    private readonly ILicenseStore _store;
    private readonly IFingerprintProvider _fingerprint;
    private readonly LicenseTokenVerifier _verifier;
    private readonly ISystemClock _clock;
    private readonly ILicenseBackend _backend;

    private LicenseState _state = LicenseState.Invalid;
    private DateTime? _expiresAtUtc;
    private int _graceDaysRemaining;
    private string _licenseIdTruncated = "";

    public LicenseManager(
        ILicenseStore store,
        IFingerprintProvider fingerprint,
        LicenseTokenVerifier verifier,
        ISystemClock clock,
        ILicenseBackend backend)
    {
        _store = store;
        _fingerprint = fingerprint;
        _verifier = verifier;
        _clock = clock;
        _backend = backend;
    }

    public LicenseState State => _state;
    public DateTime? ExpiresAtUtc => _expiresAtUtc;
    public int GraceDaysRemaining => _graceDaysRemaining;
    public string LicenseIdTruncated => _licenseIdTruncated;

    /// <summary>Re-read the store + clock + fingerprint and recompute the cached state.</summary>
    public void Refresh()
    {
        LicenseToken? token = null;
        DateTime? lastCheck = null;
        var now = _clock.UtcNow;
        var hwm = now;

        var stored = SafeLoad();
        if (stored != null)
        {
            token = _verifier.Verify(stored.Token);
            lastCheck = stored.LastOnlineCheckUtc;
            hwm = stored.HighWaterMarkUtc > now ? stored.HighWaterMarkUtc : now;
        }

        var current = SafeFingerprint();
        _state = Evaluate(token, now, lastCheck, current, hwm);

        _expiresAtUtc = token?.ExpiresAtUtc;
        _licenseIdTruncated = Truncate(token?.LicenseId ?? "");
        _graceDaysRemaining = (_state == LicenseState.Grace && lastCheck.HasValue)
            ? Math.Max(0, (int)Math.Ceiling((GraceWindow - (now - lastCheck.Value)).TotalDays))
            : 0;
    }

    /// <summary>Activate via the backend, persist the wire token, and Refresh.</summary>
    public LicenseActivationResult Activate(string licenseKey)
    {
        var current = SafeFingerprint();
        var result = _backend.Activate(licenseKey ?? "", current);
        if (result.Success && result.Token != null)
        {
            var now = _clock.UtcNow;
            _store.Save(new StoredLicenseState(result.Token, now, now));
            Refresh();
        }
        return result;
    }

    public LicenseState Evaluate(
        LicenseToken? token,
        DateTime nowUtc,
        DateTime? lastOnlineCheckUtc,
        IReadOnlyList<string> currentFingerprint,
        DateTime highWaterMarkUtc)
    {
        // Point 1 (no token) + Point 2 (bad signature -> verifier returned null upstream).
        if (token == null)
            return LicenseState.Invalid;

        // Point 3: current fingerprint must be a SUPERSET of the token's hashes.
        if (!FingerprintIsSuperset(currentFingerprint, token.FingerprintHashes))
            return LicenseState.Invalid;

        // fix #3: an unknown state is never trusted, expired or not.
        if (!IsTrustedState(token.State))
            return LicenseState.Invalid;

        bool withinExpiry = nowUtc <= token.ExpiresAtUtc;
        if (withinExpiry)
        {
            return string.Equals(token.State, "trial", StringComparison.OrdinalIgnoreCase)
                ? LicenseState.Trial
                : LicenseState.Active;
        }

        // Expired. Point 8: rollback beyond tolerance revokes the offline lease.
        if (nowUtc < highWaterMarkUtc - RollbackTolerance)
            return LicenseState.Expired;

        // Point 6: grace anchored on the last online check (fix #2). Null anchor -> Expired.
        if (lastOnlineCheckUtc.HasValue &&
            (nowUtc - lastOnlineCheckUtc.Value) <= GraceWindow)
            return LicenseState.Grace;

        // Point 7.
        return LicenseState.Expired;
    }

    private static bool IsTrustedState(string state) =>
        string.Equals(state, "active", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "trial", StringComparison.OrdinalIgnoreCase);

    private static bool FingerprintIsSuperset(
        IReadOnlyList<string> current,
        IReadOnlyList<string> tokenHashes)
    {
        if (tokenHashes == null || tokenHashes.Count == 0)
            return false; // a real token always carries >= 1 hash
        var set = new HashSet<string>(current ?? new List<string>(), StringComparer.Ordinal);
        for (int i = 0; i < tokenHashes.Count; i++)
            if (!set.Contains(tokenHashes[i])) return false;
        return true;
    }

    private static string Truncate(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        return id.Length <= 12 ? id : id.Substring(0, 8) + "…";
    }

    private StoredLicenseState? SafeLoad()
    {
        try { return _store?.Load(); } catch { return null; }
    }

    private IReadOnlyList<string> SafeFingerprint()
    {
        try { return _fingerprint?.GetHashedAttributes() ?? new List<string>(); }
        catch { return new List<string>(); }
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **18 passed**, 0 failed. Gate: confirm the verifier verifies over the transmitted bytes, not a re-serialization (the tampered-token and round-trip cases exercise it).
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors — R24 (net48) confirms no `record`/`init`/`GetValueOrDefault`/`Index`/`Range` slipped in.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Core/Licensing/LicenseManager.cs src/RevitCortex.Tests/Licensing/LicenseManagerTests.cs
git commit -m "feat(licensing): add LicenseManager 8-point state machine + stateful surface (Core)" -m "Pure Evaluate() per spec §4 (grace anchored on lastOnlineCheck; unknown state -> Invalid even when expired; rollback forces Expired) plus Refresh/State/Activate/display accessors over store+verifier+clock+fingerprint+backend. Pure logic; I/O only in Refresh/Activate." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: WindowsFingerprintProvider (registry-only MachineGuid, per-attribute SHA-256)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/WindowsFingerprintProvider.cs`
- Test: `src/RevitCortex.Tests/Licensing/FingerprintHasherTests.cs`
- Test: `src/RevitCortex.Tests/Licensing/RequiresMachineGuidFactAttribute.cs`

**Blocking precondition:** Task 4 (`IFingerprintProvider`, flat-list `GetHashedAttributes()`) merged.

fix #6: **registry-only** — reads `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid` via `Microsoft.Win32.Registry` (already available on all Plugin TFMs; no package added). **No `System.Management`/WMI** — so this task does NOT touch the csproj (fix #12), the R25+R24 gate suffices. fix #15: HKLM **read** of MachineGuid is allowed (it lives only there); the "never HKLM" rule is for WRITES (Task 12). fix #7: `FingerprintHasher` is **public** so the test compiles (no InternalsVisibleTo needed; there is none in the Plugin today). The pure hasher returns a flat list of SHA-256 hex strings, matching the Core contract.

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/FingerprintHasherTests.cs`:
```csharp
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FingerprintHasherTests
{
    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    [Fact]
    public void Hash_HashesEachValueWithSha256Hex_InOrder()
    {
        var result = FingerprintHasher.Hash(new[] { "ABC-123", "SN-999" });
        Assert.Equal(2, result.Count);
        Assert.Equal(Sha256Hex("ABC-123"), result[0]);
        Assert.Equal(Sha256Hex("SN-999"), result[1]);
    }

    [Fact]
    public void Hash_OmitsNullEmptyAndWhitespaceValues()
    {
        var result = FingerprintHasher.Hash(new[] { "GUID", null, "", "   " });
        Assert.Single(result);
        Assert.Equal(Sha256Hex("GUID"), result[0]);
    }

    [Fact]
    public void Hash_EmptyInput_ReturnsEmptyList()
    {
        Assert.Empty(FingerprintHasher.Hash(new string?[0]));
    }

    [Fact]
    public void Hash_NullInput_ReturnsEmptyList_NeverThrows()
    {
        Assert.Empty(FingerprintHasher.Hash(null));
    }

    [Fact]
    public void Hash_ProducesLowercase64CharHex()
    {
        var result = FingerprintHasher.Hash(new[] { "anything" });
        Assert.Single(result);
        Assert.Equal(64, result[0].Length);
        Assert.Equal(result[0].ToLowerInvariant(), result[0]);
    }
}

public class WindowsFingerprintProviderContractTests
{
    [RequiresMachineGuidFact]
    public void GetHashedAttributes_IncludesMachineGuidHash_OnRealMachine()
    {
        var hashes = new WindowsFingerprintProvider().GetHashedAttributes();
        Assert.NotEmpty(hashes);
        Assert.Equal(64, hashes[0].Length); // SHA-256 hex
    }

    [Fact]
    public void GetHashedAttributes_NeverThrows()
    {
        var ex = Record.Exception(() => new WindowsFingerprintProvider().GetHashedAttributes());
        Assert.Null(ex);
    }
}
```
Create `src/RevitCortex.Tests/Licensing/RequiresMachineGuidFactAttribute.cs`:
```csharp
using Microsoft.Win32;
using Xunit;

namespace RevitCortex.Tests.Licensing;

/// <summary>
/// Skips when HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid cannot be read
/// (non-Windows CI, restricted registry). Mirrors RequiresRevitApiFact: an
/// environmental absence becomes an honest Skip, not a failure.
/// </summary>
public sealed class RequiresMachineGuidFactAttribute : FactAttribute
{
    public RequiresMachineGuidFactAttribute()
    {
        if (!IsReadable())
            Skip = "Requires HKLM MachineGuid (real Windows machine registry).";
    }

    private static bool IsReadable()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
            return key?.GetValue("MachineGuid") is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch
        {
            return false;
        }
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~Fingerprint"` (matches both `FingerprintHasherTests` and `WindowsFingerprintProviderContractTests`; fix #12) → compile error: `FingerprintHasher` / `WindowsFingerprintProvider` do not exist in `RevitCortex.Plugin.Licensing`.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Plugin/Licensing/WindowsFingerprintProvider.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Pure hashing/omission logic, testable without hardware. Each non-empty raw attribute
/// value is SHA-256-hashed (lowercase hex) and returned in input order; null/empty/
/// whitespace values are dropped. Never throws. PUBLIC so the unit test can reach it.
/// </summary>
public static class FingerprintHasher
{
    public static IReadOnlyList<string> Hash(IEnumerable<string?> rawValues)
    {
        var result = new List<string>();
        if (rawValues == null) return result;

        using var sha = SHA256.Create();
        foreach (var value in rawValues)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            result.Add(sb.ToString());
        }
        return result;
    }
}

/// <summary>
/// Windows hardware fingerprint (Fase 1: MachineGuid only). Reads MachineGuid from the
/// registry (read-only, benign — read by countless programs) and SHA-256-hashes it. The
/// HKLM READ is intentional and allowed (MachineGuid lives only there); the "never HKLM"
/// rule applies to WRITES (see AntiRollbackClock). No WMI, no MAC address (personal data),
/// no System.Management dependency. Missing/unreadable -> empty list, never throws. The
/// server applies the match threshold, so a single attribute is acceptable.
/// (Future extension, OUTSIDE these tasks: add BIOS/board serial via WMI behind a per-TFM
/// System.Management PackageReference + R27 gate + try/catch-omit.)
/// </summary>
public sealed class WindowsFingerprintProvider : IFingerprintProvider
{
    public IReadOnlyList<string> GetHashedAttributes()
    {
        return FingerprintHasher.Hash(new[] { TryReadMachineGuid() });
    }

    private static string? TryReadMachineGuid()
    {
        try
        {
            using var key = RegistryKey
                .OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: false);
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **7 passed** (5 hasher + `GetHashedAttributes_NeverThrows`) with the `RequiresMachineGuidFact` contract test **passed on a real dev machine or `1 skipped` on CI** → summary "7 passed" or "6 passed, 1 skipped", 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors (no csproj change since there is no WMI).
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Plugin/Licensing/WindowsFingerprintProvider.cs src/RevitCortex.Tests/Licensing/FingerprintHasherTests.cs src/RevitCortex.Tests/Licensing/RequiresMachineGuidFactAttribute.cs
git commit -m "feat(licensing): WindowsFingerprintProvider (registry-only MachineGuid, SHA-256)" -m "Reads HKLM MachineGuid (read-only, allowed) and hashes it; no WMI/System.Management in Fase 1. Public FingerprintHasher unit-tested; provider covered by a skippable MachineGuid contract test." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: FileLicenseStore (atomic license.json in RootFolder, null on missing/corrupt)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/FileLicenseStore.cs`
- Test: `src/RevitCortex.Tests/Licensing/FileLicenseStoreTests.cs`

**Blocking precondition:** Task 5 (`ILicenseStore` / `StoredLicenseState`) merged.

Persists the `license.json` envelope (spec §5): `token` (wire string), `lastOnlineCheckUtc`, `highWaterMarkUtc`. Path defaults to `Path.Combine(CortexEnvironment.Current.RootFolder, "license.json")` — NEVER `SettingsFilePath`. Ctor takes an optional path (tests inject a temp dir, like `TelemetryConfig.Load(path)`). Atomic write = temp + `File.Replace` (fallback delete+Move). `Load()` returns null on missing/corrupt, never throws. fix #13: tests cover round-trip, missing→null, corrupt→null, and the atomic-overwrite path (no leftover `.tmp`).

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/FileLicenseStoreTests.cs`:
```csharp
using System;
using System.IO;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FileLicenseStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public FileLicenseStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rc-lic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "license.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static StoredLicenseState Sample() => new StoredLicenseState(
        "BASE64PAYLOAD.BASE64SIG",
        new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc),
        new DateTime(2026, 7, 8, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var store = new FileLicenseStore(_path);
        var state = Sample();

        store.Save(state);
        var loaded = store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(state.Token, loaded!.Token);
        Assert.Equal(state.LastOnlineCheckUtc, loaded.LastOnlineCheckUtc);
        Assert.Equal(state.HighWaterMarkUtc, loaded.HighWaterMarkUtc);
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.False(File.Exists(_path));
        Assert.Null(new FileLicenseStore(_path).Load());
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull_NeverThrows()
    {
        File.WriteAllText(_path, "{ this is not valid json ]]]");
        var store = new FileLicenseStore(_path);

        StoredLicenseState? result = null;
        var ex = Record.Exception(() => result = store.Load());

        Assert.Null(ex);
        Assert.Null(result);
    }

    [Fact]
    public void Save_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_dir, "sub", "license.json");
        new FileLicenseStore(nested).Save(Sample());
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Save_Overwrites_ExistingFileAtomically_NoLeftoverTmp()
    {
        var store = new FileLicenseStore(_path);
        store.Save(Sample());
        store.Save(new StoredLicenseState("SECOND.SIG", null,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)));

        var loaded = store.Load();
        Assert.NotNull(loaded);
        Assert.Equal("SECOND.SIG", loaded!.Token);
        Assert.Null(loaded.LastOnlineCheckUtc);
        Assert.False(File.Exists(_path + ".tmp"));
    }

    [Fact]
    public void Load_NullLastOnlineCheck_RoundTripsAsNull()
    {
        var store = new FileLicenseStore(_path);
        store.Save(new StoredLicenseState("t", null,
            new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc)));
        Assert.Null(store.Load()!.LastOnlineCheckUtc);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~FileLicenseStoreTests"` → compile error: `FileLicenseStore` does not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Plugin/Licensing/FileLicenseStore.cs`:
```csharp
using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Persists the license.json envelope (spec §5) in the active profile's RootFolder —
/// NEVER settings.json (D3: settings.json is merge-written by telemetry; sharing it is
/// the v1.0.36 corruption class). Load returns null on any failure; Save swallows all I/O
/// errors. Writes are atomic (temp + File.Replace, fallback delete+Move) so a crash
/// mid-write never leaves a truncated file. I/O discipline mirrors TelemetryConfig.
/// </summary>
public sealed class FileLicenseStore : ILicenseStore
{
    private readonly string _path;

    public FileLicenseStore(string? path = null)
    {
        _path = path ?? Path.Combine(CortexEnvironment.Current.RootFolder, "license.json");
    }

    public StoredLicenseState? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var root = JObject.Parse(File.ReadAllText(_path));

            var token = (string?)root["token"] ?? "";
            var last = ReadUtcNullable(root["lastOnlineCheckUtc"]);
            var hwm = ReadUtcNullable(root["highWaterMarkUtc"]) ?? DateTime.MinValue;

            return new StoredLicenseState(token, last, hwm);
        }
        catch
        {
            return null; // missing/corrupt/unreadable must never crash the host
        }
    }

    public void Save(StoredLicenseState state)
    {
        if (state == null) return;
        try
        {
            var root = new JObject
            {
                ["token"] = state.Token,
                ["highWaterMarkUtc"] = state.HighWaterMarkUtc.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
            if (state.LastOnlineCheckUtc.HasValue)
                root["lastOnlineCheckUtc"] = state.LastOnlineCheckUtc.Value.ToUniversalTime()
                    .ToString("yyyy-MM-ddTHH:mm:ssZ");

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, root.ToString());

            if (File.Exists(_path))
            {
                try { File.Replace(tmp, _path, null); }
                catch { File.Delete(_path); File.Move(tmp, _path); }
            }
            else
            {
                File.Move(tmp, _path);
            }
        }
        catch
        {
            try { var tmp = _path + ".tmp"; if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static DateTime? ReadUtcNullable(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return null;
        try
        {
            if (token.Type == JTokenType.Date)
                return ((DateTime)token).ToUniversalTime();
            var s = (string?)token;
            if (string.IsNullOrEmpty(s)) return null;
            return DateTime.Parse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }
        catch { return null; }
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **6 passed**, 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Plugin/Licensing/FileLicenseStore.cs src/RevitCortex.Tests/Licensing/FileLicenseStoreTests.cs
git commit -m "feat(licensing): FileLicenseStore (atomic license.json in RootFolder, null on missing/corrupt)" -m "Separate license.json in CortexEnvironment.Current.RootFolder, never settings.json (D3). Atomic write via temp + File.Replace with delete+Move fallback; Load returns null on missing/corrupt and never throws." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: AntiRollbackClock (monotonic high-water mark over HKCU + ProgramData)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/AntiRollbackClock.cs`
- Test: `src/RevitCortex.Tests/Licensing/AntiRollbackClockTests.cs`

**Blocking precondition:** Task 3 (`ISystemClock`) merged.

`AntiRollbackClock : ISystemClock` also exposes `HighWaterMarkUtc`. fix #5: the two redundant stores are **HKCU** (`RegistryHighWaterMarkStore`) and **ProgramData** (`ProgramDataHighWaterMarkStore`) — NOT license.json (which is user-writable/untrusted). Both sit behind `IHighWaterMarkStore` so the max-of-sources + monotonic logic is unit-tested with fakes. On construction it takes the max of {UtcNow, HKCU, ProgramData} and writes the new max back to BOTH stores, but only if it advances. `UtcNow` comes from an injectable `Func<DateTime>`. Rollback DETECTION lives in `LicenseManager` (Task 9); this class only guarantees `HighWaterMarkUtc` is monotonic. fix #14: `WriteCount == 0` when a store is already ahead. Writes go to HKCU + ProgramData only — MAI HKLM (write).

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/AntiRollbackClockTests.cs`:
```csharp
using System;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class AntiRollbackClockTests
{
    private sealed class FakeHwmStore : IHighWaterMarkStore
    {
        public DateTime? Value;
        public int WriteCount;
        public DateTime? Read() => Value;
        public void Write(DateTime utc) { Value = utc; WriteCount++; }
    }

    private sealed class ThrowingHwmStore : IHighWaterMarkStore
    {
        public DateTime? Read() => throw new InvalidOperationException("blocked");
        public void Write(DateTime utc) => throw new InvalidOperationException("blocked");
    }

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
        new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void HighWaterMark_IsMaxOfNow_Hkcu_AndProgramData()
    {
        var now = Utc(2026, 7, 8, 12, 0);
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };   // ahead
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 9) };      // between

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(Utc(2026, 7, 10), clock.HighWaterMarkUtc);
        Assert.Equal(now, clock.UtcNow);
    }

    [Fact]
    public void HighWaterMark_UsesNow_WhenBothSourcesEmptyOrBehind()
    {
        var now = Utc(2026, 7, 8, 12, 0);
        var clock = new AntiRollbackClock(() => now,
            new FakeHwmStore { Value = null }, new FakeHwmStore { Value = null });
        Assert.Equal(now, clock.HighWaterMarkUtc);
    }

    [Fact]
    public void Construction_PersistsNewMax_ToBothStores()
    {
        var now = Utc(2026, 7, 12);
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 9) };

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(now, clock.HighWaterMarkUtc);
        Assert.Equal(now, hkcu.Value);
        Assert.Equal(now, pd.Value);
        Assert.Equal(1, hkcu.WriteCount);
        Assert.Equal(1, pd.WriteCount);
    }

    // fix #14: a store already at/above the max is not rewritten.
    [Fact]
    public void RegistryAlreadyAhead_DoesNotRewrite()
    {
        var now = Utc(2026, 7, 5);
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };  // already ahead
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 10) };    // also at max

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(Utc(2026, 7, 10), clock.HighWaterMarkUtc);
        Assert.Equal(0, hkcu.WriteCount);
        Assert.Equal(0, pd.WriteCount);
    }

    [Fact]
    public void Rollback_HighWaterMarkStaysAtMaxSeen_NotNow()
    {
        var now = Utc(2026, 7, 5);              // rolled BACK
        var hkcu = new FakeHwmStore { Value = Utc(2026, 7, 10) };
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 8) };

        var clock = new AntiRollbackClock(() => now, hkcu, pd);

        Assert.Equal(Utc(2026, 7, 10), clock.HighWaterMarkUtc);
        Assert.Equal(now, clock.UtcNow);
        Assert.Equal(Utc(2026, 7, 10), hkcu.Value); // not overwritten downward
    }

    [Fact]
    public void StoreReadFailure_DoesNotThrow_FallsBackToOtherSources()
    {
        var now = Utc(2026, 7, 8);
        var pd = new FakeHwmStore { Value = Utc(2026, 7, 9) };

        AntiRollbackClock? clock = null;
        var ex = Record.Exception(() => clock = new AntiRollbackClock(() => now, new ThrowingHwmStore(), pd));

        Assert.Null(ex);
        Assert.NotNull(clock);
        Assert.Equal(Utc(2026, 7, 9), clock!.HighWaterMarkUtc);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~AntiRollbackClockTests"` → compile error: `AntiRollbackClock` / `IHighWaterMarkStore` do not exist.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Plugin/Licensing/AntiRollbackClock.cs`:
```csharp
using System;
using System.IO;
using Microsoft.Win32;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Thin persistence seam for a redundant high-water mark. Real adapters write to HKCU and
/// to a ProgramData file ONLY (spec §9 / fix #5: never HKLM for writes, never license.json
/// which is user-writable). A fake drives the monotonic logic in unit tests.
/// </summary>
public interface IHighWaterMarkStore
{
    DateTime? Read();        // null if unset/unreadable
    void Write(DateTime utc);
}

/// <summary>HKCU-backed high-water mark. All access is try/catch: a blocked/absent
/// registry yields null on read and swallows the write. Stores UTC ticks as a string
/// under HKCU\Software\RevitCortex\LicenseHighWaterMarkTicks.</summary>
public sealed class RegistryHighWaterMarkStore : IHighWaterMarkStore
{
    private const string SubKey = @"Software\RevitCortex";
    private const string ValueName = "LicenseHighWaterMarkTicks";

    public DateTime? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
            var raw = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(raw)) return null;
            if (!long.TryParse(raw, out var ticks)) return null;
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { return null; }
    }

    public void Write(DateTime utc)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key?.SetValue(ValueName, utc.ToUniversalTime().Ticks.ToString(), RegistryValueKind.String);
        }
        catch { /* registry write blocked -> anti-rollback degrades, never crashes */ }
    }
}

/// <summary>ProgramData-file high-water mark (second redundant source, fix #5). Stores UTC
/// ticks as text under %ProgramData%\RevitCortex\license-hwm.txt. All access is try/catch.</summary>
public sealed class ProgramDataHighWaterMarkStore : IHighWaterMarkStore
{
    private readonly string _path;

    public ProgramDataHighWaterMarkStore()
    {
        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "RevitCortex", "license-hwm.txt");
    }

    public DateTime? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var raw = File.ReadAllText(_path).Trim();
            if (!long.TryParse(raw, out var ticks)) return null;
            return new DateTime(ticks, DateTimeKind.Utc);
        }
        catch { return null; }
    }

    public void Write(DateTime utc)
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_path, utc.ToUniversalTime().Ticks.ToString());
        }
        catch { /* ProgramData not writable -> degrade, never crash */ }
    }
}

/// <summary>
/// System clock with a monotonic high-water mark. On construction it computes the maximum
/// of {UtcNow, HKCU mark, ProgramData mark} and persists that maximum back to any store
/// that is behind it, so the mark only ever advances. UtcNow reports the real (possibly
/// rolled-back) time; LicenseManager compares UtcNow against HighWaterMarkUtc to detect
/// rollback (spec §4 point 8). Every source read/write is total (failure -> ignored).
/// </summary>
public sealed class AntiRollbackClock : ISystemClock
{
    private readonly Func<DateTime> _now;

    public AntiRollbackClock(Func<DateTime> now, IHighWaterMarkStore hkcu, IHighWaterMarkStore programData)
    {
        _now = now ?? (() => DateTime.UtcNow);

        var current = _now().ToUniversalTime();
        var max = current;

        var a = SafeRead(hkcu);
        if (a.HasValue && a.Value > max) max = a.Value;
        var b = SafeRead(programData);
        if (b.HasValue && b.Value > max) max = b.Value;

        HighWaterMarkUtc = max;

        if (!a.HasValue || max > a.Value) SafeWrite(hkcu, max);
        if (!b.HasValue || max > b.Value) SafeWrite(programData, max);
    }

    public DateTime UtcNow => _now().ToUniversalTime();

    public DateTime HighWaterMarkUtc { get; }

    private static DateTime? SafeRead(IHighWaterMarkStore store)
    {
        try { return store?.Read(); } catch { return null; }
    }

    private static void SafeWrite(IHighWaterMarkStore store, DateTime utc)
    {
        try { store?.Write(utc); } catch { }
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **6 passed**, 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Plugin/Licensing/AntiRollbackClock.cs src/RevitCortex.Tests/Licensing/AntiRollbackClockTests.cs
git commit -m "feat(licensing): AntiRollbackClock (monotonic high-water mark over HKCU + ProgramData)" -m "Max-of-{UtcNow, HKCU, ProgramData}, persisted back to whichever store is behind; writes only advance. HKCU + ProgramData only (never HKLM writes, never user-writable license.json). Registry/file access behind IHighWaterMarkStore seam; unit-tested with fakes; every read/write is total." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: LicenseGate (cached state → allow/block decision)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/LicenseGate.cs`
- Test: `src/RevitCortex.Tests/Licensing/LicenseGateTests.cs`

**Blocking precondition:** Task 1 (`LicenseState`) merged.

`LicenseGate` wraps a `Func<LicenseState>` provider (cached state, computed at bootstrap — NOT per Route call) plus an `isDev` flag. In dev it is transparent (always Active). fix #8: on a provider fault it does NOT return `Active` — it returns the fail-closed `LicenseState.Invalid`. The non-blocking property lives in the router's `_licenseGate != null` guard (null gate = no gating), NOT in masking a fault as Active. `Allows(tool, isReadOnly)` blocks only Expired/Invalid + write; the router passes its real `IsToolReadOnly` as `isReadOnly`.

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Licensing/LicenseGateTests.cs`:
```csharp
using System;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class LicenseGateTests
{
    private static LicenseGate Gate(LicenseState state, bool isDev = false)
        => new LicenseGate(() => state, isDev);

    private static bool IsReadOnly(string toolName)
        => toolName.StartsWith("get_", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void CurrentState_ReturnsUnderlyingState_WhenNotDev()
    {
        Assert.Equal(LicenseState.Active, Gate(LicenseState.Active).CurrentState());
        Assert.Equal(LicenseState.Expired, Gate(LicenseState.Expired).CurrentState());
    }

    [Fact]
    public void CurrentState_IsDev_AlwaysActive_EvenWhenUnderlyingExpired()
    {
        Assert.Equal(LicenseState.Active, Gate(LicenseState.Expired, isDev: true).CurrentState());
    }

    [Fact]
    public void Decision_ActiveTrialGrace_AllowsEverything()
    {
        foreach (var state in new[] { LicenseState.Active, LicenseState.Trial, LicenseState.Grace })
        {
            var gate = Gate(state);
            Assert.True(gate.Allows("delete_element", IsReadOnly));
            Assert.True(gate.Allows("get_element_parameters", IsReadOnly));
        }
    }

    [Fact]
    public void Decision_ExpiredOrInvalid_BlocksWrite_AllowsReadOnly()
    {
        foreach (var state in new[] { LicenseState.Expired, LicenseState.Invalid })
        {
            var gate = Gate(state);
            Assert.False(gate.Allows("delete_element", IsReadOnly));
            Assert.True(gate.Allows("get_element_parameters", IsReadOnly));
        }
    }

    [Fact]
    public void Decision_IsDev_AllowsWrite_EvenWhenUnderlyingExpired()
    {
        Assert.True(Gate(LicenseState.Expired, isDev: true).Allows("delete_element", IsReadOnly));
    }

    // fix #8: a throwing provider must NOT be masked as Active. It fails CLOSED (Invalid);
    // a write is therefore blocked. (Router-level null-gate is what makes gating opt-in.)
    [Fact]
    public void FaultingProvider_FailsClosed_Invalid_BlocksWrite()
    {
        var gate = new LicenseGate(() => throw new InvalidOperationException("boom"), isDev: false);
        Assert.Equal(LicenseState.Invalid, gate.CurrentState());
        Assert.False(gate.Allows("delete_element", IsReadOnly));
        Assert.True(gate.Allows("get_element_parameters", IsReadOnly));
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~LicenseGateTests"` → compile error: `LicenseGate` does not exist in `RevitCortex.Plugin.Licensing`.
- [ ] **Step 3 — Minimal impl (FULL code).** Create `src/RevitCortex.Plugin/Licensing/LicenseGate.cs`:
```csharp
using System;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Plugin-side glue between Core license evaluation and the router. Holds a CACHED
/// <see cref="LicenseState"/> exposed via a provider delegate (computed at bootstrap +
/// on explicit refresh, NOT per Route call). In dev the gate is transparent (always
/// Active). A throwing/faulting provider fails CLOSED (Invalid), NOT open — the router's
/// null-gate guard is what makes gating opt-in, so licensing never crashes Revit while
/// still not silently masking a fault as a valid license.
/// </summary>
public sealed class LicenseGate
{
    private readonly Func<LicenseState> _stateProvider;
    private readonly bool _isDev;

    public LicenseGate(Func<LicenseState> stateProvider, bool isDev)
    {
        _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
        _isDev = isDev;
    }

    public bool IsDev => _isDev;

    /// <summary>Cached state. Dev is always Active; a faulting provider fails closed to
    /// Invalid (default(LicenseState)).</summary>
    public LicenseState CurrentState()
    {
        if (_isDev) return LicenseState.Active;
        try { return _stateProvider(); }
        catch { return LicenseState.Invalid; }
    }

    /// <summary>
    /// Block only when the state is Expired or Invalid AND the tool is NOT read-only.
    /// Everything else is allowed. <paramref name="isReadOnly"/> is the router's own
    /// IsToolReadOnly classifier — no new classification here.
    /// </summary>
    public bool Allows(string toolName, Func<string, bool> isReadOnly)
    {
        var state = CurrentState();
        if (state != LicenseState.Expired && state != LicenseState.Invalid)
            return true;
        return isReadOnly(toolName);
    }
}
```
- [ ] **Step 4 — Run & expect PASS.** Same filter. Expected: **6 passed**, 0 failed.
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Plugin/Licensing/LicenseGate.cs src/RevitCortex.Tests/Licensing/LicenseGateTests.cs
git commit -m "feat(licensing): LicenseGate - cached state + allow/block decision (Plugin)" -m "Wraps a LicenseState provider: dev=always Active; Allows(tool, isReadOnly) blocks only Expired/Invalid + write. A faulting provider fails CLOSED (Invalid), not open; the router null-gate guard is what makes gating opt-in." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 14: Wire LicenseGate into CortexRouter (additive guard)

**Files:**
- Modify: `src/RevitCortex.Plugin/CortexRouter.cs`
- Test: `src/RevitCortex.Tests/Router/CortexRouterLicenseGateTests.cs`

**Blocking precondition:** Task 13 (`LicenseGate`) merged.

Add `LicenseGate? licenseGate = null` as the **5th** optional ctor param (after `errorReporter`) + field `_licenseGate`. Insert the guard in `Route()` AFTER the `_disabledTools.Contains` check and BEFORE the `tool.RequiresDocument` check (verified anchor in the telemetry-dev worktree: ctor at CortexRouter.cs:90-97, guards at 176-184). Reuse the instance method `IsToolReadOnly` and `CortexErrorCode.PermissionDenied` (there is NO `LicenseExpired` code); message contains "License expired" + a renew suggestion, exactly like the read-only-mode block. Null gate ⇒ unchanged behavior (regression safety). fix #19: only the single correct test file below; real reflection injection into the private `_tools` dict (as in `CortexRouterExceptionTests`); `FakeTool` already implements `ICortexTool` incl. `Description`.

- [ ] **Step 1 — Failing test (FULL code).** Create `src/RevitCortex.Tests/Router/CortexRouterLicenseGateTests.cs`:
```csharp
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Licensing;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterLicenseGateTests
{
    // Real injection pattern (mirrors CortexRouterExceptionTests): reach into the private
    // _tools dictionary and register a FakeTool directly.
    private static void AddTool(CortexRouter router, FakeTool tool)
    {
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)
            field.GetValue(router)!;
        tools[tool.Name] = tool;
    }

    private static CortexRouter Router(LicenseGate? gate)
    {
        var session = new CortexSession(new SessionStore());
        return new CortexRouter(session, new FakeAnalyzer(),
            auditLogger: null, errorReporter: null, licenseGate: gate);
    }

    [Fact]
    public void Route_ExpiredLicense_BlocksWriteTool_WithPermissionDenied()
    {
        var router = Router(new LicenseGate(() => LicenseState.Expired, isDev: false));
        AddTool(router, new FakeTool { Name = "delete_element" });

        var result = router.Route("delete_element", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Contains("license", result.Error.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Route_ExpiredLicense_AllowsReadOnlyTool()
    {
        var router = Router(new LicenseGate(() => LicenseState.Expired, isDev: false));
        AddTool(router, new FakeTool { Name = "get_element_parameters" });

        Assert.True(router.Route("get_element_parameters", new JObject()).Success);
    }

    [Fact]
    public void Route_InvalidLicense_BlocksWriteTool()
    {
        var router = Router(new LicenseGate(() => LicenseState.Invalid, isDev: false));
        AddTool(router, new FakeTool { Name = "delete_element" });

        var result = router.Route("delete_element", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public void Route_ActiveLicense_AllowsWriteTool()
    {
        var router = Router(new LicenseGate(() => LicenseState.Active, isDev: false));
        AddTool(router, new FakeTool { Name = "delete_element" });

        Assert.True(router.Route("delete_element", new JObject()).Success);
    }

    [Fact]
    public void Route_NullGate_BehaviorUnchanged_WriteToolPasses()
    {
        var router = Router(gate: null);
        AddTool(router, new FakeTool { Name = "delete_element" });

        Assert.True(router.Route("delete_element", new JObject()).Success);
    }
}
```
- [ ] **Step 2 — Run & expect FAIL.** `--filter "FullyQualifiedName~CortexRouterLicenseGateTests"` → compile error: `CortexRouter` ctor has no `licenseGate` parameter.
- [ ] **Step 3 — Minimal impl (FULL code, two edits).**

**Edit A** — field + ctor. In `src/RevitCortex.Plugin/CortexRouter.cs`, replace:
```csharp
    public CortexRouter(CortexSession session, IDocumentAnalyzer analyzer,
        AuditLogger? auditLogger = null, ErrorReporter? errorReporter = null)
    {
        _session = session;
        _analyzer = analyzer;
        _auditLogger = auditLogger ?? new AuditLogger();
        _errorReporter = errorReporter;
    }
```
with:
```csharp
    // Cached license decision gate. Null = no gating (today's behavior, and the
    // best-effort fallback when LicenseBootstrap.Init fails). Evaluated at bootstrap
    // + on explicit refresh, NEVER per Route() call.
    private readonly Licensing.LicenseGate? _licenseGate;

    public CortexRouter(CortexSession session, IDocumentAnalyzer analyzer,
        AuditLogger? auditLogger = null, ErrorReporter? errorReporter = null,
        Licensing.LicenseGate? licenseGate = null)
    {
        _session = session;
        _analyzer = analyzer;
        _auditLogger = auditLogger ?? new AuditLogger();
        _errorReporter = errorReporter;
        _licenseGate = licenseGate;
    }
```

**Edit B** — guard in `Route()`. Replace:
```csharp
        if (_disabledTools.Contains(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is disabled",
                suggestion: "Enable it in RevitCortex Settings > Tools");

        if (tool.RequiresDocument && _session.Store.Get<object>("activeDocument") == null)
```
with:
```csharp
        if (_disabledTools.Contains(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is disabled",
                suggestion: "Enable it in RevitCortex Settings > Tools");

        // License gate (additive). Cached state; null gate = no gating. Blocks only write
        // tools when the license is Expired/Invalid — read-only tools stay available
        // (graceful degradation, spec §6). Reuses IsToolReadOnly (no new classification).
        // PermissionDenied (there is no LicenseExpired code) with "License expired" in the
        // message so the UI/agent tells this apart from user-chosen read-only mode.
        if (_licenseGate != null && !_licenseGate.Allows(toolName, IsToolReadOnly))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"License expired or invalid — write tool '{toolName}' is blocked",
                suggestion: "Renew or reactivate your license in RevitCortex > License & Account. Read-only tools remain available.");

        if (tool.RequiresDocument && _session.Store.Get<object>("activeDocument") == null)
```
- [ ] **Step 4 — Run & expect PASS.**
```bash
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexRouterLicenseGateTests"
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexRouterTests"
```
Expected: `CortexRouterLicenseGateTests` **5 passed**; `CortexRouterTests` unchanged, all passed (null-gate regression; the new param is optional so the ctor stays source-compatible).
- [ ] **Step 5 — Build gate.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```
Both 0 errors.
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Plugin/CortexRouter.cs src/RevitCortex.Tests/Router/CortexRouterLicenseGateTests.cs
git commit -m "feat(licensing): wire LicenseGate into CortexRouter (additive guard)" -m "Optional 5th ctor param licenseGate + field; guard in Route() after disabled-tools and before RequiresDocument. Reuses IsToolReadOnly and PermissionDenied (no LicenseExpired code); message carries 'License expired'. Null gate = unchanged behavior." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 15: LicenseBootstrap (assemble stack + wire into OnStartup)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/LicenseBootstrap.cs`
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs`

**Blocking precondition:** Tasks 8 (`FakeLicenseBackend`), 9 (`LicenseManager`), 10–13 (`WindowsFingerprintProvider`, `FileLicenseStore`, `AntiRollbackClock`, `LicenseGate`), 14 (router ctor) merged.

Static `Init(CortexEnvironment env)` assembles `FileLicenseStore` + `WindowsFingerprintProvider` + `AntiRollbackClock` (over `RegistryHighWaterMarkStore` + `ProgramDataHighWaterMarkStore`) + `LicenseTokenVerifier(embedded key)` + `FakeLicenseBackend` + `LicenseManager`, calls `manager.Refresh()`, and builds a `LicenseGate(() => manager.State, isDev:false)`. Mirrors `TelemetryBootstrap.Init`. Best-effort try/catch: failure ⇒ `Gate` stays null ⇒ no gating. In dev ⇒ transparent gate (`isDev:true`), no store/backend/fingerprint. Exposes `Gate`, `Manager`, `Backend`, `Fingerprint` for the UI (Task 16). fix #16: the embedded public key is `static readonly` (never `const`) — it is the runtime-generated public half of a `FakeLicenseBackend` keypair, so it cannot be a compile-time constant; Fase 1 marks it "replace with backend key in Fase 2".

There is no unit test (Revit-UI wiring touches registry/ProgramData); acceptance = both builds green + Task 14's router test still green. Record this so a reviewer does not flag the missing test.

- [ ] **Step 1 — No failing unit test (Revit-UI wiring).** Acceptance is (a) dual build green and (b) `CortexRouterLicenseGateTests` still 5 passed (the gate contract the bootstrap produces is the one that test pins). This is intentional and recorded here.
- [ ] **Step 2 — Impl: LicenseBootstrap.cs (FULL code).** Create `src/RevitCortex.Plugin/Licensing/LicenseBootstrap.cs`:
```csharp
using System;
using System.Security.Cryptography;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// Builds the process-wide licensing stack (store, fingerprint, clock, verifier, backend,
/// manager, gate) and owns the cached <see cref="LicenseGate"/>. Best-effort, mirroring
/// TelemetryBootstrap: any failure leaves <see cref="Gate"/> null, which the router treats
/// as "no gating" — licensing must never affect Revit startup. In dev the gate is
/// transparent (always Active). Hard enforcement arrives with the real backend in Fase 2.
/// </summary>
internal static class LicenseBootstrap
{
    // Fase 1 backend keypair (runtime-generated). In Fase 2 the client keeps ONLY the
    // public half of the real backend key; the private half never ships. Kept static so
    // the whole client path (activate -> verify -> gate) works end-to-end for dev/smoke.
    private static readonly RSA _fakeKey = RSA.Create(2048);

    /// <summary>Embedded PUBLIC key parameters. static readonly (fix #16) — a runtime
    /// keypair is not a compile-time constant. Fase 1 placeholder: replace with the real
    /// backend RSA-2048 public key in Fase 2.</summary>
    public static readonly RSAParameters EmbeddedPublicKey = _fakeKey.ExportParameters(false);

    public static LicenseGate? Gate { get; private set; }
    public static LicenseManager? Manager { get; private set; }
    public static ILicenseBackend? Backend { get; private set; }
    public static IFingerprintProvider? Fingerprint { get; private set; }

    public static void Init(CortexEnvironment env)
    {
        try
        {
            if (env.IsDev)
            {
                // Dev: transparent gate, no token, no store, no fingerprint, no backend. D4.
                Gate = new LicenseGate(() => LicenseState.Active, isDev: true);
                return;
            }

            var storePath = System.IO.Path.Combine(env.RootFolder, "license.json");
            var store = new FileLicenseStore(storePath);
            var fingerprint = new WindowsFingerprintProvider();
            var clock = new AntiRollbackClock(
                () => DateTime.UtcNow,
                new RegistryHighWaterMarkStore(),
                new ProgramDataHighWaterMarkStore());
            var verifier = new LicenseTokenVerifier(EmbeddedPublicKey.Modulus!, EmbeddedPublicKey.Exponent!);
            var backend = new FakeLicenseBackend(_fakeKey);
            var manager = new LicenseManager(store, fingerprint, verifier, clock, backend);
            manager.Refresh();

            Gate = new LicenseGate(() => manager.State, isDev: false);
            Manager = manager;
            Fingerprint = fingerprint;
            Backend = backend;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RevitCortex] License init failed: {ex.Message}");
            Gate = null;
            Manager = null;
            Backend = null;
            Fingerprint = null;
        }
    }
}
```
> `license.json` path is built with `Path.Combine(env.RootFolder, "license.json")` — NEVER `env.SettingsFilePath` (spec §5 / D3), enforced structurally by not referencing `SettingsFilePath` in this file.

- [ ] **Step 3 — Wire into RevitCortexApp.OnStartup (FULL edit).** In `src/RevitCortex.Plugin/RevitCortexApp.cs`, replace (lines 105-108 in the telemetry-dev worktree):
```csharp
            Telemetry.TelemetryBootstrap.Init(application);

            _router = new CortexRouter(_session, analyzer, auditLogger: auditLogger,
                errorReporter: Telemetry.TelemetryBootstrap.Reporter);
```
with:
```csharp
            Telemetry.TelemetryBootstrap.Init(application);

            // License gate: built before the router so it can be passed in. Best-effort —
            // a null Gate means no gating (see LicenseBootstrap).
            Licensing.LicenseBootstrap.Init(CortexEnvironment.Current);

            _router = new CortexRouter(_session, analyzer, auditLogger: auditLogger,
                errorReporter: Telemetry.TelemetryBootstrap.Reporter,
                licenseGate: Licensing.LicenseBootstrap.Gate);
```
> `RevitCortexApp.cs` already has `using RevitCortex.Core.Hosting;` (line 5), so `CortexEnvironment.Current` resolves with no new using.

- [ ] **Step 4 — Build gate + confirm router test.**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexRouterLicenseGateTests"
```
Both builds 0 errors (this proves the bootstrap consumes the Core/Plugin contracts with signatures that compile on net48 too). Router gate test: **5 passed**.
- [ ] **Step 5 — (covered by Step 4; no separate test to run).**
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Plugin/Licensing/LicenseBootstrap.cs src/RevitCortex.Plugin/RevitCortexApp.cs
git commit -m "feat(licensing): LicenseBootstrap assembles stack + wire into OnStartup" -m "Static Init(env) builds store+fingerprint+AntiRollbackClock(HKCU+ProgramData)+verifier+FakeLicenseBackend+LicenseManager+LicenseGate; exposes Gate/Manager/Backend/Fingerprint. Best-effort: failure -> Gate null -> no gating; dev -> transparent gate. Embedded public key is static readonly (Fase-1 placeholder). license.json path from RootFolder, never SettingsFilePath (D3). Wired into OnStartup before router construction." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 16: UI "License & Account" (minimal window + ribbon command)

**Files:**
- Create: `src/RevitCortex.Plugin/UI/LicenseWindow.cs`
- Create: `src/RevitCortex.Plugin/Commands/OpenLicense.cs`
- Modify: `src/RevitCortex.Plugin/UI/Localization.cs`
- Modify: `src/RevitCortex.Plugin/RevitCortexApp.cs` (ribbon button)

**Blocking precondition:** Task 15 (`LicenseBootstrap` exposing `Manager`/`Backend`/`Fingerprint`) merged.

Minimal code-only WPF window opened by an `IExternalCommand` (same pattern as `SendSupportReport`/`OpenSettings`) — it does NOT pass through `Route()`, so it is always reachable regardless of license state. It shows state, expiry, grace-days-left, truncated `licenseId` and offers **Activate** (`manager.Activate(key)` → persists + Refresh via the manager) and **Refresh** (`manager.Refresh()`). No unit test — build-only, keep it LAST. fix #17: NO `TextBoxHintHelper`, NO `SetValue(Tag)`, NO unused `Grid` (use `Content = root`); no "drop this if reviewer prefers". fix #20: the display accessors are pinned (`State`, `ExpiresAtUtc`, `GraceDaysRemaining`, `LicenseIdTruncated`, `Refresh()`, `Activate(string)`) — read them directly, no adaptation. fix #18: the ribbon `PushButtonData` block is given in full and added unconditionally, reusing `IconFactory.CreateSettingsIcon`.

- [ ] **Step 1 — Add localized strings (FULL edit).** In `src/RevitCortex.Plugin/UI/Localization.cs`, insert the following block immediately after the `["telemetry.settings_toggle"] = new() { ... },` entry and before the closing `};` of the `Table` initializer:
```csharp
        // ── License & Account ───────────────────────────────────────────
        ["license.window_title"] = new()
        {
            ["en"] = "License & Account",
            ["it"] = "Licenza e account",
        },
        ["license.state_label"] = new()
        {
            ["en"] = "Status:",
            ["it"] = "Stato:",
        },
        ["license.state_active"] = new()
        {
            ["en"] = "Active",
            ["it"] = "Attiva",
        },
        ["license.state_trial"] = new()
        {
            ["en"] = "Trial",
            ["it"] = "Prova",
        },
        ["license.state_grace"] = new()
        {
            ["en"] = "Offline (grace)",
            ["it"] = "Offline (periodo di tolleranza)",
        },
        ["license.state_expired"] = new()
        {
            ["en"] = "Expired",
            ["it"] = "Scaduta",
        },
        ["license.state_invalid"] = new()
        {
            ["en"] = "Not activated / invalid",
            ["it"] = "Non attivata / non valida",
        },
        ["license.expiry_label"] = new()
        {
            ["en"] = "Expires:",
            ["it"] = "Scadenza:",
        },
        ["license.grace_label"] = new()
        {
            ["en"] = "Offline days remaining:",
            ["it"] = "Giorni offline rimanenti:",
        },
        ["license.id_label"] = new()
        {
            ["en"] = "License ID:",
            ["it"] = "ID licenza:",
        },
        ["license.key_label"] = new()
        {
            ["en"] = "License key:",
            ["it"] = "Chiave di licenza:",
        },
        ["license.activate_button"] = new()
        {
            ["en"] = "Activate",
            ["it"] = "Attiva",
        },
        ["license.refresh_button"] = new()
        {
            ["en"] = "Refresh",
            ["it"] = "Aggiorna",
        },
        ["license.activate_ok"] = new()
        {
            ["en"] = "License activated. Status: {0}.",
            ["it"] = "Licenza attivata. Stato: {0}.",
        },
        ["license.activate_failed"] = new()
        {
            ["en"] = "Activation failed: {0}",
            ["it"] = "Attivazione non riuscita: {0}",
        },
        ["license.dev_transparent"] = new()
        {
            ["en"] = "Dev profile — licensing is transparent (always active).",
            ["it"] = "Profilo dev — licenza trasparente (sempre attiva).",
        },
        ["license.expired_hint"] = new()
        {
            ["en"] = "Write commands are blocked until you renew. Read-only commands still work.",
            ["it"] = "I comandi di scrittura sono bloccati fino al rinnovo. I comandi di sola lettura restano attivi.",
        },
```
- [ ] **Step 2 — No failing unit test (WPF/Revit UI).** Acceptance is dual build green + full suite unchanged. Recorded here so the missing test is not flagged.
- [ ] **Step 3 — Impl (FULL code).** Create `src/RevitCortex.Plugin/UI/LicenseWindow.cs`:
```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Minimal "License &amp; Account" window: shows state, expiry, grace days, truncated
/// licenseId; Activate (key -> manager.Activate) + Refresh. Reads the manager's pinned
/// display accessors only — no logic here. Best-effort: any fault shows a message, never
/// crashes Revit.
/// </summary>
public sealed class LicenseWindow : Window
{
    private readonly TextBlock _stateValue = new TextBlock { FontWeight = FontWeights.Bold };
    private readonly TextBlock _expiryValue = new TextBlock();
    private readonly TextBlock _graceValue = new TextBlock();
    private readonly TextBlock _idValue = new TextBlock();
    private readonly TextBlock _hint = new TextBlock
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly TextBox _keyBox = new TextBox { Margin = new Thickness(0, 2, 0, 8) };

    public LicenseWindow()
    {
        Title = Localization.T("license.window_title");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Row(Localization.T("license.state_label"), _stateValue));
        root.Children.Add(Row(Localization.T("license.expiry_label"), _expiryValue));
        root.Children.Add(Row(Localization.T("license.grace_label"), _graceValue));
        root.Children.Add(Row(Localization.T("license.id_label"), _idValue));
        root.Children.Add(_hint);

        root.Children.Add(new TextBlock
        {
            Text = Localization.T("license.key_label"),
            Margin = new Thickness(0, 10, 0, 0),
        });
        root.Children.Add(_keyBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var activate = new Button
        {
            Content = Localization.T("license.activate_button"),
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 4, 8, 4),
        };
        var refresh = new Button
        {
            Content = Localization.T("license.refresh_button"),
            Width = 110,
            Padding = new Thickness(8, 4, 8, 4),
        };
        activate.Click += OnActivate;
        refresh.Click += OnRefresh;
        buttons.Children.Add(activate);
        buttons.Children.Add(refresh);
        root.Children.Add(buttons);

        Content = root;
        RefreshDisplay();
    }

    private static StackPanel Row(string label, TextBlock value)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        p.Children.Add(new TextBlock { Text = label, Width = 170 });
        p.Children.Add(value);
        return p;
    }

    private void OnActivate(object sender, RoutedEventArgs e)
    {
        try
        {
            var manager = LicenseBootstrap.Manager;
            if (manager == null)
            {
                MessageBox.Show(Localization.T("license.dev_transparent"), Title);
                return;
            }

            var result = manager.Activate(_keyBox.Text?.Trim() ?? "");
            RefreshDisplay();
            if (result.Success)
                MessageBox.Show(Localization.T("license.activate_ok", StateText(manager.State)), Title);
            else
                MessageBox.Show(Localization.T("license.activate_failed", result.Error ?? ""), Title);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Localization.T("license.activate_failed", ex.Message), Title);
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        try { LicenseBootstrap.Manager?.Refresh(); } catch { }
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        var manager = LicenseBootstrap.Manager;
        if (manager == null)
        {
            // Dev or init-failed: transparent.
            _stateValue.Text = StateText(LicenseState.Active);
            _expiryValue.Text = "—";
            _graceValue.Text = "—";
            _idValue.Text = "—";
            _hint.Text = Localization.T("license.dev_transparent");
            return;
        }

        var state = manager.State;
        _stateValue.Text = StateText(state);
        _expiryValue.Text = manager.ExpiresAtUtc?.ToString("yyyy-MM-dd") ?? "—";
        _graceValue.Text = manager.GraceDaysRemaining.ToString();
        _idValue.Text = string.IsNullOrEmpty(manager.LicenseIdTruncated) ? "—" : manager.LicenseIdTruncated;
        _hint.Text = (state == LicenseState.Expired || state == LicenseState.Invalid)
            ? Localization.T("license.expired_hint")
            : "";
    }

    private static string StateText(LicenseState state)
    {
        switch (state)
        {
            case LicenseState.Active:  return Localization.T("license.state_active");
            case LicenseState.Trial:   return Localization.T("license.state_trial");
            case LicenseState.Grace:   return Localization.T("license.state_grace");
            case LicenseState.Expired: return Localization.T("license.state_expired");
            default:                   return Localization.T("license.state_invalid");
        }
    }
}
```
Create `src/RevitCortex.Plugin/Commands/OpenLicense.cs`:
```csharp
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitCortex.Plugin.UI;

namespace RevitCortex.Plugin.Commands;

/// <summary>
/// Opens the minimal "License &amp; Account" window. IExternalCommand (not routed through
/// Route()), so it is always available regardless of license state — the user must always
/// be able to reach Activate/Refresh.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class OpenLicense : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            new LicenseWindow().ShowDialog();
        }
        catch (System.Exception ex)
        {
            TaskDialog.Show(Localization.T("license.window_title"),
                Localization.T("license.activate_failed", ex.Message));
        }
        return Result.Succeeded;
    }
}
```
- [ ] **Step 3b — Ribbon button (FULL edit, fix #18).** In `src/RevitCortex.Plugin/RevitCortexApp.cs`, in `CreateRibbonPanel`, add this block immediately after the `supportBtn` `panel.AddItem(supportBtn);` line (reuse `CreateSettingsIcon` — do NOT invent a new icon):
```csharp
        // License & Account button
        var licenseBtn = new PushButtonData(
            "ID_CORTEX_LICENSE", "License &\r\nAccount",
            assemblyLocation, "RevitCortex.Plugin.Commands.OpenLicense");
        licenseBtn.ToolTip = "View license status and activate RevitCortex Premium";
        licenseBtn.Image = IconFactory.CreateSettingsIcon(16);
        licenseBtn.LargeImage = IconFactory.CreateSettingsIcon(32);
        panel.AddItem(licenseBtn);
```
- [ ] **Step 4 — Build gate + full suite (build-only acceptance).**
```bash
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"
```
Both builds 0 errors (`switch` statement + explicit `TextBlock`/`Button` construction are net48-safe). Full suite: all previously-passing tests still pass (this task adds no test); expected clean result with Revit absent per CLAUDE.md is **all licensing + router tests green, at most 1–2 skipped** (`RequiresMachineGuidFact` + any `RequiresRevitApiFact`).
- [ ] **Step 5 — (covered by Step 4).**
- [ ] **Step 6 — Commit.**
```
git add src/RevitCortex.Plugin/UI/LicenseWindow.cs src/RevitCortex.Plugin/Commands/OpenLicense.cs src/RevitCortex.Plugin/UI/Localization.cs src/RevitCortex.Plugin/RevitCortexApp.cs
git commit -m "feat(licensing): minimal 'License & Account' window + ribbon command" -m "LicenseWindow (code-only, no logic) shows state/expiry/grace/truncated licenseId; Activate (key -> manager.Activate -> persist+Refresh) and Refresh. OpenLicense IExternalCommand + ribbon button (reusing CreateSettingsIcon) - always reachable regardless of license state. Localized IT/EN strings. Build-only (WPF/Revit UI)." -m "Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Out of scope (Fase 1) — explicit

- Real backend (Keygen/Stripe), online refresh endpoint, webhook, e-fattura → Fase 2/3.
- Signed update-gate for the manifest (Ed25519 on `latest.json`) → separate later task.
- Obfuscation (light Obfuscar) → separate.
- **Optional future fingerprint extension (NOT a numbered task):** add BIOS/motherboard serial via WMI. It would require a per-TFM `System.Management` PackageReference (built-in net48; NuGet net8/net10) plus an R27/net10 restore gate, every WMI call wrapped in try/catch-omit. Deliberately excluded from Fase 1 (fix #6) to avoid the net10/R27 restore risk and AV heuristics; MachineGuid alone is an acceptable fingerprint because the server applies the match threshold.
