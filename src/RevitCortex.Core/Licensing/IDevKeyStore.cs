using System.Security.Cryptography;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Dev/demo-only store for the RSA keypair that signs demo license tokens. Persists the
/// FULL keypair so the same signing key survives process restarts (fix N1). Never used in
/// Release builds — a persisted private key must not ship. Cross-target: stored as
/// RSAParameters byte arrays (base64 JSON), never ToXmlString (not net48-safe).
/// </summary>
public interface IDevKeyStore
{
    /// <summary>Load the persisted keypair, or generate + persist one on first call.</summary>
    RSAParameters LoadOrCreate();

    /// <summary>Public half (Modulus+Exponent) of the same keypair, for the verifier.</summary>
    RSAParameters PublicOnly();
}
