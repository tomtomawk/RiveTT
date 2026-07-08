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
