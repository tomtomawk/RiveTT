using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using RiveTT.Core.Results;

namespace RiveTT.Core.Security;

/// <summary>
/// Regex filter over the C# source of send_code_to_revit. Strips comments and string
/// literals BEFORE matching, decodes identifier escapes, and blocks reflection-based
/// detours (Type.GetType, Activator.CreateInstance, MethodInfo.Invoke) that the
/// substring-only V1 missed.
///
/// WHAT THIS IS NOT: a security boundary. It matches text, so the space of equivalent
/// rewritings is not closed by construction, and the Revit API it deliberately allows
/// writes to disk (Document.SaveAs, the exports) and destroys model data
/// (Document.Delete) on its own. No pattern list can forbid that without forbidding
/// the tool itself.
///
/// It catches the mistake and the obvious reach. The actual boundaries, in order: the
/// ribbon write lock, this tool's dryRun-by-default, and the audit entry carrying the
/// code and its SHA-256. Do not document it as more than that - an operator who
/// believes the snippet is contained will run one they have not read.
/// </summary>
public static class CodeSandboxV2
{
    private static readonly string[] ProhibitedNamespaces = new[]
    {
        "System.IO",
        "System.Net",
        "System.Diagnostics.Process",
        "Microsoft.Win32",
        "System.Reflection",   // covers .Emit too; blocks reflection-based sandbox escapes
        "System.Runtime.InteropServices",
    };

    // Patterns checked against the comments/strings-stripped form of the code.
    // String literals collapse to whitespace here, which is what we want for namespace
    // and identifier matching but defeats us when we need to know whether a call has
    // an argument — see ReflectionWithArgumentPatterns below for those cases.
    private static readonly Regex[] ProhibitedPatterns = new[]
    {
        new Regex(@"\bProcess\s*\.\s*Start\b", RegexOptions.Compiled),
        new Regex(@"\b(File|Directory|Path)\s*\.\s*(Read|Write|Delete|Move|Copy|Create|Exists|Open|Append|GetFiles|GetDirectories)\w*", RegexOptions.Compiled),
        new Regex(@"\b(WebClient|HttpClient|WebRequest|HttpWebRequest|TcpClient|Socket)\b", RegexOptions.Compiled),
        new Regex(@"\bRegistry(Key)?\s*\.\s*(Open|Get|Set|Create|Delete)\b", RegexOptions.Compiled),
        new Regex(@"\bEnvironment\s*\.\s*(Exit|SetEnvironmentVariable|GetEnvironmentVariable|GetEnvironmentVariables|GetFolderPath|GetCommandLineArgs|ExpandEnvironmentVariables|MachineName|UserName|UserDomainName|CurrentDirectory|SystemDirectory|ProcessPath|StackTrace)\b", RegexOptions.Compiled),
        new Regex(@"\bAssembly\s*\.\s*(Load|LoadFrom|LoadFile)\b", RegexOptions.Compiled),
        // Reflection bypasses (no-arg / fixed-form)
        new Regex(@"\bType\s*\.\s*GetType\b", RegexOptions.Compiled),
        new Regex(@"\bActivator\s*\.\s*CreateInstance\b", RegexOptions.Compiled),
        new Regex(@"\bMethodInfo\s*\.\s*Invoke\b", RegexOptions.Compiled),
        // typeof(X).<reflection-accessor> — the typeof() makes Type.GetType unnecessary
        new Regex(@"\btypeof\s*\([^)]+\)\s*\.\s*(GetMethod|GetField|GetProperty|GetMember|GetConstructor|InvokeMember)\b", RegexOptions.Compiled),
        // dynamic keyword opens late-bound dispatch — bypasses static pattern matching entirely
        new Regex(@"\bdynamic\b", RegexOptions.Compiled),
        // Reflection enumerators (zero-arg, plural) — the entry point for "enumerate members then
        // Invoke one" escapes. Distinct from the deliberately-allowed singular obj.GetType() and
        // type.GetMethod("name") (the latter is caught by ReflectionWithArgumentPatterns when abused).
        new Regex(@"\.\s*(GetTypes|GetMethods|GetConstructors|GetMembers|GetFields|GetProperties|GetInterfaces|GetNestedTypes|GetRuntimeMethods|GetRuntimeFields|GetRuntimeProperties)\s*\(", RegexOptions.Compiled),
        // Any reflective invoke on a value (m.Invoke(...), ctor.Invoke(...), del.DynamicInvoke(...)).
        // The earlier \bMethodInfo\.Invoke\b literal only caught the class-name form, not a variable.
        new Regex(@"\.\s*(Invoke|DynamicInvoke)\s*\(", RegexOptions.Compiled),
        // Assembly acquisition — the root of a reflection walk over loaded types.
        new Regex(@"\b(GetExecutingAssembly|GetCallingAssembly|GetEntryAssembly)\b", RegexOptions.Compiled),
    };

    // Patterns checked against the ORIGINAL code (string literals NOT stripped). We need this
    // to distinguish `obj.GetType()` (harmless zero-arg type check) from `obj.GetType("ns.T")`
    // (reflection bypass via Assembly.GetType / typeof(X).Module.GetType / instance.GetType).
    // After stripping, both look like `obj.GetType(...whitespace...)` and we can't tell them apart.
    private static readonly Regex[] ReflectionWithArgumentPatterns = new[]
    {
        // .GetType( <argument> ) — covers Assembly.GetType("..."), .GetType(name),
        // .GetType(ns + "." + cls). Excludes .GetType() (zero-arg, harmless).
        //
        // The first argument character is [^\s)], not \S: \S also matches the closing
        // parenthesis, so `log.Add(c.GetType().Name)` read as `.GetType(` + `)` + `.Name`
        // + the `)` of Add, and a harmless type-name log line was refused as reflection
        // (field report of 2026-09-24, bug 1.4).
        new Regex(@"\.\s*GetType\s*\(\s*[^\s)][^)]*\)", RegexOptions.Compiled),
        // .GetMethod( <argument> ) etc. — same rationale; excludes zero-arg overloads.
        new Regex(@"\.\s*(GetMethod|GetField|GetProperty|GetMember|GetConstructor|InvokeMember)\s*\(\s*[^\s)][^)]*\)", RegexOptions.Compiled),
    };

    // Calls that act OUTSIDE the model and therefore survive the rollback that makes
    // transactionMode "readonly" safe: files written (exports, saves, prints), documents
    // opened or closed, central-model traffic, UI navigation. A model change needs no
    // entry here: readonly runs inside a TransactionGroup that is always rolled back.
    private static readonly Regex ReadOnlyScriptForbiddenCall = new(
        @"\.\s*(Save|SaveAs|SaveCloudModel|SaveAsCloudModel|SaveLocalSharedModel|SaveToProjectAsImage|" +
        @"Close|Export|ExportImage|Print|SubmitPrint|SynchronizeWithCentral|ReloadLatest|Reload|ReloadFrom|" +
        @"RelinquishOwnership|EditFamily|OpenDocumentFile|OpenAndActivateDocument|OpenIFCDocument|" +
        @"NewProjectDocument|NewFamilyDocument|NewProjectTemplateDocument|PostCommand|RequestViewChange)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ActiveViewAssignment =
        new(@"\bActiveView\s*=(?!=)", RegexOptions.Compiled);

    /// <summary>
    /// Extra check for <c>transactionMode: "readonly"</c>, the one mode the router lets
    /// through while the ribbon lock is closed. The mode's guarantee over the MODEL is the
    /// always-rolled-back TransactionGroup, not this text match; this refuses the calls a
    /// rollback cannot undo. Same caveat as <see cref="Validate"/>: a text filter, not a
    /// boundary. Returns null when the script may run.
    /// </summary>
    public static RiveTTResult<object>? ValidateReadOnlyScript(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var cleaned = StripCommentsAndStrings(DecodeIdentifierEscapes(code));

        var violations = new List<string>();
        foreach (Match match in ReadOnlyScriptForbiddenCall.Matches(cleaned))
        {
            var call = match.Groups[1].Value + "(...)";
            if (!violations.Contains(call)) violations.Add(call);
        }
        if (ActiveViewAssignment.IsMatch(cleaned)) violations.Add("ActiveView = ...");

        if (violations.Count == 0) return null;
        return RiveTTResult<object>.Fail(
            RiveTTErrorCode.PermissionDenied,
            $"transactionMode \"readonly\" refuses calls that act outside the model: {string.Join(", ", violations)}",
            suggestion: "readonly rolls back every model change, but it cannot undo a file written, a document " +
                        "opened or closed, or a view switched. Remove these calls, or run the script with " +
                        "transactionMode \"auto\" once the ribbon lock is open.");
    }

    public static RiveTTResult<object>? Validate(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        // C# allows \uXXXX escapes INSIDE identifiers. Write the I of System.IO as
        // \u0049 and the i of File as \u0069 and the compiler still reads
        // System.IO.File, while the patterns below match neither "System.IO" nor
        // "File.". Decoding first makes every pattern see what the compiler sees. This
        // closes the demonstrated form; it does not make the matcher complete - see the
        // class comment on what this filter is and is not.
        code = DecodeIdentifierEscapes(code);

        var cleaned = StripCommentsAndStrings(code);
        // Collapse whitespace around member-access dots so a prohibited namespace cannot be
        // smuggled past the substring check as "System . IO" or "System.\n  IO" (C2 hardening).
        var normalized = Regex.Replace(cleaned, @"\s*\.\s*", ".");
        var violations = new List<string>();

        foreach (var ns in ProhibitedNamespaces)
        {
            if (normalized.Contains(ns))
                violations.Add(ns);
        }

        foreach (var regex in ProhibitedPatterns)
        {
            var match = regex.Match(cleaned);
            if (match.Success)
                violations.Add(match.Value);
        }

        // Reflection-with-argument patterns must match the ORIGINAL code so we can tell
        // `obj.GetType()` (allowed) from `obj.GetType("System.IO.File")` (blocked).
        foreach (var regex in ReflectionWithArgumentPatterns)
        {
            var match = regex.Match(code);
            if (match.Success)
                violations.Add(match.Value);
        }

        if (violations.Count == 0) return null;

        return RiveTTResult<object>.Fail(
            RiveTTErrorCode.PermissionDenied,
            $"Code contains prohibited operations: {string.Join(", ", violations)}",
            suggestion: "send_code_to_revit is restricted to Revit API operations. "
                + "File I/O, network, process spawning, registry, and reflection bypasses are not allowed.");
    }

    /// <summary>
    /// Decodes <c>\uXXXX</c> and <c>\UXXXXXXXX</c> to the characters they denote, so an
    /// identifier whose letters were written as escapes is matched as the plain word.
    ///
    /// Applied to the WHOLE source, string literals included. That is deliberate and it
    /// is the safe direction: inside a literal the escape already meant that character,
    /// so decoding changes nothing semantically, and literals are blanked out by
    /// <see cref="StripCommentsAndStrings"/> before matching anyway. The one visible
    /// effect is on <see cref="ReflectionWithArgumentPatterns"/>, which reads the
    /// original text — and there, seeing the decoded form is exactly what is wanted.
    ///
    /// Escapes are decoded to a single character, so offsets shift; nothing downstream
    /// reports a source position, only the matched text.
    /// </summary>
    public static string DecodeIdentifierEscapes(string code)
    {
        if (code.IndexOf('\\') < 0) return code;

        var sb = new StringBuilder(code.Length);
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\\' && i + 1 < code.Length &&
                (code[i + 1] == 'u' || code[i + 1] == 'U'))
            {
                var digits = code[i + 1] == 'u' ? 4 : 8;
                if (i + 2 + digits <= code.Length &&
                    TryParseHex(code.AsSpan(i + 2, digits), out var codePoint) &&
                    codePoint <= 0x10FFFF)
                {
                    sb.Append(char.ConvertFromUtf32((int)codePoint));
                    i += 1 + digits;
                    continue;
                }
            }
            sb.Append(code[i]);
        }
        return sb.ToString();
    }

    private static bool TryParseHex(ReadOnlySpan<char> span, out uint value)
    {
        value = 0;
        foreach (var c in span)
        {
            uint digit;
            if (c >= '0' && c <= '9') digit = (uint)(c - '0');
            else if (c >= 'a' && c <= 'f') digit = (uint)(c - 'a' + 10);
            else if (c >= 'A' && c <= 'F') digit = (uint)(c - 'A' + 10);
            else return false;
            value = (value << 4) | digit;
        }
        // Surrogate halves are not valid on their own for ConvertFromUtf32.
        return value < 0xD800 || value > 0xDFFF;
    }

    /// <summary>
    /// Replace every comment and string literal with whitespace of the same length,
    /// preserving line numbers and non-string source structure. Not a full C# lexer —
    /// good enough to defeat comment/string-based evasion of the pattern matcher.
    ///
    /// The HOLES of an interpolated string ($"...{expr}...", $@"", raw $"""...""") are code,
    /// and are kept as code. They used to be blanked with the rest of the literal, so
    /// $"{System.IO.File.ReadAllText(p)}" passed Validate and $"{document.Export(...)}" passed
    /// the readonly check (review of 0.6.0). Nested strings inside a hole are handled
    /// recursively.
    /// </summary>
    public static string StripCommentsAndStrings(string code)
    {
        var sb = new StringBuilder(code.Length);
        var i = 0;
        ScanCode(code, ref i, sb, stopAtUnmatchedBrace: false);
        // A hole left open at the end of the input: copy what remains as code.
        while (sb.Length < code.Length) sb.Append(code[sb.Length]);
        return sb.ToString();
    }

    private static void Blank(StringBuilder sb, char c) => sb.Append(c == '\n' ? '\n' : ' ');

    /// <summary>
    /// Copies code, blanking comments and literal text. With
    /// <paramref name="stopAtUnmatchedBrace"/> (inside an interpolation hole) it returns on
    /// the '}' that closes the hole, without consuming it.
    /// </summary>
    private static void ScanCode(string code, ref int i, StringBuilder sb, bool stopAtUnmatchedBrace)
    {
        var depth = 0;
        while (i < code.Length)
        {
            var c = code[i];
            var next = i + 1 < code.Length ? code[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                while (i < code.Length && code[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }
            if (c == '/' && next == '*')
            {
                sb.Append("  ");
                i += 2;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/')) { Blank(sb, code[i]); i++; }
                if (i + 1 < code.Length) { sb.Append("  "); i += 2; }
                else while (i < code.Length) { Blank(sb, code[i]); i++; }
                continue;
            }

            if (c == '$' || c == '@' || c == '"')
            {
                var j = i;
                var dollars = 0;
                var verbatim = false;
                while (j < code.Length && (code[j] == '$' || code[j] == '@'))
                {
                    if (code[j] == '$') dollars++; else verbatim = true;
                    j++;
                }
                if (j < code.Length && code[j] == '"')
                {
                    for (; i < j; i++) sb.Append(' ');
                    ScanString(code, ref i, sb, dollars, verbatim);
                    continue;
                }
            }

            if (c == '\'')
            {
                sb.Append(' ');
                i++;
                while (i < code.Length && code[i] != '\'' && code[i] != '\n')
                {
                    if (code[i] == '\\' && i + 1 < code.Length) { sb.Append("  "); i += 2; continue; }
                    sb.Append(' ');
                    i++;
                }
                if (i < code.Length && code[i] == '\'') { sb.Append(' '); i++; }
                continue;
            }

            if (stopAtUnmatchedBrace)
            {
                if (c == '{') depth++;
                else if (c == '}')
                {
                    if (depth == 0) return;
                    depth--;
                }
            }

            sb.Append(c);
            i++;
        }
    }

    /// <summary>A string literal starting at code[i] == '"', prefix already consumed.</summary>
    private static void ScanString(string code, ref int i, StringBuilder sb, int dollars, bool verbatim)
    {
        var quotes = 0;
        while (i + quotes < code.Length && code[i + quotes] == '"') quotes++;

        // A verbatim string never opens a raw literal: @""" is a quote escaped inside @"...".
        if (quotes >= 3 && !verbatim)
        {
            // Raw string literal: ends at a run of the same number of quotes.
            for (var k = 0; k < quotes; k++) sb.Append(' ');
            i += quotes;
            while (i < code.Length)
            {
                if (code[i] == '"')
                {
                    var run = 0;
                    while (i + run < code.Length && code[i + run] == '"') run++;
                    for (var k = 0; k < run; k++) sb.Append(' ');
                    i += run;
                    if (run >= quotes) return;
                    continue;
                }
                if (dollars > 0 && code[i] == '{')
                {
                    var run = 0;
                    while (i + run < code.Length && code[i + run] == '{') run++;
                    for (var k = 0; k < run; k++) sb.Append(' ');
                    i += run;
                    if (run >= dollars)
                    {
                        ScanCode(code, ref i, sb, stopAtUnmatchedBrace: true);
                        for (var k = 0; k < dollars && i < code.Length && code[i] == '}'; k++) { sb.Append(' '); i++; }
                    }
                    continue;
                }
                Blank(sb, code[i]);
                i++;
            }
            return;
        }

        if (quotes == 2 && !(verbatim && i + 2 < code.Length && code[i + 2] == '"'))
        {
            // "" : an empty string.
            sb.Append("  ");
            i += 2;
            return;
        }

        sb.Append(' ');
        i++;
        while (i < code.Length)
        {
            var c = code[i];
            var next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (!verbatim && c == '\\' && i + 1 < code.Length) { sb.Append("  "); i += 2; continue; }
            if (verbatim && c == '"' && next == '"') { sb.Append("  "); i += 2; continue; }
            if (c == '"') { sb.Append(' '); i++; return; }
            if (dollars > 0 && c == '{')
            {
                if (next == '{') { sb.Append("  "); i += 2; continue; }
                sb.Append(' ');
                i++;
                ScanCode(code, ref i, sb, stopAtUnmatchedBrace: true);
                if (i < code.Length && code[i] == '}') { sb.Append(' '); i++; }
                continue;
            }
            if (dollars > 0 && c == '}' && next == '}') { sb.Append("  "); i += 2; continue; }
            if (!verbatim && c == '\n') return; // unterminated: the line ends the literal
            Blank(sb, c);
            i++;
        }
    }
}
