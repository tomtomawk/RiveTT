using System.Text.RegularExpressions;

namespace RevitCortex.Core.Telemetry;

/// <summary>
/// Strips potentially identifying content from failure messages.
/// Normalize() feeds the fingerprint (aggressive, lossy, stable).
/// TrySanitizeForTransmission() additionally applies a fail-closed verdict:
/// if any suspicious residue survives, NO text leaves the machine.
///
/// The verdict is deliberately an ALLOWLIST, not a blocklist of punctuation:
/// RevitCortex's own CortexResult.Fail templates ("Element {id} does not
/// exist...") are built from a small, developer-controlled vocabulary of
/// plain English structural words. Anything that survives stripping and does
/// NOT look like one of those plain words (a bare identifier, a proper noun,
/// a dotted/hyphenated token, digits fused with letters, mixed-case-in-the-
/// middle text) is treated as unproven residue -> fail closed. A blocklist of
/// characters like @ \ " ' would still let bare usernames ("mario.rossi"),
/// document titles ("TorreA"), or machine names ("DESKTOP-7F3K2A1") through
/// untouched, which is exactly the P1 leak this component exists to close.
///
/// Known residual limitation: a single capitalized word with no digits, dots,
/// hyphens, or internal caps (e.g. a bare Italian workset/room/type name like
/// "Strutture") is shape-indistinguishable from a template word like
/// "Element" and will pass. This class is the last gate for RevitCortex's own
/// fixed English CortexResult.Fail templates (see
/// docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md,
/// messageOrigin amendment) — it is not a general-purpose PII scrubber for
/// arbitrary uncontrolled text, and callers must not feed it raw Revit
/// exception messages or interpolated model data expecting a safe verdict.
/// </summary>
public static class MessageSanitizer
{
    private const string Placeholder = "_";

    // Order matters: longest/most-specific first. Revit UniqueId = GUID + 8 hex.
    private static readonly Regex RxRevitUniqueId = new Regex(
        @"\b[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}-[0-9a-fA-F]{8}\b", RegexOptions.Compiled);
    private static readonly Regex RxGuid = new Regex(
        @"\b[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b", RegexOptions.Compiled);
    private static readonly Regex RxUncPath = new Regex(@"\\\\[^\s""']+", RegexOptions.Compiled);
    private static readonly Regex RxDrivePath = new Regex(@"[A-Za-z]:\\[^\s""']*", RegexOptions.Compiled);
    private static readonly Regex RxEmail = new Regex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
    private static readonly Regex RxQuoted = new Regex("\"[^\"]*\"|'[^']*'|«[^»]*»", RegexOptions.Compiled);
    private static readonly Regex RxIfcToken = new Regex(@"\bIfc[A-Z][A-Za-z0-9]*\b", RegexOptions.Compiled);
    private static readonly Regex RxCompoundToken = new Regex(@"\b[A-Za-z]+_[A-Za-z0-9_]*\b", RegexOptions.Compiled);
    private static readonly Regex RxNumber = new Regex(@"\d+([.,]\d+)?", RegexOptions.Compiled);
    private static readonly Regex RxWhitespace = new Regex(@"\s+", RegexOptions.Compiled);

    // A token is provably safe only if it is a plain alphabetic word — the
    // shape of RevitCortex's own template vocabulary ("Element", "does",
    // "not", "exist", ...). Anything else that survived stripping (dotted
    // names, hyphenated codes, digits fused with letters, remaining
    // placeholders/punctuation) is unproven residue.
    private static readonly Regex RxSafeWord = new Regex(@"^[A-Za-z]+$", RegexOptions.Compiled);

    // A token with an internal capital letter (camelCase/PascalCase interior
    // caps) reads as an identifier or proper noun, not a sentence word, even
    // though it is all-alphabetic and would otherwise match RxSafeWord.
    private static readonly Regex RxInternalCaps = new Regex(@"^[A-Za-z][a-z]*[A-Z]", RegexOptions.Compiled);

    private static readonly Regex RxToken = new Regex(@"\S+", RegexOptions.Compiled);

    /// <summary>
    /// Aggressive, lossy, stable canonicalization for fingerprinting only.
    /// Never transmitted as-is; see <see cref="TrySanitizeForTransmission"/>.
    /// </summary>
    public static string Normalize(string? message)
    {
        return StripKnownPatterns(message).ToLowerInvariant();
    }

    /// <summary>
    /// Fail-closed verdict: returns true with safe text to transmit only if
    /// every token surviving stripping is a plain, unadorned English word.
    /// On any doubt, returns false and <paramref name="sanitized"/> is empty
    /// — no text of any kind leaves the machine.
    /// </summary>
    public static bool TrySanitizeForTransmission(string? message, out string sanitized)
    {
        sanitized = "";
        var stripped = StripKnownPatterns(message);
        if (stripped.Length == 0) return false;

        foreach (Match token in RxToken.Matches(stripped))
        {
            var word = token.Value;
            if (word == Placeholder) continue; // a redacted slot, not residue
            if (!RxSafeWord.IsMatch(word)) return false;   // punctuation/digits/dots/dashes left over
            if (RxInternalCaps.IsMatch(word)) return false; // PascalCase/camelCase identifier or proper noun
        }

        var n = RxWhitespace.Replace(stripped, " ").Trim().ToLowerInvariant();
        if (n.Length == 0) return false;
        sanitized = n.Length <= 200 ? n : n.Substring(0, 200);
        return true;
    }

    /// <summary>
    /// Applies every known-safe stripping pattern (paths, GUIDs, emails,
    /// quoted strings, IFC/compound tokens, numbers) but preserves case and
    /// does not lowercase — callers decide what to do with the result.
    /// </summary>
    private static string StripKnownPatterns(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        var s = message!;
        s = RxRevitUniqueId.Replace(s, Placeholder);
        s = RxGuid.Replace(s, Placeholder);
        s = RxUncPath.Replace(s, Placeholder);
        s = RxDrivePath.Replace(s, Placeholder);
        s = RxEmail.Replace(s, Placeholder);
        s = RxQuoted.Replace(s, Placeholder);
        s = RxIfcToken.Replace(s, Placeholder);
        s = RxCompoundToken.Replace(s, Placeholder);
        s = RxNumber.Replace(s, Placeholder);
        return RxWhitespace.Replace(s, " ").Trim();
    }
}
