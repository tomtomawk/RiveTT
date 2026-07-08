using System;
using System.Collections.Generic;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Deterministic license state machine (spec §4) plus a small stateful surface for the
/// gate and UI. Pure Evaluate() takes an already-verified token (null = signature failed
/// upstream OR nothing stored). Refresh() loads the store, verifies the wire token,
/// evaluates against clock+fingerprint+hwm, and caches State + display fields.
/// Fail-closed on validity (bad sig / fingerprint-not-subset / unknown state -> Invalid);
/// fail-open within grace (recent expiry -> Grace). Never touches I/O outside Refresh/Activate.
/// </summary>
public class LicenseManager
{
    public static readonly TimeSpan GraceWindow = TimeSpan.FromDays(10);
    public static readonly TimeSpan RollbackTolerance = TimeSpan.FromHours(1);

    private readonly ILicenseStore _store;
    private readonly IFingerprintProvider _fingerprint;
    private readonly LicenseTokenVerifier _verifier;
    private readonly ISystemClock _clock;
    private readonly ILicenseBackend _backend;

    private LicenseState _state = LicenseState.Invalid;
    private DateTime? _expiresAtUtc;
    private int _graceDaysRemaining;
    private string _licenseIdTruncated = "";

    public LicenseManager(
        ILicenseStore store,
        IFingerprintProvider fingerprint,
        LicenseTokenVerifier verifier,
        ISystemClock clock,
        ILicenseBackend backend)
    {
        _store = store;
        _fingerprint = fingerprint;
        _verifier = verifier;
        _clock = clock;
        _backend = backend;
    }

    public LicenseState State => _state;
    public DateTime? ExpiresAtUtc => _expiresAtUtc;
    public int GraceDaysRemaining => _graceDaysRemaining;
    public string LicenseIdTruncated => _licenseIdTruncated;

    /// <summary>Re-read the store + clock + fingerprint and recompute the cached state.</summary>
    public void Refresh()
    {
        LicenseToken? token = null;
        DateTime? lastCheck = null;
        var now = _clock.UtcNow;
        var hwm = now;

        var stored = SafeLoad();
        if (stored != null)
        {
            token = _verifier.Verify(stored.Token);
            lastCheck = stored.LastOnlineCheckUtc;
            hwm = stored.HighWaterMarkUtc > now ? stored.HighWaterMarkUtc : now;
        }

        var current = SafeFingerprint();
        _state = Evaluate(token, now, lastCheck, current, hwm);

        _expiresAtUtc = token?.ExpiresAtUtc;
        _licenseIdTruncated = Truncate(token?.LicenseId ?? "");
        _graceDaysRemaining = (_state == LicenseState.Grace && lastCheck.HasValue)
            ? Math.Max(0, (int)Math.Ceiling((GraceWindow - (now - lastCheck.Value)).TotalDays))
            : 0;
    }

    /// <summary>Activate via the backend, persist the wire token, and Refresh.</summary>
    public LicenseActivationResult Activate(string licenseKey)
    {
        var current = SafeFingerprint();
        var result = _backend.Activate(licenseKey ?? "", current);
        if (result.Success && result.Token != null)
        {
            var now = _clock.UtcNow;
            _store.Save(new StoredLicenseState(result.Token, now, now));
            Refresh();
        }
        return result;
    }

    public LicenseState Evaluate(
        LicenseToken? token,
        DateTime nowUtc,
        DateTime? lastOnlineCheckUtc,
        IReadOnlyList<string> currentFingerprint,
        DateTime highWaterMarkUtc)
    {
        // Point 1 (no token) + Point 2 (bad signature -> verifier returned null upstream).
        if (token == null)
            return LicenseState.Invalid;

        // Point 3: current fingerprint must be a SUPERSET of the token's hashes.
        if (!FingerprintIsSuperset(currentFingerprint, token.FingerprintHashes))
            return LicenseState.Invalid;

        // fix #3: an unknown state is never trusted, expired or not.
        if (!IsTrustedState(token.State))
            return LicenseState.Invalid;

        bool withinExpiry = nowUtc <= token.ExpiresAtUtc;
        if (withinExpiry)
        {
            return string.Equals(token.State, "trial", StringComparison.OrdinalIgnoreCase)
                ? LicenseState.Trial
                : LicenseState.Active;
        }

        // Expired. Point 8: rollback beyond tolerance revokes the offline lease.
        if (nowUtc < highWaterMarkUtc - RollbackTolerance)
            return LicenseState.Expired;

        // Point 6: grace anchored on the last online check (fix #2). Null anchor -> Expired.
        if (lastOnlineCheckUtc.HasValue &&
            (nowUtc - lastOnlineCheckUtc.Value) <= GraceWindow)
            return LicenseState.Grace;

        // Point 7.
        return LicenseState.Expired;
    }

    private static bool IsTrustedState(string state) =>
        string.Equals(state, "active", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "trial", StringComparison.OrdinalIgnoreCase);

    private static bool FingerprintIsSuperset(
        IReadOnlyList<string> current,
        IReadOnlyList<string> tokenHashes)
    {
        if (tokenHashes == null || tokenHashes.Count == 0)
            return false; // a real token always carries >= 1 hash
        var set = new HashSet<string>(current ?? new List<string>(), StringComparer.Ordinal);
        for (int i = 0; i < tokenHashes.Count; i++)
            if (!set.Contains(tokenHashes[i])) return false;
        return true;
    }

    private static string Truncate(string id)
    {
        if (string.IsNullOrEmpty(id)) return "";
        return id.Length <= 12 ? id : id.Substring(0, 8) + "…";
    }

    private StoredLicenseState? SafeLoad()
    {
        try { return _store?.Load(); } catch { return null; }
    }

    private IReadOnlyList<string> SafeFingerprint()
    {
        try { return _fingerprint?.GetHashedAttributes() ?? new List<string>(); }
        catch { return new List<string>(); }
    }
}
