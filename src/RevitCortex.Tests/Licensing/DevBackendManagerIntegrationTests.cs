using System;
using System.Collections.Generic;
using System.IO;
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

    private LicenseManager NewManager(DateTime now)
    {
        var keyStore = new FileDevKeyStore(Path.Combine(_dir, "dev-license-key.json"));
        var nodeLock = new FileDevNodeLockStore(Path.Combine(_dir, "dev-node-lock.json"));
        var backend = new DevLicenseBackend(keyStore, nodeLock, () => now);
        var pub = keyStore.PublicOnly();
        var verifier = new LicenseTokenVerifier(pub.Modulus!, pub.Exponent!);
        var store = new FileLicenseStore(Path.Combine(_dir, "license.json"));
        var fp = new FakeFingerprintProvider(new[] { "fp1" });
        return new LicenseManager(store, fp, verifier, new TestClock(now), backend);
    }

    [Fact]
    public void ActivateActiveKey_ManagerStateIsActive()
    {
        var now = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);
        var m = NewManager(now);
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
        var m = NewManager(now);
        m.Activate("CORTEX-GRACE");
        Assert.Equal(LicenseState.Grace, m.State);
    }

    [Fact]
    public void AgedStore_ManagerStateIsExpired()
    {
        // Hard Expired requires lastOnlineCheck older than the 10-day grace window.
        var activateAt = new DateTime(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);
        var m1 = NewManager(activateAt);
        m1.Activate("CORTEX-GRACE"); // token expired yesterday, lastOnlineCheck = activateAt

        // Re-open 20 days later: same store + key files, later clock.
        var later = activateAt.AddDays(20);
        var m2 = NewManager(later);
        m2.Refresh();
        Assert.Equal(LicenseState.Expired, m2.State);
    }
}
