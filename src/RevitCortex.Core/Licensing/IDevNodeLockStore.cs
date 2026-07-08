namespace RevitCortex.Core.Licensing;

/// <summary>
/// Dev/demo-only store binding a license key to the first machine fingerprint that
/// activated it. Simulates Keygen's node-lock. First-write-wins is enforced by the
/// backend, not here.
/// </summary>
public interface IDevNodeLockStore
{
    /// <summary>The fingerprint bound to this key, or null if never activated.</summary>
    string? GetBoundFingerprint(string licenseKey);

    /// <summary>Persist key -> fingerprint. Returns false if the write failed (the backend
    /// then fails activation, so a lock is never accepted without being persisted).</summary>
    bool TryBind(string licenseKey, string fingerprint);
}
