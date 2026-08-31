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
        // .GetType( <something non-whitespace> ) — covers Assembly.GetType("..."), .GetType(name),
        // .GetType(ns + "." + cls). Excludes .GetType() (zero-arg, harmless).
        new Regex(@"\.\s*GetType\s*\(\s*\S[^)]*\)", RegexOptions.Compiled),
        // .GetMethod( <something non-whitespace> ) etc. — same rationale; excludes zero-arg overloads.
        new Regex(@"\.\s*(GetMethod|GetField|GetProperty|GetMember|GetConstructor|InvokeMember)\s*\(\s*\S[^)]*\)", RegexOptions.Compiled),
    };

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
    /// </summary>
    public static string StripCommentsAndStrings(string code)
    {
        var sb = new StringBuilder(code.Length);
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];

            // Line comment //...
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') { sb.Append(' '); i++; }
                continue;
            }

            // Block comment /* ... */
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                sb.Append("  ");
                i += 2;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/'))
                {
                    sb.Append(code[i] == '\n' ? '\n' : ' ');
                    i++;
                }
                if (i + 1 < code.Length) { sb.Append("  "); i += 2; }
                continue;
            }

            // Verbatim string @"..."  (escapes are "")
            if (c == '@' && i + 1 < code.Length && code[i + 1] == '"')
            {
                sb.Append("  "); i += 2;
                while (i < code.Length)
                {
                    if (code[i] == '"')
                    {
                        if (i + 1 < code.Length && code[i + 1] == '"')
                        {
                            sb.Append("  "); i += 2; continue; // escaped quote
                        }
                        sb.Append(' '); i++; break;
                    }
                    sb.Append(code[i] == '\n' ? '\n' : ' '); i++;
                }
                continue;
            }

            // Regular string "..."  (backslash escapes)
            if (c == '"')
            {
                sb.Append(' '); i++;
                while (i < code.Length && code[i] != '"')
                {
                    if (code[i] == '\\' && i + 1 < code.Length)
                    {
                        sb.Append("  "); i += 2; continue;
                    }
                    sb.Append(code[i] == '\n' ? '\n' : ' '); i++;
                }
                if (i < code.Length) { sb.Append(' '); i++; }
                continue;
            }

            // Char literal '.'
            if (c == '\'')
            {
                sb.Append(' '); i++;
                while (i < code.Length && code[i] != '\'')
                {
                    if (code[i] == '\\' && i + 1 < code.Length) { sb.Append("  "); i += 2; continue; }
                    sb.Append(' '); i++;
                }
                if (i < code.Length) { sb.Append(' '); i++; }
                continue;
            }

            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }
}
