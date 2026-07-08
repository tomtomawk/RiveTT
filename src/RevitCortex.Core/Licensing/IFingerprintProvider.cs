using System.Collections.Generic;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Returns the machine fingerprint as a flat list of independently SHA-256-hashed
/// attributes (Fase 1: just MachineGuid). The real Windows collector lives in the
/// Plugin (registry). A missing/unavailable attribute is simply omitted, never a
/// placeholder. Core only depends on this contract.
/// </summary>
public interface IFingerprintProvider
{
    IReadOnlyList<string> GetHashedAttributes();
}

/// <summary>Test/dev provider: returns a fixed set of hashes.</summary>
public class FakeFingerprintProvider : IFingerprintProvider
{
    private readonly List<string> _hashes;

    public FakeFingerprintProvider()
    {
        _hashes = new List<string>();
    }

    public FakeFingerprintProvider(IEnumerable<string> hashes)
    {
        _hashes = new List<string>(hashes);
    }

    public IReadOnlyList<string> GetHashedAttributes() => _hashes;
}
