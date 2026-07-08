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
