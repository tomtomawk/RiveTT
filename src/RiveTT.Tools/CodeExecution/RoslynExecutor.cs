using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Tools.Utilities;

namespace RiveTT.Tools.CodeExecution;

/// <summary>
/// Compiles and executes C# code snippets inside the Revit process (2026.5+ or 2027).
///
/// Roslyn isolation: Revit hosts every add-in in ONE shared AssemblyLoadContext, and sibling
/// add-ins ship older System.Collections.Immutable / System.Reflection.Metadata copies. Simple-name
/// binding in the default ALC then hands Microsoft.CodeAnalysis 4.12 the wrong (older) dependency,
/// failing with "Could not load Microsoft.CodeAnalysis 4.12.0.0". To avoid this, the COMPILATION
/// step (the only Roslyn-touching code) is delegated to <see cref="RoslynCompilerWorker"/> loaded
/// into a dedicated <see cref="RoslynLoadContext"/> that resolves Roslyn + its 8.0 deps from OUR
/// plugin folder. This type itself references NO Microsoft.CodeAnalysis types, so the default ALC
/// never loads Roslyn (which would re-introduce the race). The emitted assembly bytes are then
/// loaded and run in the load context of THIS assembly, where the script's Revit API types match
/// the live globals and its calls to <see cref="ScriptApi"/> reach the very instance that holds
/// the run's context.
///
/// Transaction modes (the caller's value is trimmed and case-insensitive):
///   auto     one Transaction around the script, committed with the failure preprocessor (default)
///   none     no transaction: the script opens its own (StairsEditScope works here). Alias: manual
///   group    a TransactionGroup, assimilated into one undo entry; Section(...) commits per section
///   readonly a TransactionGroup that is ALWAYS rolled back: nothing the script does to the model is
///            kept. The one mode allowed while the ribbon lock is closed.
/// An unknown mode is refused. It used to fall through to "auto", so "manual" opened the very
/// transaction it was meant to avoid and "readonly" — published on the MCP surface — committed the
/// script's changes.
/// </summary>
public static class RoslynExecutor
{
    public const string ScriptFileName = RoslynCompilerWorker.ScriptFileName;

    /// <summary>Values accepted for transactionMode, as published.</summary>
    public static readonly string[] TransactionModes = { "auto", "none", "manual", "group", "readonly" };

    private const int MaxReportedIds = 200;

    private static MethodInfo? _compileMethod;
    private static readonly object _compileLock = new object();

    /// <summary>
    /// The canonical mode for a caller's value, or null when it is not one of
    /// <see cref="TransactionModes"/>. Null/blank means the default, "auto".
    /// </summary>
    public static string? NormalizeTransactionMode(string? raw)
    {
        var mode = (raw ?? "").Trim().ToLowerInvariant();
        if (mode.Length == 0) return "auto";
        if (mode == "manual") return "none";
        return TransactionModes.Contains(mode) ? mode : null;
    }

    public static RiveTTResult<object> Execute(
        string code,
        ScriptGlobals globals,
        string transactionMode = "auto")
    {
        var mode = NormalizeTransactionMode(transactionMode);
        if (mode == null)
            return RiveTTResult<object>.Fail(RiveTTErrorCode.InvalidInput,
                $"transactionMode '{transactionMode}' is not supported.",
                suggestion: "Use auto (default), none (alias manual), group or readonly.");

        var compileFailure = CompileScript(code, mode, out var prefixLines, out var assemblyBytes, out var pdbBytes);
        if (compileFailure != null) return compileFailure;

        MethodInfo method;
        try
        {
            method = LoadScript(assemblyBytes, pdbBytes);
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(RiveTTErrorCode.Unknown,
                $"The compiled script could not be loaded: {ex.Message}",
                suggestion: "Internal loading error, not a problem with your code.");
        }

        var invokeArgs = new object[] { globals.document, globals.uiDocument, globals.app, globals.scriptArgs ?? new JObject() };
        var context = ScriptApi.Begin(globals.document, mode, prefixLines);
        using var changes = new DocumentChangeTracker(globals.app, globals.document);
        var watch = Stopwatch.StartNew();
        // Set by the readonly branch's finally, read by the catch blocks below: a rollback
        // that itself failed must never be reported as "nothing was kept".
        string? rollbackError = null;
        try
        {
            object? result;
            TransactionFailureHandling.FailureCapture? txFailures = null;
            var modelKept = true;

            switch (mode)
            {
                case "none":
                    result = method.Invoke(null, invokeArgs);
                    break;

                case "group":
                {
                    using var txGroup = new TransactionGroup(globals.document, "RiveTT: Script Group");
                    txGroup.Start();
                    try
                    {
                        result = method.Invoke(null, invokeArgs);
                        if (txGroup.GetStatus() == TransactionStatus.Started
                            && txGroup.Assimilate() != TransactionStatus.Committed)
                        {
                            return Failure(RiveTTErrorCode.TransactionFailed,
                                "Revit rolled back the script transaction group on commit.",
                                "The script triggered a Revit error during commit. Fix the reported model errors and retry.",
                                context, changes: null, watch, extra: null);
                        }
                    }
                    catch
                    {
                        if (txGroup.GetStatus() == TransactionStatus.Started)
                            txGroup.RollBack();
                        throw;
                    }
                    break;
                }

                case "readonly":
                {
                    // Always rolled back, success or not: the guarantee the router relies on
                    // to let this mode through the ribbon lock. Model changes the script makes
                    // (in its own transactions or Sections) exist only until this RollBack.
                    using var txGroup = new TransactionGroup(globals.document, "RiveTT: Read-only Script");
                    txGroup.Start();
                    // The script also sees uiDocument.Document: when the active UI document is
                    // not the session's document, it gets its own always-rolled-back group.
                    // Every OTHER open document is out of reach: Application.Documents is
                    // refused by ReadOnlyScriptAnalyzer.
                    TransactionGroup? uiGroup = null;
                    var uiDocument = globals.uiDocument?.Document;
                    if (uiDocument != null && !uiDocument.Equals(globals.document) && !uiDocument.IsLinked)
                    {
                        uiGroup = new TransactionGroup(uiDocument, "RiveTT: Read-only Script (UI document)");
                        uiGroup.Start();
                    }
                    try
                    {
                        result = method.Invoke(null, invokeArgs);
                    }
                    finally
                    {
                        foreach (var group in new[] { uiGroup, txGroup })
                        {
                            if (group == null) continue;
                            try
                            {
                                if (group.GetStatus() == TransactionStatus.Started)
                                    group.RollBack();
                            }
                            catch (Exception rollback)
                            {
                                // RollBack refuses while an inner transaction is still open, i.e.
                                // one the script started and neither committed nor disposed.
                                rollbackError = rollback.Message;
                            }
                        }
                        uiGroup?.Dispose();
                    }
                    if (rollbackError != null)
                        return Failure(RiveTTErrorCode.TransactionFailed,
                            $"readonly: the rollback of the script's changes failed: {rollbackError}",
                            "The script left a transaction open. Check the model with a read tool and undo " +
                            "in Revit (Ctrl+Z) if it changed; wrap every Transaction in a using block.",
                            context, changes, watch, extra: new Dictionary<string, object>
                            {
                                ["stage"] = "rollback",
                                ["rolledBack"] = false,
                                ["modelChanged"] = "unknown"
                            });
                    modelKept = false;
                    break;
                }

                default: // auto
                {
                    using var tx = new Transaction(globals.document, "RiveTT: Script");
                    tx.Start();
                    txFailures = TransactionFailureHandling.SuppressWarnings(tx);
                    try
                    {
                        result = method.Invoke(null, invokeArgs);
                        if (tx.GetStatus() == TransactionStatus.Started
                            && tx.Commit() != TransactionStatus.Committed)
                        {
                            return Failure(RiveTTErrorCode.TransactionFailed,
                                $"Revit rolled back the script transaction: {TransactionFailureHandling.Describe(txFailures)}",
                                "The script triggered a Revit error during commit; errorGroups names the elements. " +
                                "Fix those elements, or split the script into Section(...) blocks under " +
                                "transactionMode \"group\" so one failing part no longer cancels the others.",
                                context, changes: null, watch,
                                extra: TransactionFailureHandling.ToContext(txFailures, null));
                        }
                    }
                    catch
                    {
                        if (tx.GetStatus() == TransactionStatus.Started)
                            tx.RollBack();
                        throw;
                    }
                    break;
                }
            }

            var data = SerializeResult(result);
            data["scriptRun"] = BuildRunReport(context, modelKept ? changes : null, modelKept, watch,
                txFailures?.WarningsSuppressed ?? 0);
            return RiveTTResult<object>.Ok(data);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            var inner = ex.InnerException;
            var line = ScriptLineNumbers.FromException(inner, prefixLines);
            // Under "none" the script's own transactions stay committed after it throws;
            // under every other mode the enclosing transaction or group was rolled back —
            // unless that rollback itself failed.
            var partial = mode == "none" || rollbackError != null;
            var extra = new Dictionary<string, object>
            {
                ["stage"] = "execution",
                ["line"] = line ?? (object)"unknown",
                ["exceptionType"] = inner.GetType().FullName ?? inner.GetType().Name,
                ["stackTrace"] = Truncate(inner.StackTrace ?? "", 2000),
                ["rolledBack"] = !partial,
                ["modelChanged"] = partial
                    ? "unknown: see scriptRun.changes for what was committed before the failure"
                    : (object)false
            };
            if (rollbackError != null) extra["rollbackError"] = rollbackError;
            return Failure(RiveTTErrorCode.Unknown,
                $"Runtime error{(line != null ? $" at line {line}" : "")}: {inner.GetType().Name}: {inner.Message}",
                line != null
                    ? $"Fix line {line} and re-run. With fromScript + edits you can correct just that line " +
                      "instead of re-sending the whole script."
                    : "Check variable names, null references, and Revit API usage.",
                context, partial ? changes : null, watch, extra);
        }
        catch (Exception ex)
        {
            return Failure(RiveTTErrorCode.Unknown, $"Execution error: {ex.Message}", null,
                context, changes: null, watch, extra: null);
        }
        finally
        {
            ScriptApi.End();
        }
    }

    /// <summary>
    /// Compiles without running: what send_code_to_revit's preview reports, so a syntax error or
    /// a readonly violation is known before the script is ever executed. Null when it compiles.
    /// </summary>
    public static RiveTTResult<object>? CheckCompiles(string code, string transactionMode)
    {
        var mode = NormalizeTransactionMode(transactionMode) ?? "auto";
        return CompileScript(code, mode, out _, out _, out _);
    }

    /// <summary>
    /// Compiles inside the isolated ALC (Roslyn + 8.0 deps resolved from our folder). With mode
    /// "readonly" the syntax tree is also checked by ReadOnlyScriptAnalyzer: the text filter
    /// cannot see an interpolation hole, a method group or an event subscription.
    /// </summary>
    private static RiveTTResult<object>? CompileScript(string code, string mode, out int prefixLines,
        out byte[] assemblyBytes, out byte[]? pdbBytes)
    {
        assemblyBytes = Array.Empty<byte>();
        pdbBytes = null;
        var wrappedCode = WrapCode(code, out prefixLines);
        var referencePaths = GatherReferencePaths();

        byte[]? emitted;
        string[] compileErrors;
        string[] violations;
        try
        {
            var compile = GetCompileMethod();
            var args = new object?[] { wrappedCode, referencePaths.ToArray(), prefixLines, mode == "readonly", null, null, null };
            emitted = (byte[]?)compile.Invoke(null, args);
            compileErrors = (string[]?)args[4] ?? Array.Empty<string>();
            pdbBytes = (byte[]?)args[5];
            violations = (string[]?)args[6] ?? Array.Empty<string>();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            return RiveTTResult<object>.Fail(
                RiveTTErrorCode.Unknown,
                $"Roslyn compilation failed: {ex.InnerException.Message}",
                suggestion: "This is an internal compiler/assembly-loading error, not a problem with your code.");
        }
        catch (Exception ex)
        {
            return RiveTTResult<object>.Fail(
                RiveTTErrorCode.Unknown,
                $"Roslyn compilation failed: {ex.Message}",
                suggestion: "This is an internal compiler/assembly-loading error, not a problem with your code.");
        }

        if (violations.Length > 0)
        {
            return RiveTTResult<object>.Fail(
                RiveTTErrorCode.PermissionDenied,
                $"transactionMode \"readonly\" refuses this script: {string.Join("; ", violations)}",
                suggestion: "readonly rolls back every change to the active document during the run; it cannot undo a " +
                            "handler, updater, timer or thread that runs later, a change to another open document, or a " +
                            "file written. Remove these, or run the script with transactionMode \"auto\" once the ribbon " +
                            "lock is open.",
                context: new Dictionary<string, object>
                {
                    ["stage"] = "readonlyCheck",
                    ["violations"] = violations,
                    ["modelChanged"] = false
                });
        }

        if (emitted == null)
        {
            return RiveTTResult<object>.Fail(
                RiveTTErrorCode.InvalidInput,
                $"Compilation error:\n{string.Join("\n", compileErrors)}",
                suggestion: "Globals: document (Document), uiDocument (UIDocument), app (Application), scriptArgs (JObject). " +
                            "Helpers: Mm, ToMm, Pt, Log, LevelByName, TypeByName<T>, SymbolByName, FindHostWall, " +
                            "PlaceOnLevel, PlaceInWall, Section. Use explicit 'return'.",
                context: new Dictionary<string, object>
                {
                    ["stage"] = "compilation",
                    ["compileErrors"] = compileErrors,
                    ["modelChanged"] = false
                });
        }

        assemblyBytes = emitted;
        return null;
    }

    private static RiveTTResult<object> Failure(RiveTTErrorCode code, string message, string? suggestion,
        ScriptRunContext context, DocumentChangeTracker? changes, Stopwatch watch,
        Dictionary<string, object>? extra)
    {
        var ctx = extra != null ? new Dictionary<string, object>(extra) : new Dictionary<string, object>();
        // What ran before the failure is exactly what the caller needs to resume. A tracker
        // is only passed when committed work may have survived the failure.
        ctx["scriptRun"] = BuildRunReport(context, changes, modelKept: changes != null, watch, 0);
        return RiveTTResult<object>.Fail(code, message, suggestion, ctx);
    }

    private static JObject BuildRunReport(ScriptRunContext context, DocumentChangeTracker? changes,
        bool modelKept, Stopwatch watch, int warningsSuppressed)
    {
        var report = new JObject
        {
            ["transactionMode"] = context.TransactionMode,
            ["modelKept"] = modelKept,
            ["durationMs"] = watch.ElapsedMilliseconds,
            ["warningsSuppressed"] = warningsSuppressed
        };
        if (context.LogLines.Count > 0)
        {
            report["log"] = new JArray(context.LogLines);
            if (context.LogLinesDropped > 0) report["logLinesDropped"] = context.LogLinesDropped;
        }
        if (context.Sections.Count > 0)
        {
            report["sections"] = JArray.FromObject(context.Sections.Select(s => s.ToReport()));
            report["sectionsCommitted"] = context.Sections.Count(s => s.Status == "committed");
            report["sectionsFailed"] = context.Sections.Count(s => s.Status != "committed");
        }
        if (changes != null) report["changes"] = changes.ToReport(MaxReportedIds);
        else if (!modelKept && context.TransactionMode == "readonly")
            report["note"] = "readonly: every model change was rolled back.";
        return report;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text.Substring(0, max) + "...";

    /// <summary>
    /// Loads the emitted script into the load context of THIS assembly, with its PDB so a
    /// runtime stack frame carries the script line.
    /// </summary>
    private static MethodInfo LoadScript(byte[] assemblyBytes, byte[]? pdbBytes)
    {
        var alc = AssemblyLoadContext.GetLoadContext(typeof(RoslynExecutor).Assembly) ?? AssemblyLoadContext.Default;
        using var pe = new MemoryStream(assemblyBytes);
        using var pdb = pdbBytes == null ? null : new MemoryStream(pdbBytes);
        var assembly = alc.LoadFromStream(pe, pdb);
        var type = assembly.GetType("RiveTT.DynamicScript.ScriptRunner")!;
        return type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
    }

    /// <summary>
    /// Lazily creates the isolated Roslyn ALC, loads RiveTT.Tools into it, and caches the
    /// <see cref="RoslynCompilerWorker.Compile"/> MethodInfo. Invoking it runs the Roslyn
    /// compilation inside that context, where Microsoft.CodeAnalysis + Immutable/Metadata 8.0
    /// bind to our plugin-folder copies instead of a sibling add-in's older versions.
    /// </summary>
    private static MethodInfo GetCompileMethod()
    {
        if (_compileMethod != null)
            return _compileMethod;

        lock (_compileLock)
        {
            if (_compileMethod != null)
                return _compileMethod;

            var dir = Path.GetDirectoryName(typeof(RoslynExecutor).Assembly.Location);
            if (string.IsNullOrEmpty(dir))
                dir = AppContext.BaseDirectory;

            var alc = new RoslynLoadContext(dir!);
            var toolsAsm = alc.LoadFromAssemblyPath(Path.Combine(dir!, "RiveTT.Tools.dll"));
            var workerType = toolsAsm.GetType("RiveTT.Tools.CodeExecution.RoslynCompilerWorker", throwOnError: true)!;
            // Two overloads exist: take the one with the readonly flag and the violations.
            _compileMethod = workerType.GetMethod("Compile", BindingFlags.Public | BindingFlags.Static, null,
                    new[]
                    {
                        typeof(string), typeof(string[]), typeof(int), typeof(bool),
                        typeof(string[]).MakeByRefType(), typeof(byte[]).MakeByRefType(), typeof(string[]).MakeByRefType()
                    }, null)
                ?? throw new InvalidOperationException("RoslynCompilerWorker.Compile not found");
            return _compileMethod;
        }
    }

    /// <summary>
    /// AssemblyLoadContext that serves Roslyn (Microsoft.CodeAnalysis*) and its 8.0 dependencies
    /// (System.Collections.Immutable / System.Reflection.Metadata) plus RiveTT.Tools from the
    /// plugin folder, and defers everything else (Revit API, RiveTT.Core, the .NET runtime) to
    /// the default ALC so those types stay shared.
    /// </summary>
    private sealed class RoslynLoadContext : AssemblyLoadContext
    {
        private readonly string _dir;

        public RoslynLoadContext(string dir) : base(name: "RiveTTRoslyn", isCollectible: false)
        {
            _dir = dir;
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
                return null;

            bool isolate = name!.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                || name == "System.Collections.Immutable"
                || name == "System.Reflection.Metadata"
                || name == "RiveTT.Tools";

            if (isolate)
            {
                var path = Path.Combine(_dir, name + ".dll");
                if (File.Exists(path))
                    return LoadFromAssemblyPath(path);
            }

            return null; // defer to the default ALC
        }
    }

    /// <summary>
    /// The lines placed before the caller's code. Their count is the offset that maps a
    /// compiler diagnostic or a stack frame back to the caller's line numbering, so it is
    /// computed from this list rather than kept as a separate constant that could drift.
    /// </summary>
    internal static readonly string[] WrapperHeader =
    {
        "using System;",
        "using System.Linq;",
        "using System.Collections.Generic;",
        "using Autodesk.Revit.DB;",
        // Room, Stairs, StairsRun, Railing live here: without it every script spelled them
        // out in full, or failed to compile on the short name (field report 2026-09-24).
        "using Autodesk.Revit.DB.Architecture;",
        "using Autodesk.Revit.DB.Structure;",
        "using Autodesk.Revit.UI;",
        "using Newtonsoft.Json.Linq;",
        "using static RiveTT.Tools.CodeExecution.ScriptApi;",
        "namespace RiveTT.DynamicScript {",
        "  public static class ScriptRunner {",
        "    public static object Run(",
        "      Autodesk.Revit.DB.Document document,",
        "      Autodesk.Revit.UI.UIDocument uiDocument,",
        "      Autodesk.Revit.ApplicationServices.Application app,",
        "      Newtonsoft.Json.Linq.JObject scriptArgs) {",
    };

    internal static string WrapCode(string userCode, out int prefixLines)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in WrapperHeader)
            sb.Append(line).Append('\n');
        prefixLines = WrapperHeader.Length;
        sb.Append(userCode).Append('\n');
        sb.Append("      return null;\n");
        sb.Append("    }\n");
        sb.Append("  }\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>
    /// Collects the file paths of the assemblies the script may reference. Pure string work —
    /// deliberately does NOT touch Microsoft.CodeAnalysis (MetadataReference creation happens in
    /// the isolated worker), so JITing this type never loads Roslyn into the default ALC.
    /// </summary>
    private static List<string> GatherReferencePaths()
    {
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddTrustedPlatformAssemblies(paths, seen);

        AddAssemblyPath(paths, seen, typeof(object).Assembly);
        AddAssemblyPath(paths, seen, typeof(Enumerable).Assembly);
        AddAssemblyPath(paths, seen, typeof(List<>).Assembly);
        AddAssemblyPath(paths, seen, typeof(Document).Assembly);
        AddAssemblyPath(paths, seen, typeof(Autodesk.Revit.UI.UIDocument).Assembly);
        AddAssemblyPath(paths, seen, typeof(JObject).Assembly);
        AddAssemblyPath(paths, seen, typeof(ScriptApi).Assembly);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (ShouldReferenceLoadedAssembly(asm))
                AddAssemblyPath(paths, seen, asm);
        }

        return paths;
    }

    private static void AddTrustedPlatformAssemblies(List<string> paths, HashSet<string> seen)
    {
        var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(tpa))
            return;

        foreach (var path in tpa!.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var name = Path.GetFileNameWithoutExtension(path);
            if (!IsFrameworkReference(name))
                continue;

            AddPath(paths, seen, path);
        }
    }

    private static bool IsFrameworkReference(string name)
    {
        return name == "netstandard"
            || name == "Microsoft.CSharp"
            || name == "WindowsBase"
            || name == "PresentationCore"
            || name == "PresentationFramework"
            || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldReferenceLoadedAssembly(Assembly asm)
    {
        try
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                return false;

            var name = asm.GetName().Name ?? string.Empty;
            return name == "RevitAPI"
                || name == "RevitAPIUI"
                || name == "Newtonsoft.Json"
                || name.StartsWith("Autodesk.Revit.", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("RiveTT.", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void AddAssemblyPath(List<string> paths, HashSet<string> seen, Assembly asm)
    {
        try
        {
            if (asm.IsDynamic || string.IsNullOrEmpty(asm.Location))
                return;

            AddPath(paths, seen, asm.Location);
        }
        catch { }
    }

    private static void AddPath(List<string> paths, HashSet<string> seen, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
                return;

            paths.Add(path);
        }
        catch { }
    }

    /// <summary>
    /// The script's return value as the response payload. An object's properties land at the
    /// top level (return new { walls = n, ok = true }); a scalar or a collection lands under
    /// "result". Revit objects are written compactly instead of being walked property by
    /// property: an Element as id/name/category, an ElementId as its number, an XYZ in feet.
    /// </summary>
    internal static JObject SerializeResult(object? result)
    {
        if (result == null)
            return new JObject { ["result"] = null };

        if (result is string or int or long or double or float or bool or decimal)
            return new JObject { ["result"] = JToken.FromObject(result) };

        try
        {
            var serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                Converters = { new RevitScriptValueConverter() },
                Error = (sender, args) => args.ErrorContext.Handled = true
            });
            var token = JToken.FromObject(result, serializer);
            // The result envelope expects a JObject. A script that returns a collection
            // (List/array) or a Revit value parses to something else — wrap it so it doesn't
            // fail downstream with "Object serialized to Array. JObject instance expected."
            return token is JObject obj ? obj : new JObject { ["result"] = token };
        }
        catch
        {
            return new JObject { ["result"] = result.ToString() };
        }
    }

    /// <summary>
    /// Records which elements the script's committed transactions created, modified and
    /// deleted, through the application's DocumentChanged event — the same mechanism add-ins
    /// use to learn the ids of instances placed interactively. It turns "the script ran" into
    /// "the script created these 77 elements", which is what an agent needs to verify or to
    /// undo selectively.
    /// </summary>
    private sealed class DocumentChangeTracker : IDisposable
    {
        private readonly Autodesk.Revit.ApplicationServices.Application? _app;
        private readonly Document _document;
        private readonly HashSet<long> _added = new HashSet<long>();
        private readonly HashSet<long> _modified = new HashSet<long>();
        private readonly HashSet<long> _deleted = new HashSet<long>();
        private bool _undone;
        private bool _subscribed;

        public DocumentChangeTracker(Autodesk.Revit.ApplicationServices.Application? app, Document document)
        {
            _app = app;
            _document = document;
            try
            {
                if (_app != null)
                {
                    _app.DocumentChanged += OnDocumentChanged;
                    _subscribed = true;
                }
            }
            catch
            {
                _subscribed = false;
            }
        }

        private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
        {
            try
            {
                if (!e.GetDocument().Equals(_document)) return;
                if (e.Operation != UndoOperation.TransactionCommitted)
                {
                    _undone = true;
                    return;
                }
                foreach (var id in e.GetAddedElementIds()) _added.Add(ToolHelpers.GetElementIdValue(id));
                foreach (var id in e.GetModifiedElementIds())
                {
                    var value = ToolHelpers.GetElementIdValue(id);
                    if (!_added.Contains(value)) _modified.Add(value);
                }
                foreach (var id in e.GetDeletedElementIds())
                {
                    var value = ToolHelpers.GetElementIdValue(id);
                    // Created then deleted by the same script: it never existed for the caller.
                    if (!_added.Remove(value)) _deleted.Add(value);
                    _modified.Remove(value);
                }
            }
            catch
            {
                // A failure to observe must never become a failure of the script.
            }
        }

        public JObject ToReport(int maxIds)
        {
            if (!_subscribed)
                return new JObject { ["captured"] = false, ["reason"] = "DocumentChanged could not be observed" };
            var report = new JObject
            {
                ["captured"] = true,
                ["createdCount"] = _added.Count,
                ["modifiedCount"] = _modified.Count,
                ["deletedCount"] = _deleted.Count,
                ["createdElementIds"] = new JArray(_added.OrderBy(id => id).Take(maxIds)),
                ["deletedElementIds"] = new JArray(_deleted.OrderBy(id => id).Take(maxIds)),
                ["modifiedElementIds"] = new JArray(_modified.OrderBy(id => id).Take(maxIds)),
            };
            if (_added.Count > maxIds || _modified.Count > maxIds || _deleted.Count > maxIds)
                report["idsTruncatedAt"] = maxIds;
            if (_undone)
                report["note"] = "Part of the run was rolled back or undone; the ids above only count committed transactions.";
            return report;
        }

        public void Dispose()
        {
            if (!_subscribed || _app == null) return;
            try { _app.DocumentChanged -= OnDocumentChanged; } catch { }
            _subscribed = false;
        }
    }
}

/// <summary>
/// Writes Revit values compactly in a script's result instead of letting Json.NET walk them:
/// an Element's Document property alone would pull the whole model into the response.
/// </summary>
internal sealed class RevitScriptValueConverter : JsonConverter
{
    public override bool CanRead => false;

    public override bool CanConvert(Type objectType) =>
        typeof(Element).IsAssignableFrom(objectType)
        || objectType == typeof(ElementId)
        || objectType == typeof(XYZ)
        || objectType == typeof(UV)
        || objectType == typeof(BoundingBoxXYZ)
        || typeof(Document).IsAssignableFrom(objectType);

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        => throw new NotSupportedException();

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        switch (value)
        {
            case null:
                writer.WriteNull();
                return;
            case ElementId id:
                writer.WriteValue(ToolHelpers.GetElementIdValue(id));
                return;
            case XYZ point:
                WritePoint(writer, point);
                return;
            case UV uv:
                writer.WriteStartObject();
                writer.WritePropertyName("u"); writer.WriteValue(uv.U);
                writer.WritePropertyName("v"); writer.WriteValue(uv.V);
                writer.WritePropertyName("unit"); writer.WriteValue("ft");
                writer.WriteEndObject();
                return;
            case BoundingBoxXYZ box:
                writer.WriteStartObject();
                writer.WritePropertyName("min"); WritePoint(writer, box.Min);
                writer.WritePropertyName("max"); WritePoint(writer, box.Max);
                writer.WriteEndObject();
                return;
            case Document document:
                writer.WriteStartObject();
                writer.WritePropertyName("title"); writer.WriteValue(Safe(() => document.Title));
                writer.WritePropertyName("pathName"); writer.WriteValue(Safe(() => document.PathName));
                writer.WriteEndObject();
                return;
            case Element element:
                writer.WriteStartObject();
                writer.WritePropertyName("elementId"); writer.WriteValue(ToolHelpers.GetElementIdValue(element.Id));
                writer.WritePropertyName("name"); writer.WriteValue(Safe(() => element.Name));
                writer.WritePropertyName("category"); writer.WriteValue(Safe(() => element.Category?.Name));
                writer.WritePropertyName("categoryBic");
                writer.WriteValue(Safe(() => CategoryResolver.DescribeBuiltInCategory(element.Category)));
                writer.WriteEndObject();
                return;
            default:
                writer.WriteValue(value.ToString());
                return;
        }
    }

    private static void WritePoint(JsonWriter writer, XYZ? point)
    {
        if (point == null)
        {
            writer.WriteNull();
            return;
        }
        writer.WriteStartObject();
        writer.WritePropertyName("x"); writer.WriteValue(point.X);
        writer.WritePropertyName("y"); writer.WriteValue(point.Y);
        writer.WritePropertyName("z"); writer.WriteValue(point.Z);
        writer.WritePropertyName("unit"); writer.WriteValue("ft");
        writer.WriteEndObject();
    }

    private static string? Safe(Func<string?> read)
    {
        try { return read(); } catch { return null; }
    }
}
