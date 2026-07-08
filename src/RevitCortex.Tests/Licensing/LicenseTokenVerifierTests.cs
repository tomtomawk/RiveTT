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
