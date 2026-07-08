using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Licensing;

/// <summary>
/// The signed license payload as understood by the client (PAYLOAD-ONLY: no signature,
/// no wire token — the raw wire token lives in StoredLicenseState.Token). Immutable
/// class (net48-safe: no record/init). FromJson never throws (null on malformed input).
/// </summary>
public class LicenseToken
{
    public string LicenseId { get; }
    public string State { get; }
    public DateTime ExpiresAtUtc { get; }
    public int SeatLimit { get; }
    public IReadOnlyList<string> FingerprintHashes { get; }
    public DateTime IssuedAtUtc { get; }

    public LicenseToken(
        string licenseId,
        string state,
        DateTime expiresAtUtc,
        int seatLimit,
        IReadOnlyList<string> fingerprintHashes,
        DateTime issuedAtUtc)
    {
        LicenseId = licenseId ?? "";
        State = state ?? "";
        ExpiresAtUtc = expiresAtUtc;
        SeatLimit = seatLimit;
        FingerprintHashes = fingerprintHashes ?? new List<string>();
        IssuedAtUtc = issuedAtUtc;
    }

    public static LicenseToken? FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var o = JObject.Parse(json);

            var hashes = new List<string>();
            if (o["fingerprintHashes"] is JArray arr)
            {
                foreach (var t in arr)
                {
                    var s = (string?)t;
                    if (!string.IsNullOrEmpty(s)) hashes.Add(s!);
                }
            }

            return new LicenseToken(
                (string?)o["licenseId"] ?? "",
                (string?)o["state"] ?? "",
                ReadUtc(o["expiresAtUtc"]),
                o["seatLimit"] != null ? (int)o["seatLimit"]! : 0,
                hashes,
                ReadUtc(o["issuedAtUtc"]));
        }
        catch
        {
            return null;
        }
    }

    // fix #1: Newtonsoft materializes an ISO "…Z" string as a Date JValue. Read the Date
    // directly (NOT via (string?)) so the round-trip is correct; fall back to string parse
    // only when the token is not already a Date.
    private static DateTime ReadUtc(JToken? token)
    {
        if (token == null || token.Type == JTokenType.Null) return DateTime.MinValue;
        if (token.Type == JTokenType.Date)
            return ((DateTime)token).ToUniversalTime();

        var s = (string?)token;
        if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
        var dt = DateTime.Parse(
            s,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
    }
}
