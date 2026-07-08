using System;
using System.Security.Cryptography;
using System.Text;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Verifies a signed license token and parses it into a <see cref="LicenseToken"/>.
///
/// Wire format: base64(payloadUtf8) + "." + base64(signature).
/// Signature: RSA-2048, SHA-256, PKCS#1 v1.5, over the RAW UTF-8 payload bytes
/// (segment 1 decoded from base64 VERBATIM — never re-serialized).
///
/// Cross-target: the public key is supplied as raw Modulus + Exponent and imported via
/// RSAParameters + ImportParameters. This avoids ImportSubjectPublicKeyInfo /
/// ImportRSAPublicKey, which DO NOT exist on net48 / netstandard2.0. Only RSA.Create,
/// ImportParameters and VerifyData are used — present on every target (R23-R27).
///
/// Any malformed/tampered/truncated/wrong-key/non-parseable input returns null; never throws.
/// </summary>
public class LicenseTokenVerifier
{
    private readonly RSAParameters _publicKey;

    public LicenseTokenVerifier(byte[] modulus, byte[] exponent)
    {
        // fix #10: clone the caller's buffers so later mutation can't affect verification.
        _publicKey = new RSAParameters
        {
            Modulus = (byte[])modulus.Clone(),
            Exponent = (byte[])exponent.Clone()
        };
    }

    public LicenseToken? Verify(string wireToken)
    {
        if (string.IsNullOrEmpty(wireToken)) return null;

        var dot = wireToken.IndexOf('.');
        if (dot <= 0 || dot >= wireToken.Length - 1) return null;

        var payloadB64 = wireToken.Substring(0, dot);
        var sigB64 = wireToken.Substring(dot + 1);

        byte[] payloadBytes;
        byte[] sigBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(payloadB64);
            sigBytes = Convert.FromBase64String(sigB64);
        }
        catch
        {
            return null; // not valid base64
        }

        bool valid;
        try
        {
            using (var rsa = RSA.Create())
            {
                rsa.ImportParameters(_publicKey);
                valid = rsa.VerifyData(
                    payloadBytes, sigBytes,
                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }
        catch
        {
            return null; // any crypto failure => invalid, never throw
        }

        if (!valid) return null;

        var json = Encoding.UTF8.GetString(payloadBytes);
        return LicenseToken.FromJson(json); // null if payload isn't valid token JSON
    }
}
