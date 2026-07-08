using System.Collections.Generic;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Abstraction over the licensing authority (Fase 2: Keygen/Stripe; Fase 1:
/// <see cref="FakeLicenseBackend"/>). The client never trusts anything outside the RSA
/// signature carried inside the returned wire token.
/// </summary>
public interface ILicenseBackend
{
    /// <summary>
    /// Exchanges a license key + the current machine fingerprint hashes for a signed
    /// wire token (base64(payload).base64(sig)) verifiable by LicenseTokenVerifier.
    /// </summary>
    LicenseActivationResult Activate(string licenseKey, IReadOnlyList<string> fingerprintHashes);

    /// <summary>
    /// Re-checks an existing wire token (online-refresh path). Fase 1 echoes a parseable
    /// token; Fase 2 revalidates server-side.
    /// </summary>
    LicenseActivationResult Validate(string wireToken);
}
