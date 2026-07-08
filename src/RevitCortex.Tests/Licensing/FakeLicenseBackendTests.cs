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
