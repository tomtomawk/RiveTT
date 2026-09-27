using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Security;
using RiveTT.Tools.CodeExecution;
using Xunit;

namespace RiveTT.Tests.Security;

/// <summary>
/// transactionMode "readonly" runs while the ribbon lock is CLOSED. The review of 0.6.0 found
/// four ways past the rolled-back TransactionGroup; each must be refused, and ordinary reads
/// must still pass. Semantic checks compile against the real Revit API (skipped without it).
/// </summary>
public class ReadOnlyScriptGuardTests
{
    // ── semantic guard (compile time) ────────────────────────────────────────

    [RequiresRevitDbApiFact]
    public void AnIdlingHandlerThatWouldWriteAfterTheRollbackIsRefused()
    {
        var violations = Violations(
            "uiDocument.Application.Idling += (s, e) => { };\nreturn 1;");
        Assert.Contains(violations, v => v.Contains("event subscription") && v.Contains("Idling"));
    }

    [RequiresRevitDbApiFact]
    public void OtherOpenDocumentsAreOutOfReach()
    {
        var violations = Violations(
            "foreach (Document d in app.Documents) { }\nreturn 1;");
        Assert.Contains(violations, v => v.Contains("Documents"));
    }

    [RequiresRevitDbApiFact]
    public void CodeInAnInterpolationHoleIsSeen()
    {
        var violations = Violations(
            "var s = $\"{document.Export(\"c:\\\\x\", \"n\", new List<ElementId>(), new DWGExportOptions())}\";\nreturn s;");
        Assert.Contains(violations, v => v.Contains("Export"));
    }

    [RequiresRevitDbApiFact]
    public void AMethodGroupIsSeenWithoutParentheses()
    {
        var violations = Violations("Func<bool, bool> close = document.Close;\nreturn 1;");
        Assert.Contains(violations, v => v.Contains("Close"));
    }

    [RequiresRevitDbApiFact]
    public void ClosingTheBodyToDeclareMembersIsRefused()
    {
        var violations = Violations("return 1;\n    }\n    static object Later() {");
        Assert.Contains(violations, v => v.Contains("outside the script body"));
    }

    [RequiresRevitDbApiFact]
    public void ThreadsAndTasksAreRefused()
    {
        Assert.Contains(Violations("System.Threading.Tasks.Task.Run(() => { });\nreturn 1;"),
            v => v.Contains("Task"));
    }

    [RequiresRevitDbApiFact]
    public void SwitchingTheActiveViewIsRefused()
    {
        Assert.Contains(Violations("uiDocument.ActiveView = document.ActiveView;\nreturn 1;"),
            v => v.Contains("ActiveView"));
    }

    [RequiresRevitDbApiFact]
    public void ReadsAndRolledBackProbesCompileWithoutViolation()
    {
        var code = string.Join("\n",
            "var walls = new FilteredElementCollector(document).OfClass(typeof(Wall)).ToElementIds();",
            "if (uiDocument.ActiveView == null) return 0;",
            "using (var t = new Transaction(document, \"probe\")) { t.Start(); t.RollBack(); }",
            "Section(\"read\", () => Log($\"{walls.Count} walls\"));",
            "var total = 0; void Count(int n) { total += n; } Count(walls.Count);",
            "return new { walls = walls.Count, total };");
        var bytes = Compile(code, readOnly: true, out var errors, out var violations);
        Assert.True(violations.Length == 0, string.Join("\n", violations));
        Assert.True(bytes != null, string.Join("\n", errors));
    }

    [RequiresRevitDbApiFact]
    public void TheSameScriptIsAcceptedOutsideReadonly()
    {
        // The guard is for the mode that bypasses the lock; auto/group/none keep their freedom.
        var bytes = Compile("Func<bool, bool> close = document.Close;\nreturn 1;", readOnly: false,
            out var errors, out var violations);
        Assert.Empty(violations);
        Assert.True(bytes != null, string.Join("\n", errors));
    }

    // ── text filter: interpolation holes are code ────────────────────────────

    [Theory]
    [InlineData("var s = $\"{System.IO.File.ReadAllText(p)}\";")]
    [InlineData("var s = $@\"{System.IO.File.ReadAllText(p)}\";")]
    [InlineData("var s = @$\"x{System.IO.File.ReadAllText(p)}y\";")]
    [InlineData("var s = $\"\"\"{System.IO.Path.GetTempPath()}\"\"\";")]
    [InlineData("var s = $\"a{(flag ? \"b\" : System.IO.File.ReadAllText(p))}\";")]
    public void ForbiddenCodeInAHoleIsBlocked(string code)
    {
        Assert.NotNull(CodeSandboxV2.Validate(code));
    }

    [Theory]
    [InlineData("var s = $\"{{System.IO}} is only text\";")]
    [InlineData("var s = \"\"\"System.IO.File\"\"\";")]
    [InlineData("var s = @\"System.IO.File \"\" quoted\";")]
    [InlineData("var s = $\"{(c == '}' ? 1 : 2)} ok\";")]
    public void TextThatOnlyLooksLikeCodeStaysText(string code)
    {
        Assert.Null(CodeSandboxV2.Validate(code));
    }

    [Fact]
    public void ReadonlyTextCheckSeesAHole()
    {
        Assert.NotNull(CodeSandboxV2.ValidateReadOnlyScript("var s = $\"{document.Export(a, b, c)}\";"));
    }

    [Theory]
    [InlineData("var s = $\"a{x}b{{c}}\" + @\"d\"\"e\"; // f\n/* g */ var t = 'h';")]
    [InlineData("var s = $\"\"\"x{y}z\"\"\"; var u = $@\"{(q ? \"r\" : \"s\")}\";")]
    public void StrippingKeepsLengthAndLines(string code)
    {
        var stripped = CodeSandboxV2.StripCommentsAndStrings(code);
        Assert.Equal(code.Length, stripped.Length);
        Assert.Equal(code.Split('\n').Length, stripped.Split('\n').Length);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string[] Violations(string code)
    {
        Compile(code, readOnly: true, out _, out var violations);
        return violations;
    }

    private static byte[]? Compile(string code, bool readOnly, out string[] errors, out string[] violations)
    {
        var wrapped = RoslynExecutor.WrapCode(code, out var prefix);
        return RoslynCompilerWorker.Compile(wrapped, References().ToArray(), prefix, readOnly,
            out errors, out _, out violations);
    }

    private static IEnumerable<string> References()
    {
        foreach (var path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "").Split(Path.PathSeparator))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || name is "netstandard.dll" or "Microsoft.CSharp.dll")
                yield return path;
        }
        var revitApi = typeof(Autodesk.Revit.DB.Document).Assembly.Location;
        yield return revitApi;
        yield return Path.Combine(Path.GetDirectoryName(revitApi)!, "RevitAPIUI.dll");
        yield return typeof(JObject).Assembly.Location;
        yield return typeof(ScriptApi).Assembly.Location;
    }
}
