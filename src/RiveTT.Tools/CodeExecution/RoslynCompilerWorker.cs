using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace RiveTT.Tools.CodeExecution;

/// <summary>
/// The Roslyn-touching half of send_code_to_revit compilation. This type is designed to be
/// loaded and invoked inside an isolated <see cref="System.Runtime.Loader.AssemblyLoadContext"/>
/// (see <c>RoslynExecutor.GetCompileMethod</c>), so Microsoft.CodeAnalysis 4.12 and its
/// System.Collections.Immutable / System.Reflection.Metadata 8.0 dependencies bind to OUR
/// copies in the plugin folder instead of an older version that a sibling Revit add-in may
/// have loaded into the shared default ALC (the cause of
/// "Could not load Microsoft.CodeAnalysis 4.12.0.0").
///
/// IMPORTANT: the public signature uses only framework types (string / string[] / byte[] /
/// int) so it can be invoked by reflection across the ALC boundary without any shared-type
/// identity problems. It must NOT expose Microsoft.CodeAnalysis types. Compilation produces
/// the emitted assembly bytes; the bytes are loaded and run later in the DEFAULT ALC by
/// RoslynExecutor, where the script's Revit API types match the live document globals.
/// </summary>
public static class RoslynCompilerWorker
{
    /// <summary>
    /// File name given to the script's syntax tree. It is what a runtime stack frame shows
    /// ("in RiveTTScript.cs:line 57"), which is how RoslynExecutor maps an exception back to
    /// the caller's line. Must match <c>RoslynExecutor.ScriptFileName</c>.
    /// </summary>
    public const string ScriptFileName = "RiveTTScript.cs";

    /// <summary>
    /// Compiles <paramref name="wrappedCode"/> to an in-memory assembly and its portable PDB.
    /// Returns the emitted bytes on success (with <paramref name="errors"/> empty), or null
    /// on failure (with <paramref name="errors"/> populated, line numbers already adjusted to
    /// be relative to the user's code via <paramref name="prefixLines"/>).
    ///
    /// The PDB exists for one reason: without it a runtime exception carries no line number,
    /// and a 300-line script that threw somewhere had to be re-read in full to find where
    /// (field report of 2026-09-24). Debug optimization keeps the line table exact.
    /// </summary>
    public static byte[]? Compile(string wrappedCode, string[] referencePaths, int prefixLines,
        out string[] errors, out byte[]? pdb)
        => Compile(wrappedCode, referencePaths, prefixLines, readOnlyMode: false, out errors, out pdb, out _);

    /// <summary>
    /// As above; with <paramref name="readOnlyMode"/>, the script is also checked by
    /// <see cref="ReadOnlyScriptAnalyzer"/> before anything is emitted. A violation returns null
    /// with the reasons in <paramref name="violations"/> and no compiler error.
    /// </summary>
    public static byte[]? Compile(string wrappedCode, string[] referencePaths, int prefixLines, bool readOnlyMode,
        out string[] errors, out byte[]? pdb, out string[] violations)
    {
        pdb = null;
        violations = Array.Empty<string>();
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest);

        var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode, parseOptions,
            path: ScriptFileName, encoding: Encoding.UTF8);

        var refs = new List<MetadataReference>(referencePaths.Length);
        foreach (var path in referencePaths)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    refs.Add(MetadataReference.CreateFromFile(path));
            }
            catch { }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName: "RiveTTScript_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: new[] { syntaxTree },
            references: refs,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: false));

        if (readOnlyMode)
        {
            var found = ReadOnlyScriptAnalyzer.Analyze(compilation, syntaxTree, prefixLines);
            if (found.Count > 0)
            {
                violations = found.ToArray();
                errors = Array.Empty<string>();
                return null;
            }
        }

        using var ms = new MemoryStream();
        using var pdbStream = new MemoryStream();
        var emitResult = compilation.Emit(ms, pdbStream,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));

        if (!emitResult.Success)
        {
            errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d =>
                {
                    var line = d.Location.GetLineSpan().StartLinePosition.Line + 1 - prefixLines;
                    return $"Line {line}: {d.GetMessage()}";
                })
                .ToArray();
            return null;
        }

        errors = Array.Empty<string>();
        pdb = pdbStream.ToArray();
        return ms.ToArray();
    }
}
