# Dev License Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Debug-only `DevLicenseBackend` that mimics a real licensing authority locally — key whitelist, node-lock, persisted RSA keypair (survives Revit restart / fix N1) — wired through the REAL `LicenseManager` even for the dev profile, with Release fail-closed (no fake backend) and a localized read-only gate message.

**Architecture:** New `DevLicenseBackend : ILicenseBackend` in Core, fed by two file-backed JSON stores (`IDevKeyStore`, `IDevNodeLockStore`) whose Plugin implementations live in `RevitCortex.Plugin/Licensing`. `LicenseBootstrap.Init` is restructured: under `#if DEBUG` it builds a real `LicenseManager` + `DevLicenseBackend` with a **non-dev** gate (D4 "dev transparent" is deliberately suspended in Debug so the gate is exercisable); under `#else` it fails closed (gate null, no `FakeLicenseBackend`). The gate block message moves to `Localization` (IT/EN).

**Tech Stack:** C# (netstandard2.0 Core / net48+net8 Plugin), xUnit, Newtonsoft.Json, System.Security.Cryptography (RSAParameters import/export as base64 JSON — never ToXmlString, not net48-safe).

**Revised against** `docs/superpowers/specs/2026-07-08-dev-license-backend-evaluation-design.md`, which caught two bugs in the first draft: (1) `CORTEX-EXPIRED` activated now evaluates to **Grace**, not Expired (writes stay unlocked) → renamed `CORTEX-GRACE`, hard-Expired shown only via a store fixture; (2) the dev profile early-returns a transparent gate, so the backend would be **dead code** under `deploy-dev.ps1` → bootstrap restructured to select by build config, not profile.

---

## Cross-group contracts (pinned — cited identically everywhere)

- **`IDevKeyStore`** (Core): `RSAParameters LoadOrCreate()` — full (private) keypair, generated+persisted on first call, reloaded after. `RSAParameters PublicOnly()` — public half (Modulus+Exponent).
- **`IDevNodeLockStore`** (Core): `string? GetBoundFingerprint(string licenseKey)` — bound fingerprint or null. `bool TryBind(string licenseKey, string fingerprint)` — persist the binding; returns false if the write failed (activation then fails, per eval-spec save-failure policy).
- **`DevLicenseBackend`** (Core) implements `ILicenseBackend` (unchanged). Ctors: `DevLicenseBackend(IDevKeyStore, IDevNodeLockStore)` and `DevLicenseBackend(IDevKeyStore, IDevNodeLockStore, Func<DateTime> nowUtc)`.
- **Node-lock:** binds/compares `fingerprintHashes[0]`. Empty list → `Fail("no machine fingerprint available")`.
- **Whitelist** (state, expiry-from-`nowUtc`): `CORTEX-ACTIVE-2026`→(active, +1y); `CORTEX-TRIAL-14`→(trial, +14d); `CORTEX-GRACE`→(active, −1d). Anything else → `Fail("invalid license key")`.
- **Wire format (identical to FakeLicenseBackend):** `base64(payloadJsonUtf8) + "." + base64(pkcs1-sha256 sig over the SAME bytes)`. Payload keys: `licenseId`, `state`, `expiresAtUtc` (`yyyy-MM-ddTHH:mm:ssZ`), `seatLimit`, `fingerprintHashes`, `issuedAtUtc`.
- **`LicenseActivationResult`** (existing): `Ok(string)` / `Fail(string)`; `.Success/.Token/.Error`.
- **`LicenseTokenVerifier`** (existing): ctor `(byte[] modulus, byte[] exponent)`, `LicenseToken? Verify(string)`.
- **`LicenseManager`** (existing, unchanged): ctor `(ILicenseStore, IFingerprintProvider, LicenseTokenVerifier, ISystemClock, ILicenseBackend)`, `.State`, `.Activate(key)`, `.Refresh()`.

**Do NOT touch:** `ILicenseBackend`, `LicenseManager`, `LicenseGate` (decision logic), `LicenseTokenVerifier`, `FileLicenseStore`, `FakeLicenseBackend`, and the 696 existing tests. `FakeLicenseBackend` stays for its tests but is no longer wired into the plugin.

---

## Task 1: `IDevKeyStore` + `IDevNodeLockStore` interfaces (Core)

**Files:**
- Create: `src/RevitCortex.Core/Licensing/IDevKeyStore.cs`
- Create: `src/RevitCortex.Core/Licensing/IDevNodeLockStore.cs`

- [ ] **Step 1: Write the interfaces**

`IDevKeyStore.cs`:
```csharp
using System.Security.Cryptography;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Dev/demo-only store for the RSA keypair that signs demo license tokens. Persists the
/// FULL keypair so the same signing key survives process restarts (fix N1). Never used in
/// Release builds — a persisted private key must not ship. Cross-target: stored as
/// RSAParameters byte arrays (base64 JSON), never ToXmlString (not net48-safe).
/// </summary>
public interface IDevKeyStore
{
    /// <summary>Load the persisted keypair, or generate + persist one on first call.</summary>
    RSAParameters LoadOrCreate();

    /// <summary>Public half (Modulus+Exponent) of the same keypair, for the verifier.</summary>
    RSAParameters PublicOnly();
}
```

`IDevNodeLockStore.cs`:
```csharp
namespace RevitCortex.Core.Licensing;

/// <summary>
/// Dev/demo-only store binding a license key to the first machine fingerprint that
/// activated it. Simulates Keygen's node-lock. First-write-wins is enforced by the
/// backend, not here.
/// </summary>
public interface IDevNodeLockStore
{
    /// <summary>The fingerprint bound to this key, or null if never activated.</summary>
    string? GetBoundFingerprint(string licenseKey);

    /// <summary>Persist key -> fingerprint. Returns false if the write failed (the backend
    /// then fails activation, so a lock is never accepted without being persisted).</summary>
    bool TryBind(string licenseKey, string fingerprint);
}
```

- [ ] **Step 2: Build Core**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Core/RevitCortex.Core.csproj`
Expected: `Errori: 0`

- [ ] **Step 3: Commit**

```bash
git add src/RevitCortex.Core/Licensing/IDevKeyStore.cs src/RevitCortex.Core/Licensing/IDevNodeLockStore.cs
git commit -m "$(cat <<'EOF'
feat(licensing): IDevKeyStore + IDevNodeLockStore interfaces (dev backend contracts)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: `DevLicenseBackend` — whitelist + minting (Core)

**Files:**
- Create: `src/RevitCortex.Core/Licensing/DevLicenseBackend.cs`
- Test: `src/RevitCortex.Tests/Licensing/DevLicenseBackendTests.cs`

- [ ] **Step 1: Write the failing test file**

```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using RevitCortex.Core.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class DevLicenseBackendTests : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly RSAParameters _full;
    private readonly RSAParameters _pub;

    public DevLicenseBackendTests()
    {
        _full = _key.ExportParameters(true);
        _pub = _key.ExportParameters(false);
    }

    public void Dispose() => _key.Dispose();

    private static readonly DateTime Now = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeKeyStore : IDevKeyStore
    {
        private readonly RSAParameters _f, _p;
        public FakeKeyStore(RSAParameters f, RSAParameters p) { _f = f; _p = p; }
        public RSAParameters LoadOrCreate() => _f;
        public RSAParameters PublicOnly() => _p;
    }

    private sealed class FakeNodeLock : IDevNodeLockStore
    {
        public readonly Dictionary<string, string> Map = new Dictionary<string, string>();
        public bool FailWrites = false;
        public string? GetBoundFingerprint(string k) => Map.TryGetValue(k, out var v) ? v : null;
        public bool TryBind(string k, string fp) { if (FailWrites) return false; Map[k] = fp; return true; }
    }

    private DevLicenseBackend NewBackend(FakeNodeLock? nl = null) =>
        new DevLicenseBackend(new FakeKeyStore(_full, _pub), nl ?? new FakeNodeLock(), () => Now);

    private LicenseTokenVerifier Verifier() => new LicenseTokenVerifier(_pub.Modulus!, _pub.Exponent!);

    [Fact]
    public void Activate_ActiveKey_MintsActiveTokenPlusOneYear()
    {
        var r = NewBackend().Activate("CORTEX-ACTIVE-2026", new List<string> { "fpA" });
        Assert.True(r.Success);
        var t = Verifier().Verify(r.Token!);
        Assert.NotNull(t);
        Assert.Equal("active", t!.State);
        Assert.Equal(Now.AddYears(1), t.ExpiresAtUtc);
        Assert.Equal(new[] { "fpA" }, t.FingerprintHashes);
    }

    [Fact]
    public void Activate_TrialKey_MintsTrialTokenPlus14Days()
    {
        var t = Verifier().Verify(NewBackend().Activate("CORTEX-TRIAL-14", new List<string> { "fpA" }).Token!);
        Assert.Equal("trial", t!.State);
        Assert.Equal(Now.AddDays(14), t.ExpiresAtUtc);
    }

    [Fact]
    public void Activate_GraceKey_MintsActiveTokenExpiredYesterday()
    {
        var t = Verifier().Verify(NewBackend().Activate("CORTEX-GRACE", new List<string> { "fpA" }).Token!);
        Assert.Equal("active", t!.State);
        Assert.Equal(Now.AddDays(-1), t.ExpiresAtUtc);
    }

    [Fact]
    public void Activate_UnknownKey_Fails()
    {
        var r = NewBackend().Activate("NOPE", new List<string> { "fpA" });
        Assert.False(r.Success);
        Assert.Contains("invalid license key", r.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_EmptyFingerprint_Fails()
    {
        var r = NewBackend().Activate("CORTEX-ACTIVE-2026", new List<string>());
        Assert.False(r.Success);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevLicenseBackendTests"`
Expected: FAIL — `DevLicenseBackend` does not exist (compile error).

- [ ] **Step 3: Write `DevLicenseBackend.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Dev/demo-only <see cref="ILicenseBackend"/> that mimics a real licensing authority
/// LOCALLY: a fixed key whitelist (active / trial-14d / grace), node-lock to the first
/// machine fingerprint, and a persisted signing keypair (via <see cref="IDevKeyStore"/>) so
/// tokens survive process restarts. Selected only under #if DEBUG in LicenseBootstrap;
/// Release is fail-closed (no backend). Wire format matches FakeLicenseBackend so
/// LicenseTokenVerifier round-trips. NOTE: CORTEX-GRACE mints a token expired yesterday;
/// under LicenseManager (lastOnlineCheck=now) that evaluates to Grace, not hard Expired —
/// hard Expired is only reachable via an aged stored license (see plan Task 8 fixture).
/// </summary>
public class DevLicenseBackend : ILicenseBackend
{
    private sealed class Plan
    {
        public string State = "active";
        public Func<DateTime, DateTime> Expiry = now => now.AddYears(1);
    }

    private static readonly Dictionary<string, Plan> Whitelist =
        new Dictionary<string, Plan>(StringComparer.Ordinal)
        {
            ["CORTEX-ACTIVE-2026"] = new Plan { State = "active", Expiry = n => n.AddYears(1) },
            ["CORTEX-TRIAL-14"]    = new Plan { State = "trial",  Expiry = n => n.AddDays(14) },
            ["CORTEX-GRACE"]       = new Plan { State = "active", Expiry = n => n.AddDays(-1) },
        };

    private readonly IDevKeyStore _keyStore;
    private readonly IDevNodeLockStore _nodeLock;
    private readonly Func<DateTime> _nowUtc;

    public DevLicenseBackend(IDevKeyStore keyStore, IDevNodeLockStore nodeLock)
        : this(keyStore, nodeLock, () => DateTime.UtcNow) { }

    public DevLicenseBackend(IDevKeyStore keyStore, IDevNodeLockStore nodeLock, Func<DateTime> nowUtc)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _nodeLock = nodeLock ?? throw new ArgumentNullException(nameof(nodeLock));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public LicenseActivationResult Activate(string licenseKey, IReadOnlyList<string> fingerprintHashes)
    {
        var key = (licenseKey ?? "").Trim();
        if (!Whitelist.TryGetValue(key, out var plan))
            return LicenseActivationResult.Fail("invalid license key");

        if (fingerprintHashes == null || fingerprintHashes.Count == 0)
            return LicenseActivationResult.Fail("no machine fingerprint available");

        // Node-lock enforced in Task 3; Task 2 only mints.
        var now = _nowUtc();
        return LicenseActivationResult.Ok(Mint(key, plan, now, fingerprintHashes));
    }

    public LicenseActivationResult Validate(string wireToken)
    {
        if (string.IsNullOrEmpty(wireToken) || wireToken.IndexOf('.') < 0)
            return LicenseActivationResult.Fail("malformed token");
        return LicenseActivationResult.Ok(wireToken);
    }

    private string Mint(string licenseKey, Plan plan, DateTime now, IReadOnlyList<string> fps)
    {
        var fpArray = new JArray();
        foreach (var h in fps) fpArray.Add(h);

        var payload = new JObject
        {
            ["licenseId"] = licenseKey,
            ["state"] = plan.State,
            ["expiresAtUtc"] = plan.Expiry(now).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["seatLimit"] = 1,
            ["fingerprintHashes"] = fpArray,
            ["issuedAtUtc"] = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(_keyStore.LoadOrCreate());
            var sig = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(payloadBytes) + "." + Convert.ToBase64String(sig);
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevLicenseBackendTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Build R24**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Core/Licensing/DevLicenseBackend.cs src/RevitCortex.Tests/Licensing/DevLicenseBackendTests.cs
git commit -m "$(cat <<'EOF'
feat(licensing): DevLicenseBackend whitelist + token minting (dev-only)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Node-lock enforcement in `DevLicenseBackend` (Core)

**Files:**
- Modify: `src/RevitCortex.Core/Licensing/DevLicenseBackend.cs`
- Test: `src/RevitCortex.Tests/Licensing/DevLicenseBackendTests.cs` (add tests)

- [ ] **Step 1: Add the failing tests**

Append to `DevLicenseBackendTests`:
```csharp
    [Fact]
    public void Activate_FirstTime_BindsFingerprint()
    {
        var nl = new FakeNodeLock();
        var r = NewBackend(nl).Activate("CORTEX-ACTIVE-2026", new List<string> { "fp1" });
        Assert.True(r.Success);
        Assert.Equal("fp1", nl.GetBoundFingerprint("CORTEX-ACTIVE-2026"));
    }

    [Fact]
    public void Activate_SameFingerprint_Succeeds()
    {
        var nl = new FakeNodeLock();
        var b = NewBackend(nl);
        b.Activate("CORTEX-ACTIVE-2026", new List<string> { "fp1" });
        Assert.True(b.Activate("CORTEX-ACTIVE-2026", new List<string> { "fp1" }).Success);
    }

    [Fact]
    public void Activate_DifferentFingerprint_Fails()
    {
        var nl = new FakeNodeLock();
        var b = NewBackend(nl);
        b.Activate("CORTEX-ACTIVE-2026", new List<string> { "fp1" });
        var r2 = b.Activate("CORTEX-ACTIVE-2026", new List<string> { "fp2" });
        Assert.False(r2.Success);
        Assert.Contains("another machine", r2.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Activate_NodeLockWriteFails_FailsActivation()
    {
        var nl = new FakeNodeLock { FailWrites = true };
        var r = NewBackend(nl).Activate("CORTEX-ACTIVE-2026", new List<string> { "fp1" });
        Assert.False(r.Success);
    }
```

- [ ] **Step 2: Run to verify the new tests fail**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevLicenseBackendTests"`
Expected: the 4 new tests FAIL (no binding/enforcement yet).

- [ ] **Step 3: Add node-lock logic to `Activate`**

In `DevLicenseBackend.Activate`, replace:
```csharp
        // Node-lock enforced in Task 3; Task 2 only mints.
        var now = _nowUtc();
        return LicenseActivationResult.Ok(Mint(key, plan, now, fingerprintHashes));
```
with:
```csharp
        var fp = fingerprintHashes[0];
        var bound = _nodeLock.GetBoundFingerprint(key);
        if (bound == null)
        {
            if (!_nodeLock.TryBind(key, fp))
                return LicenseActivationResult.Fail("could not persist license activation");
        }
        else if (!string.Equals(bound, fp, StringComparison.Ordinal))
        {
            return LicenseActivationResult.Fail("license already activated on another machine");
        }

        var now = _nowUtc();
        return LicenseActivationResult.Ok(Mint(key, plan, now, fingerprintHashes));
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevLicenseBackendTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Core/Licensing/DevLicenseBackend.cs src/RevitCortex.Tests/Licensing/DevLicenseBackendTests.cs
git commit -m "$(cat <<'EOF'
feat(licensing): node-lock enforcement in DevLicenseBackend (first-fingerprint-wins)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: `FileDevKeyStore` — persisted RSA keypair, JSON (Plugin)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/FileDevKeyStore.cs`
- Test: `src/RevitCortex.Tests/Licensing/FileDevKeyStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FileDevKeyStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public FileDevKeyStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rc-devkey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "dev-license-key.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void LoadOrCreate_FirstCall_GeneratesAndPersists()
    {
        var p = new FileDevKeyStore(_path).LoadOrCreate();
        Assert.NotNull(p.Modulus);
        Assert.NotNull(p.D);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void LoadOrCreate_SecondInstance_ReturnsSameKey()
    {
        var first = new FileDevKeyStore(_path).LoadOrCreate();
        var second = new FileDevKeyStore(_path).LoadOrCreate();
        Assert.Equal(first.Modulus, second.Modulus);
        Assert.Equal(first.D, second.D);
    }

    [Fact]
    public void SignedTokenSurvivesReload_VerifierAcceptsAcrossInstances()
    {
        // Simulates a Revit restart: instance A signs, fresh instance B verifies.
        var backendA = new DevLicenseBackend(new FileDevKeyStore(_path), new InMemNodeLock(),
            () => new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc));
        var token = backendA.Activate("CORTEX-ACTIVE-2026", new List<string> { "fp1" }).Token!;

        var pubB = new FileDevKeyStore(_path).PublicOnly();
        var verifier = new LicenseTokenVerifier(pubB.Modulus!, pubB.Exponent!);
        Assert.NotNull(verifier.Verify(token));
    }

    [Fact]
    public void CorruptKeyFile_RenamedBad_AndRegenerated()
    {
        File.WriteAllText(_path, "{ not valid json ");
        var p = new FileDevKeyStore(_path).LoadOrCreate();
        Assert.NotNull(p.Modulus);                       // regenerated
        Assert.True(File.Exists(_path + ".bad"));        // corrupt file preserved
        Assert.True(File.Exists(_path));                 // fresh key written
    }

    private sealed class InMemNodeLock : IDevNodeLockStore
    {
        private readonly Dictionary<string, string> _m = new();
        public string? GetBoundFingerprint(string k) => _m.TryGetValue(k, out var v) ? v : null;
        public bool TryBind(string k, string fp) { _m[k] = fp; return true; }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~FileDevKeyStoreTests"`
Expected: FAIL — `FileDevKeyStore` does not exist.

- [ ] **Step 3: Write `FileDevKeyStore.cs`**

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// File-backed <see cref="IDevKeyStore"/>: persists the full RSA keypair as base64
/// RSAParameters fields in a JSON file (cross-target — never ToXmlString). Dev/demo only;
/// gated to Debug builds by LicenseBootstrap. A corrupt file is renamed ".bad" and the
/// key is regenerated (old debug tokens stop verifying — acceptable local demo state).
/// </summary>
public class FileDevKeyStore : IDevKeyStore
{
    private readonly string _path;
    private RSAParameters? _cached;

    public FileDevKeyStore(string path) { _path = path; }

    public RSAParameters LoadOrCreate()
    {
        if (_cached != null) return _cached.Value;

        var loaded = TryLoad();
        if (loaded != null) { _cached = loaded.Value; return _cached.Value; }

        using (var rsa = RSA.Create(2048))
        {
            var p = rsa.ExportParameters(true);
            Save(p);
            _cached = p;
            return p;
        }
    }

    public RSAParameters PublicOnly()
    {
        var full = LoadOrCreate();
        return new RSAParameters { Modulus = full.Modulus, Exponent = full.Exponent };
    }

    private RSAParameters? TryLoad()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var o = JObject.Parse(File.ReadAllText(_path));
            return new RSAParameters
            {
                Modulus  = B64(o, "modulus"),
                Exponent = B64(o, "exponent"),
                D        = B64(o, "d"),
                P        = B64(o, "p"),
                Q        = B64(o, "q"),
                DP       = B64(o, "dp"),
                DQ       = B64(o, "dq"),
                InverseQ = B64(o, "inverseQ"),
            };
        }
        catch
        {
            // Corrupt: preserve as .bad (best-effort), then signal regenerate.
            try { if (File.Exists(_path + ".bad")) File.Delete(_path + ".bad"); File.Move(_path, _path + ".bad"); }
            catch { }
            return null;
        }
    }

    private void Save(RSAParameters p)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var o = new JObject
        {
            ["format"] = 1,
            ["algorithm"] = "RSA-2048-PKCS1-SHA256",
            ["modulus"]  = Conv(p.Modulus),
            ["exponent"] = Conv(p.Exponent),
            ["d"]        = Conv(p.D),
            ["p"]        = Conv(p.P),
            ["q"]        = Conv(p.Q),
            ["dp"]       = Conv(p.DP),
            ["dq"]       = Conv(p.DQ),
            ["inverseQ"] = Conv(p.InverseQ),
        };
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, o.ToString(Newtonsoft.Json.Formatting.Indented));
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }

    private static string? Conv(byte[]? b) => b == null ? null : Convert.ToBase64String(b);
    private static byte[]? B64(JObject o, string k)
    {
        var v = (string?)o[k];
        return string.IsNullOrEmpty(v) ? null : Convert.FromBase64String(v);
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~FileDevKeyStoreTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Build R24**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Plugin/Licensing/FileDevKeyStore.cs src/RevitCortex.Tests/Licensing/FileDevKeyStoreTests.cs
git commit -m "$(cat <<'EOF'
feat(licensing): FileDevKeyStore — persisted RSA keypair, JSON, .bad recovery (fix N1)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: `FileDevNodeLockStore` — persisted node-lock map (Plugin)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/FileDevNodeLockStore.cs`
- Test: `src/RevitCortex.Tests/Licensing/FileDevNodeLockStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class FileDevNodeLockStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public FileDevNodeLockStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rc-nodelock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "dev-node-lock.json");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void GetBoundFingerprint_Unknown_ReturnsNull()
        => Assert.Null(new FileDevNodeLockStore(_path).GetBoundFingerprint("K"));

    [Fact]
    public void TryBind_ThenGet_ReturnsFingerprint()
    {
        var s = new FileDevNodeLockStore(_path);
        Assert.True(s.TryBind("K", "fp1"));
        Assert.Equal("fp1", s.GetBoundFingerprint("K"));
    }

    [Fact]
    public void TryBind_PersistsAcrossInstances()
    {
        new FileDevNodeLockStore(_path).TryBind("K", "fp1");
        Assert.Equal("fp1", new FileDevNodeLockStore(_path).GetBoundFingerprint("K"));
    }

    [Fact]
    public void CorruptFile_TreatedAsEmpty()
    {
        File.WriteAllText(_path, "{ not json ");
        Assert.Null(new FileDevNodeLockStore(_path).GetBoundFingerprint("K"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~FileDevNodeLockStoreTests"`
Expected: FAIL — `FileDevNodeLockStore` does not exist.

- [ ] **Step 3: Write `FileDevNodeLockStore.cs`**

```csharp
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Licensing;

namespace RevitCortex.Plugin.Licensing;

/// <summary>
/// File-backed <see cref="IDevNodeLockStore"/>: JSON `{ format, locks: { key: fingerprint } }`.
/// Dev/demo only. Missing/corrupt file → empty. A failed write returns false so the backend
/// fails activation rather than accepting an unpersisted lock. Atomic temp-replace write.
/// </summary>
public class FileDevNodeLockStore : IDevNodeLockStore
{
    private readonly string _path;

    public FileDevNodeLockStore(string path) { _path = path; }

    public string? GetBoundFingerprint(string licenseKey)
    {
        var locks = LoadLocks();
        return (string?)locks[licenseKey];
    }

    public bool TryBind(string licenseKey, string fingerprint)
    {
        try
        {
            var locks = LoadLocks();
            locks[licenseKey] = fingerprint;
            var root = new JObject { ["format"] = 1, ["locks"] = locks };

            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, root.ToString(Newtonsoft.Json.Formatting.Indented));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
            return true;
        }
        catch { return false; }
    }

    private JObject LoadLocks()
    {
        try
        {
            if (!File.Exists(_path)) return new JObject();
            var root = JObject.Parse(File.ReadAllText(_path));
            return root["locks"] as JObject ?? new JObject();
        }
        catch { return new JObject(); }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~FileDevNodeLockStoreTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Build R24**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Plugin/Licensing/FileDevNodeLockStore.cs src/RevitCortex.Tests/Licensing/FileDevNodeLockStoreTests.cs
git commit -m "$(cat <<'EOF'
feat(licensing): FileDevNodeLockStore — persisted key->fingerprint map (dev backend)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Restructure `LicenseBootstrap` — Debug real gate, Release fail-closed

**Files:**
- Modify: `src/RevitCortex.Plugin/Licensing/LicenseBootstrap.cs`

**Context (read the current file first):** `Init(env)` today is:
```csharp
try
{
    if (env.IsDev) { Gate = new LicenseGate(() => LicenseState.Active, isDev: true); return; }
    var storePath = ...; var store = new FileLicenseStore(storePath);
    var fingerprint = new WindowsFingerprintProvider();
    var clock = new AntiRollbackClock(() => DateTime.UtcNow, new RegistryHighWaterMarkStore(), new ProgramDataHighWaterMarkStore());
    var verifier = new LicenseTokenVerifier(EmbeddedPublicKey.Modulus!, EmbeddedPublicKey.Exponent!);
    var backend = new FakeLicenseBackend(_fakeKey);
    var manager = new LicenseManager(store, fingerprint, verifier, clock, backend);
    manager.Refresh();
    Gate = new LicenseGate(() => manager.State, isDev: false);
    Manager = manager; Fingerprint = fingerprint; Backend = backend;
}
catch (Exception ex) { ...; Gate = null; Manager = null; Backend = null; Fingerprint = null; }
```
We replace the **entire body of the `try`** (both the `IsDev` early-return AND the non-dev block) with a `#if DEBUG` / `#else`. No unit test here — validated by the build matrix + Task 8 bootstrap/integration tests. This deliberately **suspends D4 in Debug** (dev profile is no longer transparent) so the gate is exercisable; documented in the design.

- [ ] **Step 1: Replace the `try` body**

Replace everything between `try` `{` and the closing `}` before `catch` with:
```csharp
#if DEBUG
            // DEBUG: real manager + DevLicenseBackend, EVEN for the dev profile. D4
            // (IsDev => transparent) is deliberately suspended in Debug so the gate can be
            // exercised live. Debug builds never ship. env.RootFolder keeps dev/prod profiles
            // separate (dev => ~/.revitcortex-dev).
            var store = new FileLicenseStore(System.IO.Path.Combine(env.RootFolder, "license.json"));
            var fingerprint = new WindowsFingerprintProvider();
            var clock = new AntiRollbackClock(
                () => DateTime.UtcNow,
                new RegistryHighWaterMarkStore(),
                new ProgramDataHighWaterMarkStore());
            var keyStore = new FileDevKeyStore(System.IO.Path.Combine(env.RootFolder, "dev-license-key.json"));
            var nodeLock = new FileDevNodeLockStore(System.IO.Path.Combine(env.RootFolder, "dev-node-lock.json"));
            var devPub = keyStore.PublicOnly();
            var verifier = new LicenseTokenVerifier(devPub.Modulus!, devPub.Exponent!);
            var backend = new DevLicenseBackend(keyStore, nodeLock);
            var manager = new LicenseManager(store, fingerprint, verifier, clock, backend);
            manager.Refresh();
            Gate = new LicenseGate(() => manager.State, isDev: false);
            Manager = manager;
            Fingerprint = fingerprint;
            Backend = backend;
#else
            // RELEASE before Fase 2: fail-closed-honest. No FakeLicenseBackend (it accepts any
            // key). Gate null => NO gating => app runs full (like today's prod), but WITHOUT a
            // fake licensing authority in a production binary. Real enforcement = Keygen (Fase 2).
            Gate = null;
            Manager = null;
            Backend = null;
            Fingerprint = null;
#endif
```

- [ ] **Step 2: Handle now-unused Release fields**

`_fakeKey` and `EmbeddedPublicKey` are now unused in Release (and in Debug). Wrap their declarations in `#if DEBUG` is wrong (they're not used in Debug either now). Instead, keep them but suppress the warning: above the `_fakeKey` field add `#pragma warning disable` is heavy — simpler: mark them used-for-Fase-2 by leaving them and confirming the build has no *error* (unused private field is CS0169 warning, not error). If the build treats warnings as errors, delete `_fakeKey`/`EmbeddedPublicKey` and the `using System.Security.Cryptography;` if now unused. **Check:** run Step 3; if it errors on unused fields, remove them.

- [ ] **Step 3: Build R25 (Debug)**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 4: Build R24 (Debug)**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 5: Build Release (the #else fail-closed path)**

Run: `dotnet build -c "Release R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Plugin/Licensing/LicenseBootstrap.cs
git commit -m "$(cat <<'EOF'
feat(licensing): Debug real gate via DevLicenseBackend; Release fail-closed (no fake)

Suspends D4 (dev-transparent) in Debug so the gate is exercisable; Release wires no
FakeLicenseBackend (gate null) — no fake licensing authority in a production binary.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Localize the gate block message (Plugin)

**Files:**
- Modify: `src/RevitCortex.Plugin/UI/Localization.cs`
- Modify: `src/RevitCortex.Plugin/CortexRouter.cs:193-196`
- Test: `src/RevitCortex.Tests/Router/CortexRouterLicenseMessageTests.cs`

**IMPORTANT — locale trap:** `Localization.DetectLocale()` falls back to
`CultureInfo.CurrentUICulture` with no Revit `UIApplication`. On an Italian machine tests
run under "it", so asserting English words would fail for the wrong reason. Tests must be
**locale-independent**: assert the resolved string is NOT the raw key and interpolates the
tool name.

- [ ] **Step 1: Add the failing test**

Create `src/RevitCortex.Tests/Router/CortexRouterLicenseMessageTests.cs`:
```csharp
using RevitCortex.Plugin.UI;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterLicenseMessageTests
{
    [Fact]
    public void GateBlockedKey_IsTranslated_AndInterpolatesToolName()
    {
        var msg = Localization.T("license.gate_blocked", "create_level");
        Assert.NotEqual("license.gate_blocked", msg);   // a translation exists
        Assert.Contains("create_level", msg);           // {0} interpolated
    }

    [Fact]
    public void GateSuggestionKey_IsTranslated()
    {
        var s = Localization.T("license.gate_suggestion");
        Assert.NotEqual("license.gate_suggestion", s);
        Assert.NotEqual("", s);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexRouterLicenseMessageTests"`
Expected: FAIL — `T("license.gate_blocked", "create_level")` returns the raw key (no interpolation).

- [ ] **Step 3: Add the two keys to `Localization.cs`**

Immediately after the `["license.expired_hint"]` entry (before the table's closing `};`), add:
```csharp
        ["license.gate_blocked"] = new()
        {
            ["en"] = "License not active: RevitCortex Premium is running in read-only mode. Editing command '{0}' is disabled until you activate a valid license.",
            ["it"] = "Licenza non attiva: RevitCortex Premium funziona in sola lettura. Il comando di modifica '{0}' è disattivato finché non attivi una licenza valida.",
        },
        ["license.gate_suggestion"] = new()
        {
            ["en"] = "Activate a license in RevitCortex > License & Account. Read-only commands remain available.",
            ["it"] = "Attiva una licenza da RevitCortex > Licenza e account. I comandi di sola lettura restano disponibili.",
        },
```

- [ ] **Step 4: Swap the hard-coded strings in `CortexRouter.cs`**

Find (around line 193):
```csharp
        if (_licenseGate != null && !_licenseGate.Allows(toolName, IsToolReadOnly))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                $"License expired or invalid — write tool '{toolName}' is blocked",
                suggestion: "Renew or reactivate your license in RevitCortex > License & Account. Read-only tools remain available.");
```
Replace with:
```csharp
        if (_licenseGate != null && !_licenseGate.Allows(toolName, IsToolReadOnly))
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                UI.Localization.T("license.gate_blocked", toolName),
                suggestion: UI.Localization.T("license.gate_suggestion"));
```

- [ ] **Step 5: Run the new test + licensing regression**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexRouterLicense"`
Expected: PASS — new 2 tests + existing `CortexRouterLicenseGateTests` still green (gate still returns PermissionDenied; only the message text changed).

- [ ] **Step 6: Build R24**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 7: Commit**

```bash
git add src/RevitCortex.Plugin/UI/Localization.cs src/RevitCortex.Plugin/CortexRouter.cs src/RevitCortex.Tests/Router/CortexRouterLicenseMessageTests.cs
git commit -m "$(cat <<'EOF'
feat(licensing): localize gate block message (IT/EN, read-only wording)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 8: Integration tests — real LicenseManager states (Grace + hard Expired)

**Files:**
- Test: `src/RevitCortex.Tests/Licensing/DevBackendManagerIntegrationTests.cs`

**Why:** the corrected whitelist depends on `CORTEX-GRACE` evaluating to **Grace** (not Expired) through the REAL manager, and hard `Expired` being reachable only via an aged store. These tests prove both against `LicenseManager` — no manager change.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using RevitCortex.Core.Licensing;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Licensing;

public class DevBackendManagerIntegrationTests : IDisposable
{
    private readonly string _dir;
    public DevBackendManagerIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "rc-devint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class TestClock : ISystemClock
    {
        private readonly DateTime _now;
        public TestClock(DateTime now) { _now = now; }
        public DateTime UtcNow => _now;
        public DateTime HighWaterMarkUtc => _now;
    }

    private LicenseManager NewManager(DateTime now, out DevLicenseBackend backend, out FileLicenseStore store)
    {
        var keyStore = new FileDevKeyStore(Path.Combine(_dir, "dev-license-key.json"));
        var nodeLock = new FileDevNodeLockStore(Path.Combine(_dir, "dev-node-lock.json"));
        backend = new DevLicenseBackend(keyStore, nodeLock, () => now);
        var pub = keyStore.PublicOnly();
        var verifier = new LicenseTokenVerifier(pub.Modulus!, pub.Exponent!);
        store = new FileLicenseStore(Path.Combine(_dir, "license.json"));
        var fp = new FakeFingerprintProvider(new[] { "fp1" });
        return new LicenseManager(store, fp, verifier, new TestClock(now), backend);
    }

    [Fact]
    public void ActivateActiveKey_ManagerStateIsActive()
    {
        var now = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);
        var m = NewManager(now, out _, out _);
        var r = m.Activate("CORTEX-ACTIVE-2026");
        Assert.True(r.Success);
        Assert.Equal(LicenseState.Active, m.State);
    }

    [Fact]
    public void ActivateGraceKey_ManagerStateIsGrace_NotExpired()
    {
        // The honest behavior the corrected whitelist relies on: expired token activated
        // now => Grace (lastOnlineCheck=now), so writes STAY allowed.
        var now = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);
        var m = NewManager(now, out _, out _);
        m.Activate("CORTEX-GRACE");
        Assert.Equal(LicenseState.Grace, m.State);
    }

    [Fact]
    public void AgedStore_ManagerStateIsExpired()
    {
        // Hard Expired requires lastOnlineCheck older than the 10-day grace window.
        var activateAt = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);
        var m1 = NewManager(activateAt, out _, out var store);
        m1.Activate("CORTEX-GRACE"); // token expired yesterday, lastOnlineCheck = activateAt

        // Re-open 20 days later: same store + key files, later clock.
        var later = activateAt.AddDays(20);
        var m2 = NewManager(later, out _, out _);
        m2.Refresh();
        Assert.Equal(LicenseState.Expired, m2.State);
    }
}
```

Note: `AgedStore` reuses the same `_dir`, so `m2` reads the `license.json` written by `m1.Activate` (whose `lastOnlineCheckUtc = activateAt`); 20 days later that exceeds the 10-day grace window → Expired. The `dev-license-key.json` persists too, so the stored token still verifies.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevBackendManagerIntegrationTests"`
Expected: FAIL to compile until Tasks 2-5 types exist; if run after them, the assertions drive the behavior. (In subagent order this runs last, so it should compile and pass.)

- [ ] **Step 3: No new implementation** — these tests exercise existing `LicenseManager` + the Task 2-5 types. If `AgedStore` fails, the bug is in a store's persistence (fix there), NOT in `LicenseManager`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevBackendManagerIntegrationTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Tests/Licensing/DevBackendManagerIntegrationTests.cs
git commit -m "$(cat <<'EOF'
test(licensing): integration — CORTEX-GRACE=>Grace, aged store=>Expired (no manager change)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 9: Full-suite regression + final build matrix

**Files:** none (verification only)

- [ ] **Step 1: Full test suite**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`
Expected: all green. New count = 696 + 9 (T2/T3) + 4 (T4) + 4 (T5) + 2 (T7) + 3 (T8) = **718 passed / 1 skipped / 0 failed**.

- [ ] **Step 2: Build all five Debug targets + one Release**

Run each; expected `Errori: 0`:
```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Release R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

- [ ] **Step 3: No commit** (verification only). Report the final suite count and confirm Release (the `#else` fail-closed path) built clean.

---

## Self-review notes

- **Spec coverage:** whitelist active/trial-14/grace (T2), node-lock + save-failure (T3), persisted JSON key + .bad recovery + N1 (T4), node-lock persistence (T5), Debug-real-gate/D4-suspend + Release-fail-closed (T6), localized gate message (T7), real-manager Grace vs hard-Expired (T8), full regression (T9). All spec sections covered, including the evaluation-spec corrections.
- **Type consistency:** `IDevKeyStore.LoadOrCreate()/PublicOnly()`, `IDevNodeLockStore.GetBoundFingerprint()/TryBind()` (bool), `DevLicenseBackend(keyStore, nodeLock[, nowUtc])` used identically T1-T8. Whitelist keys `CORTEX-ACTIVE-2026`/`CORTEX-TRIAL-14`/`CORTEX-GRACE` consistent T2↔T3↔T4↔T8. `ISystemClock` has `UtcNow`+`HighWaterMarkUtc` (matches the F1 change). Localization keys `license.gate_blocked`/`license.gate_suggestion` consistent T7.
- **No placeholders:** every code step shows full code; every run step shows the exact command + expected outcome. (T6 Step 2 explicitly instructs how to resolve the possible unused-field warning rather than leaving it vague.)
- **Scope guard:** `ILicenseBackend`, `LicenseManager`, `LicenseGate` logic, verifier, `FileLicenseStore`, `FakeLicenseBackend`, and the 696 existing tests untouched. `FakeLicenseBackend` no longer wired into the plugin (Release fail-closed, Debug uses DevLicenseBackend).
