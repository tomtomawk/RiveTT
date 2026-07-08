using System;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// The persisted client-side state: the raw signed wire token (payload.sig) plus two
/// local grace-metadata timestamps. Immutable class (net48-safe, no record). Only the
/// token is trusted (via its signature); the timestamps are local hints that can only
/// SHORTEN grace (with anti-rollback), never extend it.
/// </summary>
public class StoredLicenseState
{
    public string Token { get; }
    public DateTime? LastOnlineCheckUtc { get; }
    public DateTime HighWaterMarkUtc { get; }

    public StoredLicenseState(string token, DateTime? lastOnlineCheckUtc, DateTime highWaterMarkUtc)
    {
        Token = token ?? "";
        LastOnlineCheckUtc = lastOnlineCheckUtc;
        HighWaterMarkUtc = highWaterMarkUtc;
    }
}

/// <summary>Persistence abstraction for the stored license state.</summary>
public interface ILicenseStore
{
    /// <summary>Returns the stored state, or null if none / unreadable (never throws).</summary>
    StoredLicenseState? Load();

    /// <summary>Persists the state, overwriting any previous one.</summary>
    void Save(StoredLicenseState state);
}

/// <summary>In-memory store for tests and dev.</summary>
public class InMemoryLicenseStore : ILicenseStore
{
    private StoredLicenseState? _state;

    public StoredLicenseState? Load() => _state;

    public void Save(StoredLicenseState state) => _state = state;
}
