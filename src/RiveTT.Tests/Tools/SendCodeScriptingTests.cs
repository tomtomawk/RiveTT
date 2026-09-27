using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Tools;
using RiveTT.Tools.CodeExecution;
using RiveTT.Tools.Elements;
using Xunit;

namespace RiveTT.Tests.Tools;

/// <summary>
/// send_code_to_revit after the field session of 2026-09-24 (docs/retex/2026-09-24): modes that
/// do what they say, a read branch that works while RiveTT is locked, runtime errors that carry
/// the script line, and saved scripts that can be corrected by one edit instead of re-sent.
/// </summary>
public class SendCodeScriptingTests
{
    // ── transaction modes ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("auto", "auto")]
    [InlineData(" Group ", "group")]
    [InlineData("none", "none")]
    [InlineData("manual", "none")]
    [InlineData("MANUAL", "none")]
    [InlineData("readonly", "readonly")]
    [InlineData("READONLY", "readonly")]
    public void KnownModesNormalize(string? raw, string expected)
    {
        Assert.Equal(expected, RoslynExecutor.NormalizeTransactionMode(raw));
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("sections")]
    [InlineData("transaction")]
    public void UnknownModesAreRefusedInsteadOfFallingBackToAuto(string raw)
    {
        // "manual" and "readonly" were published on the MCP surface and both fell through to
        // the auto branch: manual opened the transaction it was meant to avoid, and readonly
        // committed the script's changes.
        Assert.Null(RoslynExecutor.NormalizeTransactionMode(raw));
    }

    // ScriptGlobals names UIDocument: JIT-compiling Execute loads RevitAPIUI.
    [RequiresRevitApiFact]
    public void ExecutorRefusesAnUnknownModeBeforeCompiling()
    {
        var result = RoslynExecutor.Execute("return 1;", new ScriptGlobals(), "bogus");
        Assert.False(result.Success);
        Assert.Equal(RiveTTErrorCode.InvalidInput, result.Error!.Code);
    }

    // ── the read branch through the lock ─────────────────────────────────────

    [Fact]
    public void SendCodeDeclaresReadonlyAsItsOnlyReadBranch()
    {
        var attribute = typeof(SendCodeToRevitTool).GetCustomAttribute<ReadOnlyActionsAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal("transactionMode", attribute!.Key);
        // DefaultAction must match the runtime default, or an omitted mode would be misread.
        Assert.Equal("auto", attribute.DefaultAction);
        Assert.Equal(RoslynExecutor.NormalizeTransactionMode(null), attribute.DefaultAction);
        Assert.Equal(new[] { "readonly" }, attribute.Actions);
    }

    [Theory]
    [InlineData("{\"code\":\"return 1;\",\"transactionMode\":\"readonly\"}", true)]
    [InlineData("{\"code\":\"return 1;\",\"transactionMode\":\"READONLY\"}", true)]
    [InlineData("{\"code\":\"return 1;\"}", false)]
    [InlineData("{\"code\":\"return 1;\",\"transactionMode\":\"auto\"}", false)]
    [InlineData("{\"code\":\"return 1;\",\"transactionMode\":\"manual\"}", false)]
    [InlineData("{\"code\":\"return 1;\",\"transactionMode\":\" readonly \"}", false)]
    [InlineData("{\"code\":\"return 1;\",\"transactionMode\":[\"readonly\"]}", false)]
    public void OnlyTheReadonlyModeMatchesTheReadBranch(string json, bool readOnly)
    {
        var attribute = typeof(SendCodeToRevitTool).GetCustomAttribute<ReadOnlyActionsAttribute>()!;
        Assert.Equal(readOnly, attribute.Matches(JObject.Parse(json)));
    }

    [Fact]
    public void EveryValueTheLockLetsThroughRunsAsReadonly()
    {
        // The two sides must agree in the dangerous direction: whatever the attribute lets
        // through the lock must be executed as readonly by the runtime, never as auto.
        var attribute = typeof(SendCodeToRevitTool).GetCustomAttribute<ReadOnlyActionsAttribute>()!;
        foreach (var value in new[] { "readonly", "READONLY", "ReadOnly" })
        {
            Assert.True(attribute.Matches(new JObject { ["transactionMode"] = value }));
            Assert.Equal("readonly", RoslynExecutor.NormalizeTransactionMode(value));
        }
    }

    [Theory]
    [InlineData("document.Save();", "Save")]
    [InlineData("document.SaveAs(@\"C:\\x.rvt\");", "SaveAs")]
    [InlineData("document.Export(folder, name, options);", "Export")]
    [InlineData("document.ExportImage(options);", "ExportImage")]
    [InlineData("app.OpenDocumentFile(path);", "OpenDocumentFile")]
    [InlineData("uiDocument.Application.OpenAndActivateDocument(path);", "OpenAndActivateDocument")]
    [InlineData("var f = document.EditFamily(family);", "EditFamily")]
    [InlineData("document.SynchronizeWithCentral(t, s);", "SynchronizeWithCentral")]
    [InlineData("uiDocument.ActiveView = view;", "ActiveView")]
    [InlineData("uiDocument.RequestViewChange(view);", "RequestViewChange")]
    [InlineData("other.Close(false);", "Close")]
    public void ReadonlyRefusesCallsARollbackCannotUndo(string code, string expected)
    {
        var result = CodeSandboxV2.ValidateReadOnlyScript(code);
        Assert.NotNull(result);
        Assert.Equal(RiveTTErrorCode.PermissionDenied, result!.Error!.Code);
        Assert.Contains(expected, result.Error.Message);
    }

    [Theory]
    [InlineData("var walls = new FilteredElementCollector(document).OfClass(typeof(Wall)).ToElements(); return walls.Count;")]
    [InlineData("if (uiDocument.ActiveView == null) return 0; return uiDocument.ActiveView.Name;")]
    [InlineData("// document.Save(); is what we must not do\nreturn 1;")]
    [InlineData("var s = \"document.Export(x)\"; return s;")]
    [InlineData("using (var t = new Transaction(document, \"probe\")) { t.Start(); t.RollBack(); } return 1;")]
    [InlineData("Section(\"probe\", () => { Log(\"x\"); }); return 1;")]
    public void ReadonlyAcceptsReadsAndRolledBackProbes(string code)
    {
        Assert.Null(CodeSandboxV2.ValidateReadOnlyScript(code));
    }

    // ── the reflection filter no longer refuses a type name in a log line ────

    [Theory]
    [InlineData("log.Add(c.GetType().Name);")]
    [InlineData("Log(\"curve \" + c.GetType().Name + \" skipped\");")]
    [InlineData("Log($\"{c.GetType().Name} ({i})\");")]
    [InlineData("var kinds = curves.Select(c => c.GetType().Name).Distinct().ToList();")]
    public void ZeroArgGetTypeInsideACallIsAllowed(string code)
    {
        // Field report 2026-09-24, bug 1.4: `.GetType().Name` inside a call was refused as
        // reflection because \S matched the closing parenthesis of GetType().
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Theory]
    [InlineData("var t = asm.GetType(\"System.IO.File\");")]
    [InlineData("var t = asm.GetType( name );")]
    [InlineData("var m = typeof(Wall).Assembly.GetType(ns + \".File\");")]
    [InlineData("var m = t.GetMethod(\"Delete\");")]
    public void GetTypeWithAnArgumentIsStillRefused(string code)
    {
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    // ── fromScript + edits ───────────────────────────────────────────────────

    [Fact]
    public void StripHeaderRemovesThePersistedHeaderOnly()
    {
        var saved = "// TEMP\n// Generated: 2026-09-24 10:00:00\n// Name: murs\n" +
                    "// =============================================\n" +
                    "var x = 1;\n// =============================================\nreturn x;";
        Assert.Equal("var x = 1;\n// =============================================\nreturn x;",
            SendCodeToRevitTool.StripHeader(saved));
        Assert.Equal("return 1;", SendCodeToRevitTool.StripHeader("return 1;"));
    }

    [Fact]
    public void FromScriptLoadsTheLatestVersionOfThatExactName()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rivett-scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            const string header = "// TEMP\n// Generated: x\n// Name: murs\n// =============================================\n";
            File.WriteAllText(Path.Combine(folder, "20260924_101010_murs.cs"), header + "return 1;");
            File.WriteAllText(Path.Combine(folder, "20260924_111111_murs.cs"), header + "return 2;");
            // Same suffix, other scripts: must never be taken for "murs".
            File.WriteAllText(Path.Combine(folder, "20260924_121212_v2_murs.cs"), header + "return 3;");
            File.WriteAllText(Path.Combine(folder, "20260924_131313_murs-v2.cs"), header + "return 4;");

            var code = SendCodeToRevitTool.LoadSavedScript(folder, "murs", out var path, out var error);
            Assert.Null(error);
            Assert.Equal("return 2;", code);
            Assert.EndsWith("20260924_111111_murs.cs", path);

            var missing = SendCodeToRevitTool.LoadSavedScript(folder, "toiture", out _, out var notFound);
            Assert.Null(missing);
            Assert.Equal(RiveTTErrorCode.ElementNotFound, notFound!.Error!.Code);
            Assert.Contains("murs", notFound.Error.Suggestion);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void EditsReplaceExactlyOneOccurrenceInOrder()
    {
        var code = "var h = Mm(2500);\nvar w = Mm(200);\nreturn h + w;";
        var edits = JArray.Parse("[{\"oldText\":\"Mm(2500)\",\"newText\":\"Mm(2700)\"}," +
                                 "{\"oldText\":\"Mm(2700);\\nvar w\",\"newText\":\"Mm(2700);\\nvar width\"}]");
        var result = SendCodeToRevitTool.ApplyEdits(code, edits, out var applied, out var error);
        Assert.Null(error);
        Assert.Equal(2, applied);
        Assert.Equal("var h = Mm(2700);\nvar width = Mm(200);\nreturn h + w;", result);
    }

    [Theory]
    [InlineData("[{\"oldText\":\"Mm(\",\"newText\":\"X(\"}]", "occurs 2 times")]
    [InlineData("[{\"oldText\":\"absent\",\"newText\":\"x\"}]", "was not found")]
    [InlineData("[{\"oldText\":\"\",\"newText\":\"x\"}]", "cannot be empty")]
    [InlineData("{\"oldText\":\"Mm(\"}", "must be a JSON array")]
    public void AmbiguousOrMissingEditsAreRefused(string editsJson, string expected)
    {
        var code = "var h = Mm(2500);\nvar w = Mm(200);";
        var result = SendCodeToRevitTool.ApplyEdits(code, JToken.Parse(editsJson), out _, out var error);
        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Equal(RiveTTErrorCode.InvalidInput, error!.Error!.Code);
        Assert.Contains(expected, error.Error.Message);
    }

    // ── line numbers ─────────────────────────────────────────────────────────

    [Fact]
    public void WrapperOffsetIsTheHeaderLength()
    {
        var wrapped = RoslynExecutor.WrapCode("int first = 1;\nreturn first;", out var prefix);
        Assert.Equal(RoslynExecutor.WrapperHeader.Length, prefix);
        var lines = wrapped.Split('\n');
        Assert.Equal("int first = 1;", lines[prefix]);
        Assert.Contains("using Autodesk.Revit.DB.Architecture;", wrapped);
        Assert.Contains("using static RiveTT.Tools.CodeExecution.ScriptApi;", wrapped);
        Assert.Contains("JObject scriptArgs", wrapped);
    }

    [Fact]
    public void StackFramesMapBackToTheCallersLine()
    {
        var trace = "   at RiveTT.DynamicScript.ScriptRunner.<>c.<Run>b__0_0() in RiveTTScript.cs:line 23\n" +
                    "   at RiveTT.Tools.CodeExecution.ScriptApi.Section(String name, Action body)\n" +
                    "   at RiveTT.DynamicScript.ScriptRunner.Run(...) in RiveTTScript.cs:line 40";
        Assert.Equal(23 - 16, ScriptLineNumbers.FromStackTrace(trace, 16));
        Assert.Null(ScriptLineNumbers.FromStackTrace("   at Foo.Bar() in Other.cs:line 3", 16));
        Assert.Null(ScriptLineNumbers.FromStackTrace(null, 16));
    }

    [RequiresRevitDbApiFact]
    public void CompiledScriptResolvesTheInjectedNamespacesAndHelpers()
    {
        // Compiles against the real Revit API: Room and StairsRun by their short names, the
        // helpers without prefix, scriptArgs as a JObject. An ambiguity introduced by one of
        // the wrapper's usings would fail here, not in the user's session.
        var code = string.Join("\n",
            "double h = Mm(2890);",
            "XYZ p = Pt(1000, 2000);",
            "Room r = null;",
            "StairsRun run = null;",
            "var level = scriptArgs[\"level\"]?.Value<string>();",
            "Section(\"noop\", () => Log(\"x\"));",
            "return new { h, p, hasRoom = r != null, hasRun = run != null, level };");
        var bytes = Compile(code, out var errors, out var pdb);
        Assert.True(bytes != null, string.Join("\n", errors));
        Assert.NotNull(pdb);
        Assert.NotEmpty(pdb!);
    }

    [RequiresRevitDbApiFact]
    public void CompileErrorsCarryTheCallersLineNumber()
    {
        var bytes = Compile("int a = 1;\nint b = a +;\nreturn b;", out var errors, out _);
        Assert.Null(bytes);
        Assert.Contains(errors, error => error.StartsWith("Line 2:", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeExceptionsCarryTheCallersLineNumber()
    {
        // End to end without Revit: PDB emitted, loaded with the assembly, frame mapped back.
        // The real wrapper's signature names UIDocument, whose assembly cannot load outside
        // Revit, so this wraps the same way with framework types only.
        var header = new[]
        {
            "using System;",
            "namespace RiveTT.DynamicScript {",
            "  public static class ScriptRunner {",
            "    public static object Run() {",
        };
        var wrapped = string.Join("\n", header) + "\n" +
                      "int a = 1;\nint b = a + 1;\nthrow new InvalidOperationException(\"boom\" + b);\n" +
                      "    }\n  }\n}\n";
        var framework = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                           || Path.GetFileName(path) == "netstandard.dll")
            .ToArray();

        var bytes = RoslynCompilerWorker.Compile(wrapped, framework, header.Length, out var errors, out var pdb);
        Assert.True(bytes != null, string.Join("\n", errors));
        Assert.NotNull(pdb);

        var context = new AssemblyLoadContext("script-line-test", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(bytes!), new MemoryStream(pdb!));
            var run = assembly.GetType("RiveTT.DynamicScript.ScriptRunner")!.GetMethod("Run")!;
            var exception = Assert.Throws<TargetInvocationException>(() => run.Invoke(null, null));
            Assert.Equal(3, ScriptLineNumbers.FromException(exception.InnerException, header.Length));
        }
        finally
        {
            context.Unload();
        }
    }

    private static byte[]? Compile(string code, out string[] errors, out byte[]? pdb)
    {
        var wrapped = RoslynExecutor.WrapCode(code, out var prefix);
        return RoslynCompilerWorker.Compile(wrapped, ReferencePaths().ToArray(), prefix, out errors, out pdb);
    }

    private static IEnumerable<string> ReferencePaths()
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path) is var name
                           && (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                               || name is "netstandard.dll" or "Microsoft.CSharp.dll"));
        foreach (var path in tpa) yield return path;

        var revitApi = typeof(Autodesk.Revit.DB.Document).Assembly.Location;
        yield return revitApi;
        // RevitAPIUI's own native init fails outside Revit: reference it by path, never load it.
        yield return Path.Combine(Path.GetDirectoryName(revitApi)!, "RevitAPIUI.dll");
        yield return typeof(JObject).Assembly.Location;
        yield return typeof(ScriptApi).Assembly.Location;
    }
}
