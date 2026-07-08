# Dev License Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Debug-only `DevLicenseBackend` that mimics a real licensing authority locally — key whitelist, node-lock, and a persisted RSA keypair so the license survives Revit restarts — plus a localized read-only gate message.

**Architecture:** New `DevLicenseBackend : ILicenseBackend` in Core, fed by two injected file-backed stores (`IDevKeyStore`, `IDevNodeLockStore`) whose Plugin implementations live in `RevitCortex.Plugin/Licensing`. `LicenseBootstrap` selects it under `#if DEBUG` only; the existing `FakeLicenseBackend` and the Release path stay untouched. The gate block message in `CortexRouter` moves to `Localization` (IT/EN).

**Tech Stack:** C# (netstandard2.0 Core / net48+net8 Plugin), xUnit, Newtonsoft.Json, System.Security.Cryptography (RSAParameters import/export — never ToXmlString, which is not net48-safe).

---

## Cross-group contracts (pinned — cited identically everywhere)

- **`IDevKeyStore`** (Core): `RSAParameters LoadOrCreate()` — returns a full (private) RSA keypair, generating + persisting it on first call, reloading it thereafter. `RSAParameters PublicOnly()` — the public half (Modulus+Exponent) of the same keypair, for building the verifier.
- **`IDevNodeLockStore`** (Core): `string? GetBoundFingerprint(string licenseKey)` — the fingerprint hash bound to this key, or null if never activated. `void Bind(string licenseKey, string fingerprint)` — records the binding (first-write-wins semantics enforced by the backend, not the store).
- **`DevLicenseBackend`** (Core) implements `ILicenseBackend` (unchanged): `Activate(string licenseKey, IReadOnlyList<string> fingerprintHashes)` and `Validate(string wireToken)`. Ctor: `DevLicenseBackend(IDevKeyStore keyStore, IDevNodeLockStore nodeLockStore)`.
- **Node-lock fingerprint choice:** the backend binds/compares against `fingerprintHashes[0]` (the first hash — MachineGuid in Fase 1). Empty list → activation fails ("no machine fingerprint available").
- **Whitelist:** internal static map. `CORTEX-ACTIVE-2026` → state `active`, expiry `nowUtc.AddYears(1)`. `CORTEX-TRIAL-14` → state `trial`, expiry `nowUtc.AddDays(14)`. `CORTEX-EXPIRED` → state `active`, expiry `nowUtc.AddDays(-1)`. Any other key → `Fail`. Expiry is computed from a `Func<DateTime> nowUtc` ctor param defaulting to `() => DateTime.UtcNow` so tests are deterministic.
- **Wire format (identical to FakeLicenseBackend):** `base64(payloadJsonUtf8) + "." + base64(pkcs1-sha256 signature over the SAME payload bytes)`. Payload keys: `licenseId`, `state`, `expiresAtUtc` (ISO `yyyy-MM-ddTHH:mm:ssZ`), `seatLimit`, `fingerprintHashes`, `issuedAtUtc`.
- **`LicenseActivationResult`** (existing): `Ok(string token)` / `Fail(string error)`; `.Success`, `.Token`, `.Error`.
- **`LicenseTokenVerifier`** (existing): ctor `(byte[] modulus, byte[] exponent)`, method `LicenseToken? Verify(string wireToken)`.

**Do NOT touch:** `ILicenseBackend`, `LicenseManager`, `LicenseGate` (decision logic), `LicenseTokenVerifier`, `FileLicenseStore`, `FakeLicenseBackend`, and the 696 existing tests. Release (`#else`) path unchanged.

---

## Task 1: `IDevKeyStore` + `IDevNodeLockStore` interfaces (Core)

**Files:**
- Create: `src/RevitCortex.Core/Licensing/IDevKeyStore.cs`
- Create: `src/RevitCortex.Core/Licensing/IDevNodeLockStore.cs`
- Test: `src/RevitCortex.Tests/Licensing/DevLicenseBackendTests.cs` (fakes live here; created in Task 2)

- [ ] **Step 1: Write the interfaces**

`IDevKeyStore.cs`:
```csharp
using System.Security.Cryptography;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Dev/demo-only store for the RSA keypair that signs demo license tokens. Persists the
/// FULL keypair so the same signing key survives process restarts (fix N1). Never used in
/// Release builds — a persisted private key must not ship. Cross-target: keypair is stored
/// as RSAParameters byte arrays (base64), never ToXmlString (not net48-safe).
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

    /// <summary>Record key -> fingerprint.</summary>
    void Bind(string licenseKey, string fingerprint);
}
```

- [ ] **Step 2: Build Core to verify it compiles**

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

    // Deterministic clock for expiry assertions.
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
        public string? GetBoundFingerprint(string k) => Map.TryGetValue(k, out var v) ? v : null;
        public void Bind(string k, string fp) => Map[k] = fp;
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
        var r = NewBackend().Activate("CORTEX-TRIAL-14", new List<string> { "fpA" });
        var t = Verifier().Verify(r.Token!);
        Assert.Equal("trial", t!.State);
        Assert.Equal(Now.AddDays(14), t.ExpiresAtUtc);
    }

    [Fact]
    public void Activate_ExpiredKey_MintsAlreadyExpiredToken()
    {
        var r = NewBackend().Activate("CORTEX-EXPIRED", new List<string> { "fpA" });
        var t = Verifier().Verify(r.Token!);
        Assert.Equal("active", t!.State);
        Assert.True(t.ExpiresAtUtc < Now);
    }

    [Fact]
    public void Activate_UnknownKey_Fails()
    {
        var r = NewBackend().Activate("NOPE", new List<string> { "fpA" });
        Assert.False(r.Success);
        Assert.NotNull(r.Error);
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
/// LOCALLY: a fixed key whitelist (active / trial-14d / expired), node-lock to the first
/// machine fingerprint, and a persisted signing keypair (via <see cref="IDevKeyStore"/>) so
/// tokens survive process restarts. Selected only under #if DEBUG in LicenseBootstrap;
/// Release keeps FakeLicenseBackend / (Fase 2) the real Keygen backend. Wire format matches
/// FakeLicenseBackend so LicenseTokenVerifier round-trips.
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
            ["CORTEX-EXPIRED"]     = new Plan { State = "active", Expiry = n => n.AddDays(-1) },
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

        // Node-lock will be enforced in Task 3; Task 2 only mints.
        var now = _nowUtc();
        var token = Mint(key, plan, now, fingerprintHashes);
        return LicenseActivationResult.Ok(token);
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

- [ ] **Step 5: Build R24 (net48) to confirm cross-target**

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
        var r2 = b.Activate("CORTEX-ACTIVE-2026", new List<string> { "fp1" });
        Assert.True(r2.Success);
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
```

- [ ] **Step 2: Run to verify the new tests fail**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevLicenseBackendTests"`
Expected: the 3 new tests FAIL (no binding happens yet); `DifferentFingerprint` fails because activation still succeeds.

- [ ] **Step 3: Add node-lock logic to `Activate`**

In `DevLicenseBackend.Activate`, replace the comment line `// Node-lock will be enforced in Task 3; Task 2 only mints.` and the two lines after it with:
```csharp
        var fp = fingerprintHashes[0];
        var bound = _nodeLock.GetBoundFingerprint(key);
        if (bound == null)
            _nodeLock.Bind(key, fp);
        else if (!string.Equals(bound, fp, StringComparison.Ordinal))
            return LicenseActivationResult.Fail("license already activated on another machine");

        var now = _nowUtc();
        var token = Mint(key, plan, now, fingerprintHashes);
        return LicenseActivationResult.Ok(token);
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~DevLicenseBackendTests"`
Expected: PASS (8 tests).

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

## Task 4: `FileDevKeyStore` — persisted RSA keypair (Plugin)

**Files:**
- Create: `src/RevitCortex.Plugin/Licensing/FileDevKeyStore.cs`
- Test: `src/RevitCortex.Tests/Licensing/FileDevKeyStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System;
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
        var store = new FileDevKeyStore(_path);
        var p = store.LoadOrCreate();
        Assert.NotNull(p.Modulus);
        Assert.NotNull(p.D); // private material present
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void LoadOrCreate_SecondInstance_ReturnsSameKey()
    {
        var first = new FileDevKeyStore(_path).LoadOrCreate();
        var second = new FileDevKeyStore(_path).LoadOrCreate(); // reload from disk
        Assert.Equal(first.Modulus, second.Modulus);
        Assert.Equal(first.D, second.D);
    }

    [Fact]
    public void SignedTokenSurvivesReload_VerifierAcceptsAcrossInstances()
    {
        // Simulates a Revit restart: instance A signs, instance B (fresh) verifies.
        var storeA = new FileDevKeyStore(_path);
        var backendA = new DevLicenseBackend(storeA, new InMemNodeLock(),
            () => new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc));
        var token = backendA.Activate("CORTEX-ACTIVE-2026",
            new System.Collections.Generic.List<string> { "fp1" }).Token!;

        var pubB = new FileDevKeyStore(_path).PublicOnly();
        var verifier = new LicenseTokenVerifier(pubB.Modulus!, pubB.Exponent!);
        Assert.NotNull(verifier.Verify(token));
    }

    private sealed class InMemNodeLock : IDevNodeLockStore
    {
        private readonly System.Collections.Generic.Dictionary<string, string> _m = new();
        public string? GetBoundFingerprint(string k) => _m.TryGetValue(k, out var v) ? v : null;
        public void Bind(string k, string fp) => _m[k] = fp;
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
/// gated to Debug builds by LicenseBootstrap. Any read failure regenerates the key.
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
        try
        {
            if (!File.Exists(_path)) return null;
            var o = JObject.Parse(File.ReadAllText(_path));
            return new RSAParameters
            {
                Modulus  = B64(o, "Modulus"),
                Exponent = B64(o, "Exponent"),
                D        = B64(o, "D"),
                P        = B64(o, "P"),
                Q        = B64(o, "Q"),
                DP       = B64(o, "DP"),
                DQ       = B64(o, "DQ"),
                InverseQ = B64(o, "InverseQ"),
            };
        }
        catch { return null; }
    }

    private void Save(RSAParameters p)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var o = new JObject
        {
            ["Modulus"]  = Conv(p.Modulus),
            ["Exponent"] = Conv(p.Exponent),
            ["D"]        = Conv(p.D),
            ["P"]        = Conv(p.P),
            ["Q"]        = Conv(p.Q),
            ["DP"]       = Conv(p.DP),
            ["DQ"]       = Conv(p.DQ),
            ["InverseQ"] = Conv(p.InverseQ),
        };
        File.WriteAllText(_path, o.ToString(Newtonsoft.Json.Formatting.Indented));
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
Expected: PASS (3 tests).

- [ ] **Step 5: Build R24**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 6: Commit**

```bash
git add src/RevitCortex.Plugin/Licensing/FileDevKeyStore.cs src/RevitCortex.Tests/Licensing/FileDevKeyStoreTests.cs
git commit -m "$(cat <<'EOF'
feat(licensing): FileDevKeyStore — persisted RSA keypair for dev backend (fix N1)

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
    {
        Assert.Null(new FileDevNodeLockStore(_path).GetBoundFingerprint("K"));
    }

    [Fact]
    public void Bind_ThenGet_ReturnsFingerprint()
    {
        var s = new FileDevNodeLockStore(_path);
        s.Bind("K", "fp1");
        Assert.Equal("fp1", s.GetBoundFingerprint("K"));
    }

    [Fact]
    public void Bind_PersistsAcrossInstances()
    {
        new FileDevNodeLockStore(_path).Bind("K", "fp1");
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
/// File-backed <see cref="IDevNodeLockStore"/>: a JSON object mapping license key ->
/// bound fingerprint. Dev/demo only. A missing/corrupt file is treated as empty; a bad
/// write is swallowed (demo must never crash Revit).
/// </summary>
public class FileDevNodeLockStore : IDevNodeLockStore
{
    private readonly string _path;

    public FileDevNodeLockStore(string path) { _path = path; }

    public string? GetBoundFingerprint(string licenseKey)
    {
        var map = Load();
        return (string?)map[licenseKey];
    }

    public void Bind(string licenseKey, string fingerprint)
    {
        try
        {
            var map = Load();
            map[licenseKey] = fingerprint;
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, map.ToString(Newtonsoft.Json.Formatting.Indented));
        }
        catch { /* demo store must never throw */ }
    }

    private JObject Load()
    {
        try { return File.Exists(_path) ? JObject.Parse(File.ReadAllText(_path)) : new JObject(); }
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

## Task 6: Wire `DevLicenseBackend` into `LicenseBootstrap` under `#if DEBUG`

**Files:**
- Modify: `src/RevitCortex.Plugin/Licensing/LicenseBootstrap.cs`

**Context:** The current non-dev branch (lines ~43-58) builds `store`, `fingerprint`, `clock`, `verifier`, `backend`, `manager`. We wrap only the backend + verifier selection in `#if DEBUG`. No test here — this is compile-time wiring, validated by both builds. (`LicenseBootstrap` has no unit test today; behavior is exercised by the live smoke.)

- [ ] **Step 1: Replace the verifier+backend construction lines**

In `LicenseBootstrap.Init`, find:
```csharp
            var verifier = new LicenseTokenVerifier(EmbeddedPublicKey.Modulus!, EmbeddedPublicKey.Exponent!);
            var backend = new FakeLicenseBackend(_fakeKey);
            var manager = new LicenseManager(store, fingerprint, verifier, clock, backend);
```
Replace with:
```csharp
#if DEBUG
            // Dev/demo backend: whitelist + node-lock + persisted keypair (survives restart).
            // Signer and verifier share the SAME persisted keypair so tokens round-trip.
            var keyStore = new FileDevKeyStore(System.IO.Path.Combine(env.RootFolder, "dev-license-key.json"));
            var nodeLock = new FileDevNodeLockStore(System.IO.Path.Combine(env.RootFolder, "dev-node-lock.json"));
            var devPub = keyStore.PublicOnly();
            var verifier = new LicenseTokenVerifier(devPub.Modulus!, devPub.Exponent!);
            ILicenseBackend backend = new DevLicenseBackend(keyStore, nodeLock);
#else
            var verifier = new LicenseTokenVerifier(EmbeddedPublicKey.Modulus!, EmbeddedPublicKey.Exponent!);
            ILicenseBackend backend = new FakeLicenseBackend(_fakeKey);
#endif
            var manager = new LicenseManager(store, fingerprint, verifier, clock, backend);
```

- [ ] **Step 2: Build R25 (Debug — takes the #if DEBUG path)**

Run: `dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 3: Build R24 (Debug — same path, net48)**

Run: `dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0`

- [ ] **Step 4: Build Release to confirm the #else path still compiles**

Run: `dotnet build -c "Release R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj`
Expected: `Errori: 0` (FakeLicenseBackend path intact).

- [ ] **Step 5: Commit**

```bash
git add src/RevitCortex.Plugin/Licensing/LicenseBootstrap.cs
git commit -m "$(cat <<'EOF'
feat(licensing): select DevLicenseBackend under #if DEBUG (Release path unchanged)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 7: Localize the gate block message (Plugin)

**Files:**
- Modify: `src/RevitCortex.Plugin/UI/Localization.cs`
- Modify: `src/RevitCortex.Plugin/CortexRouter.cs:193-196`
- Test: `src/RevitCortex.Tests/Router/` — see Step 1 for the exact new test file

**Context:** `IsToolReadOnly` and the gate guard are in `CortexRouter`. `Localization` is in the same assembly. We add two keys and swap the hard-coded strings.

**IMPORTANT — locale trap:** `Localization.DetectLocale()` falls back to `CultureInfo.CurrentUICulture` when no Revit `UIApplication` is present. On an Italian machine the tests would run under locale "it", so asserting on English words ("read-only") would fail for the wrong reason. The tests must be **locale-independent**: assert that (a) the resolved string is NOT the raw key (proves a translation was found) and (b) it interpolates the tool name (`{0}` → `create_level`, same in every locale).

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
        // Locale-independent: proves the key resolves to a real (translated) string that
        // embeds the tool name, without depending on the machine's UI language.
        var msg = Localization.T("license.gate_blocked", "create_level");
        Assert.NotEqual("license.gate_blocked", msg);          // a translation exists
        Assert.Contains("create_level", msg);                  // {0} was interpolated
    }

    [Fact]
    public void GateSuggestionKey_IsTranslated()
    {
        var s = Localization.T("license.gate_suggestion");
        Assert.NotEqual("license.gate_suggestion", s);         // a translation exists
        Assert.NotEqual("", s);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexRouterLicenseMessageTests"`
Expected: FAIL — `T("license.gate_blocked", "create_level")` returns the raw key `"license.gate_blocked"` (no interpolation), so both asserts fail.

- [ ] **Step 3: Add the two keys to `Localization.cs`**

Immediately after the `["license.expired_hint"]` entry (before the closing `};` of the table), add:
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

- [ ] **Step 5: Run the new test + a broad licensing regression**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25" --filter "FullyQualifiedName~CortexRouterLicense"`
Expected: PASS (the new 2 tests + the existing `CortexRouterLicenseGateTests` still green — the gate still returns PermissionDenied, only the message text changed).

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

## Task 8: Full-suite regression + final build matrix

**Files:** none (verification only)

- [ ] **Step 1: Full test suite**

Run: `dotnet test src/RevitCortex.Tests/RevitCortex.Tests.csproj -c "Debug R25"`
Expected: all green. New count = 696 + 8 (Task 2/3) + 3 (Task 4) + 4 (Task 5) + 2 (Task 7) = **713 passed / 1 skipped / 0 failed**.

- [ ] **Step 2: Build all five Revit targets + Release**

Run each; expected `Errori: 0`:
```bash
dotnet build -c "Debug R23" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R24" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R26" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Debug R27" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
dotnet build -c "Release R25" src/RevitCortex.Plugin/RevitCortex.Plugin.csproj
```

- [ ] **Step 3: No commit** (verification only). Report the final suite count and confirm Release (the `#else` path) built clean.

---

## Self-review notes

- **Spec coverage:** whitelist (T2), node-lock (T3), persisted key/N1 (T4+T6), node-lock persistence (T5), Debug-only wiring + Release-unchanged (T6), localized read-only gate message (T7), full regression (T8). All spec sections covered.
- **Type consistency:** `IDevKeyStore.LoadOrCreate()/PublicOnly()`, `IDevNodeLockStore.GetBoundFingerprint()/Bind()`, `DevLicenseBackend(keyStore, nodeLock[, nowUtc])` used identically across T1-T6. Whitelist keys `CORTEX-ACTIVE-2026`/`CORTEX-TRIAL-14`/`CORTEX-EXPIRED` consistent T2↔T3↔T4. Localization keys `license.gate_blocked`/`license.gate_suggestion` consistent T7.
- **No placeholders:** every code step shows full code; every run step shows the exact command + expected outcome.
- **Scope guard:** `ILicenseBackend`, `LicenseManager`, `LicenseGate` logic, verifier, `FileLicenseStore`, `FakeLicenseBackend`, and the 696 existing tests are untouched; only the `#else` selection sits beside the new `#if DEBUG` path.
