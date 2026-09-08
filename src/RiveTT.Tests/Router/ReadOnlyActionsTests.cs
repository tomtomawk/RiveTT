using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RiveTT.Core.Results;
using RiveTT.Core.Security;
using RiveTT.Core.Session;
using RiveTT.Core.Tools;
using RiveTT.Plugin;
using RiveTT.Tools.Project;
using RiveTT.Tools.Views;
using Xunit;

namespace RiveTT.Tests.Router;

public class ReadOnlyActionsTests
{
    [ReadOnlyActions("list", "list", "get")]
    [ToolSafety(false, true)]
    private sealed class MixedTool : FakeTool { }

    [ToolSafety(true, false, supportsDryRun: true)]
    private sealed class NavigationTool : FakeTool { }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"action\":\"LIST\"}", true)]
    [InlineData("{\"action\":null}", true)]
    [InlineData("{\"action\":\"get\"}", true)]
    [InlineData("{\"action\":\"delete\"}", false)]
    [InlineData("{\"action\":\"list_and_delete\"}", false)]
    [InlineData("{\"action\":123}", false)]
    [InlineData("{\"action\":{\"value\":\"list\"}}", false)]
    [InlineData("{\"action\":\"list\",\"data\":{\"action\":\"delete\"}}", false)]
    public void LockedRouterAllowsOnlyDeclaredReadBranches(string json, bool allowed)
    {
        var session = new RiveTTSession(new SessionStore());
        session.WriteAccess.Set(false, "test");
        var audit = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var router = new RiveTTRouter(session, new FakeAnalyzer(), new AuditLogger(audit));
            router.RegisterTool(new MixedTool { Name = "manage_mixed" });
            var result = router.Route("manage_mixed", JObject.Parse(json));
            Assert.Equal(allowed, result.Success);
            if (!allowed) Assert.Equal(RiveTTErrorCode.PermissionDenied, result.Error!.Code);
            else
            {
                var execution = JObject.FromObject(result.Data!)["execution"]!;
                Assert.True(execution["toolReadOnly"]!.Value<bool>());
                Assert.False(execution["toolDestructive"]!.Value<bool>());
            }
            Assert.False(session.WriteAccess.WritesAllowed);
        }
        finally { File.Delete(audit); }
    }

    [Theory]
    [InlineData("open_file")]
    [InlineData("open_document")]
    [InlineData("open_family")]
    [InlineData("open_template")]
    [InlineData("activate_view")]
    public void NavigationInvalidatesCachesButNeverUnlocksTheSession(string name)
    {
        var session = new RiveTTSession(new SessionStore());
        session.WriteAccess.Set(false, "test");
        var audit = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var router = new RiveTTRouter(session, new FakeAnalyzer(), new AuditLogger(audit));
            router.RegisterTool(new NavigationTool { Name = name });
            var version = session.DocumentVersion;
            Assert.True(router.Route(name, new JObject { ["dryRun"] = true }).Success);
            Assert.Equal(version, session.DocumentVersion);
            Assert.True(router.Route(name, new JObject { ["dryRun"] = false }).Success);
            Assert.True(session.DocumentVersion > version);
            Assert.False(session.WriteAccess.WritesAllowed);
        }
        finally { File.Delete(audit); }
    }

    [Fact]
    public void RouterResponseEnrichmentDoesNotRequireRevitApiForAnEmptySession()
    {
        var source = File.ReadAllText(RepositoryFile.Path("src", "RiveTT.Plugin", "RiveTTRouter.cs"));
        var start = source.IndexOf("private string GetActiveRevitVersion()", StringComparison.Ordinal);
        var end = source.IndexOf("private string GetActiveDocumentTitle()", start, StringComparison.Ordinal);
        Assert.DoesNotContain("Autodesk.Revit.DB.Document", source[start..end]);
    }

    [Fact]
    public void ActualNavigationToolsAreDeclaredReadOnlyAndNeverCached()
    {
        foreach (var type in new[] { typeof(OpenFileTool), typeof(OpenDocumentTool), typeof(OpenFamilyTool), typeof(OpenTemplateTool), typeof(ActivateViewTool) })
        {
            var safety = type.GetCustomAttribute<ToolSafetyAttribute>()!;
            Assert.True(safety.ReadOnly);
            Assert.True(safety.SupportsDryRun);
            Assert.DoesNotContain(type.GetInterfaces(), i => i.Name == "ICacheableTool");
        }
    }

    [Fact]
    public void SelectionEnvelopeCannotMaskANestedWrite()
    {
        var policy = new ReadOnlyActionsAttribute("", "select") { UseDataEnvelope = true };
        Assert.True(policy.Matches(JObject.Parse("{\"data\":{\"action\":\"select\"}}")));
        Assert.False(policy.Matches(JObject.Parse("{\"action\":\"select\",\"data\":{\"action\":\"hide\"}}")));
        Assert.True(policy.Matches(JObject.Parse("{\"action\":\"hide\",\"data\":{\"action\":\"select\"}}")));
        Assert.False(policy.Matches(JObject.Parse("{\"data\":{}}")));
    }
}
