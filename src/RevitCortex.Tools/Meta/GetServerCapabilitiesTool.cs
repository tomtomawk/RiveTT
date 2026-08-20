using System.Linq;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Hosting;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;

namespace RevitCortex.Tools.Meta;

[ToolSafety(true, false)]
public sealed class GetServerCapabilitiesTool : ICortexTool
{
    public string Name => "get_server_capabilities";
    public string Category => "Meta";
    public bool RequiresDocument => false;
    public bool IsDynamic => false;
    public string Description => "Report the effective MCPRVTT27 execution, safety, response, and document capability contract.";

    public CortexResult<object> Execute(JObject input, CortexSession session)
    {
        var caps = session.Capabilities;
        return CortexResult<object>.Ok(new
        {
            connector = "MCPRVTT27",
            revitVersion = 2027,
            runtime = ".NET 10 / Windows x64",
            transport = "named_pipe_current_user",
            executionMode = "automatic",
            confirmationRequired = false,
            dryRunDefault = true,
            auditLogPath = CortexEnvironment.Current.AuditLogPath,
            responseModes = new[] { "summary", "idsOnly", "details" },
            selectionScopes = new[]
            {
                "elementIds", "selectionToken", "savedSelectionName",
                "selection", "last_filter", "active_view", "whole_model"
            },
            document = new
            {
                locale = session.DetectedLocale,
                hasWorksets = caps.HasWorksets,
                hasPhases = caps.HasPhases,
                hasDesignOptions = caps.HasDesignOptions,
                hasLinkedModels = caps.HasLinkedModels,
                enabledDynamicTools = caps.EnabledTools.OrderBy(name => name).ToArray()
            },
            lifecycleLimitations = new[]
            {
                "open_document is not exposed because UIApplication.OpenAndActivateDocument cannot run inside an API event handler.",
                "edit_family is not exposed through the ExternalEvent dispatcher; modal document lifecycle needs a dedicated orchestrator."
            }
        });
    }
}
