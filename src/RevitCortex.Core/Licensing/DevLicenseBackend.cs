using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// Dev/demo-only <see cref="ILicenseBackend"/> that mimics a real licensing authority
/// LOCALLY: a fixed key whitelist (active / trial-14d / grace), node-lock to the first
/// machine fingerprint, and a persisted signing keypair (via <see cref="IDevKeyStore"/>) so
/// tokens survive process restarts. Selected only under #if DEBUG in LicenseBootstrap;
/// Release is fail-closed (no backend). Wire format matches FakeLicenseBackend so
/// LicenseTokenVerifier round-trips. NOTE: CORTEX-GRACE mints a token expired yesterday;
/// under LicenseManager (lastOnlineCheck=now) that evaluates to Grace, not hard Expired —
/// hard Expired is only reachable via an aged stored license (see plan Task 8 fixture).
/// </summary>
public class DevLicenseBackend : ILicenseBackend
{
    private sealed class Plan
    {
        public string State = "active";
        public Func<DateTime, DateTime> Expiry = now => now.AddYears(1);
    }

    private static readonly Dictionary<string, Plan> Whitelist =
        new Dictionary<string, Plan>(StringComparer.Ordinal)
        {
            ["CORTEX-ACTIVE-2026"] = new Plan { State = "active", Expiry = n => n.AddYears(1) },
            ["CORTEX-TRIAL-14"]    = new Plan { State = "trial",  Expiry = n => n.AddDays(14) },
            ["CORTEX-GRACE"]       = new Plan { State = "active", Expiry = n => n.AddDays(-1) },
        };

    private readonly IDevKeyStore _keyStore;
    private readonly IDevNodeLockStore _nodeLock;
    private readonly Func<DateTime> _nowUtc;

    public DevLicenseBackend(IDevKeyStore keyStore, IDevNodeLockStore nodeLock)
        : this(keyStore, nodeLock, () => DateTime.UtcNow) { }

    public DevLicenseBackend(IDevKeyStore keyStore, IDevNodeLockStore nodeLock, Func<DateTime> nowUtc)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _nodeLock = nodeLock ?? throw new ArgumentNullException(nameof(nodeLock));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public LicenseActivationResult Activate(string licenseKey, IReadOnlyList<string> fingerprintHashes)
    {
        var key = (licenseKey ?? "").Trim();
        if (!Whitelist.TryGetValue(key, out var plan))
            return LicenseActivationResult.Fail("invalid license key");

        if (fingerprintHashes == null || fingerprintHashes.Count == 0)
            return LicenseActivationResult.Fail("no machine fingerprint available");

        // Node-lock enforced in Task 3; Task 2 only mints.
        var now = _nowUtc();
        return LicenseActivationResult.Ok(Mint(key, plan, now, fingerprintHashes));
    }

    public LicenseActivationResult Validate(string wireToken)
    {
        if (string.IsNullOrEmpty(wireToken) || wireToken.IndexOf('.') < 0)
            return LicenseActivationResult.Fail("malformed token");
        return LicenseActivationResult.Ok(wireToken);
    }

    private string Mint(string licenseKey, Plan plan, DateTime now, IReadOnlyList<string> fps)
    {
        var fpArray = new JArray();
        foreach (var h in fps) fpArray.Add(h);

        var payload = new JObject
        {
            ["licenseId"] = licenseKey,
            ["state"] = plan.State,
            ["expiresAtUtc"] = plan.Expiry(now).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["seatLimit"] = 1,
            ["fingerprintHashes"] = fpArray,
            ["issuedAtUtc"] = now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var payloadBytes = Encoding.UTF8.GetBytes(payload.ToString(Newtonsoft.Json.Formatting.None));
        using (var rsa = RSA.Create())
        {
            rsa.ImportParameters(_keyStore.LoadOrCreate());
            var sig = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Convert.ToBase64String(payloadBytes) + "." + Convert.ToBase64String(sig);
        }
    }
}
