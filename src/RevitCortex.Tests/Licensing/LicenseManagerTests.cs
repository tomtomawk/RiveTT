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

    private readonly LicenseManager Mgr;

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

        // Evaluate-only tests use this shared instance; the ctor now enforces non-null
        // collaborators, so these are real (but unexercised outside Evaluate) doubles.
        Mgr = new LicenseManager(
            new InMemoryLicenseStore(), new FakeFingerprintProvider(), _verifier, new SystemClock(), _backend);
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

    // GraceDaysRemaining is 0 when the state is Active (only non-zero in Grace).
    [Fact]
    public void GraceDaysRemaining_IsZero_WhenActive()
    {
        var store = new InMemoryLicenseStore();
        var fp = new FakeFingerprintProvider(MachineFp);
        var clock = new TestClock(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
        _backend.State = "active";
        _backend.FingerprintHashes = MachineFp;
        var manager = new LicenseManager(store, fp, _verifier, clock, _backend);
        var wire = _backend.Activate("K", MachineFp).Token!;
        store.Save(new StoredLicenseState(wire, clock.UtcNow, clock.UtcNow));

        manager.Refresh();

        Assert.Equal(LicenseState.Active, manager.State);
        Assert.Equal(0, manager.GraceDaysRemaining);
    }

    // Refresh with an empty store: state stays Invalid, display fields reset.
    [Fact]
    public void Refresh_NoStoredToken_StaysInvalid_DisplayReset()
    {
        var manager = new LicenseManager(
            new InMemoryLicenseStore(), new FakeFingerprintProvider(MachineFp), _verifier, new SystemClock(), _backend);

        manager.Refresh();

        Assert.Equal(LicenseState.Invalid, manager.State);
        Assert.Null(manager.ExpiresAtUtc);
        Assert.Equal("", manager.LicenseIdTruncated);
        Assert.Equal(0, manager.GraceDaysRemaining);
    }
}
