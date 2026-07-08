using Newtonsoft.Json.Linq;
using RevitCortex.Core.Licensing;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Plugin;
using RevitCortex.Plugin.Licensing;
using Xunit;

namespace RevitCortex.Tests.Router;

public class CortexRouterLicenseGateTests
{
    // Real injection pattern (mirrors CortexRouterExceptionTests): reach into the private
    // _tools dictionary and register a FakeTool directly.
    private static void AddTool(CortexRouter router, FakeTool tool)
    {
        var field = typeof(CortexRouter).GetField("_tools",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var tools = (System.Collections.Generic.Dictionary<string, RevitCortex.Core.Tools.ICortexTool>)
            field.GetValue(router)!;
        tools[tool.Name] = tool;
    }

    private static CortexRouter Router(LicenseGate? gate)
    {
        var session = new CortexSession(new SessionStore());
        return new CortexRouter(session, new FakeAnalyzer(),
            auditLogger: null, errorReporter: null, licenseGate: gate);
    }

    [Fact]
    public void Route_ExpiredLicense_BlocksWriteTool_WithPermissionDenied()
    {
        var router = Router(new LicenseGate(() => LicenseState.Expired, isDev: false));
        AddTool(router, new FakeTool { Name = "delete_element" });

        var result = router.Route("delete_element", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
        // Locale-independent: the localized block message interpolates the tool name ({0}),
        // which is identical in every language. (Asserting on the word "license" was locale-
        // dependent — the Italian message reads "Licenza…", breaking the substring on IT hosts.)
        Assert.Contains("delete_element", result.Error.Message, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Route_ExpiredLicense_AllowsReadOnlyTool()
    {
        var router = Router(new LicenseGate(() => LicenseState.Expired, isDev: false));
        AddTool(router, new FakeTool { Name = "get_element_parameters" });

        Assert.True(router.Route("get_element_parameters", new JObject()).Success);
    }

    [Fact]
    public void Route_InvalidLicense_BlocksWriteTool()
    {
        var router = Router(new LicenseGate(() => LicenseState.Invalid, isDev: false));
        AddTool(router, new FakeTool { Name = "delete_element" });

        var result = router.Route("delete_element", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public void Route_ActiveLicense_AllowsWriteTool()
    {
        var router = Router(new LicenseGate(() => LicenseState.Active, isDev: false));
        AddTool(router, new FakeTool { Name = "delete_element" });

        Assert.True(router.Route("delete_element", new JObject()).Success);
    }

    [Fact]
    public void Route_NullGate_BehaviorUnchanged_WriteToolPasses()
    {
        var router = Router(gate: null);
        AddTool(router, new FakeTool { Name = "delete_element" });

        Assert.True(router.Route("delete_element", new JObject()).Success);
    }
}
