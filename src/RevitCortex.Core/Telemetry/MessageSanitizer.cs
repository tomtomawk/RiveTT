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
/// Known residual limitation: a single word of ANY case — all-lowercase
/// ("strutture") or single-leading-cap ("Strutture") — with no digits, dots,
/// hyphens, or internal capitalization is shape-indistinguishable from a
/// template word like "Element" and will pass. A bare Italian workset/room/
/// type name is exactly this shape, and the spec explicitly lists such names
/// as forbidden data, so this gap is real, not hypothetical.
///
/// Because of that gap, TrySanitizeForTransmission carries a HARD CALLER
/// CONTRACT: it is only safe to call when the caller has ALREADY established
/// messageOrigin=templated (a structured CortexResult.Fail from RevitCortex
/// tool code) AND the template does NOT embed ex.Message or bare (unquoted)
/// interpolated names. It is NOT a general-purpose PII scrubber: feeding it a
/// raw Revit exception message, or a template that interpolates uncontrolled
/// model data as bare tokens, can produce a "safe" verdict on unsafe text.
/// See docs/superpowers/specs/2026-07-07-bug-telemetry-pipeline-paid-readiness-design.md
/// (messageOrigin amendment) for the gate the caller must implement first.
///
/// Non-ASCII / accented tokens fail closed by design: RxSafeWord is
/// ^[A-Za-z]+$, so "città", "Wände", "Pièce" never match and never transmit.
/// This component gates English templates only; a surviving accented token
/// signals uncontrolled localized text and is correctly rejected.
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

    /// <summary>Case-preserving strip used by ErrorReporter's pure-template
    /// pre-filter. Same patterns as Normalize but without the final ToLower.</summary>
    internal static string StripForTemplateCheck(string? message)
        => StripKnownPatterns(message);

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
