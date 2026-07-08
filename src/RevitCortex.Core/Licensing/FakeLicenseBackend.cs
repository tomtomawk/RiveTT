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
