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
