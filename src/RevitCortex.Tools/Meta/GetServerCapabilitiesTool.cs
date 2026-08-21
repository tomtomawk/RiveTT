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
            // There is no read-only mode in MCPRVTT27. Per-response,
            // execution.toolReadOnly classifies the tool that answered; it is not a
            // session state. The old field name was "readOnly", which read as a
            // server-wide lock and led to writes being believed impossible.
            writesAllowed = true,
            readOnlyModeExists = false,
            executionFields = new
            {
                toolReadOnly = "classification of the tool that produced this response",
                toolDestructive = "the tool can delete or overwrite model data",
                writesAllowed = "session-wide: always true, MCPRVTT27 has no read-only mode",
                cached = "the response was served from the tool result cache"
            },
            dryRunDefault = true,
            unitPolicy = new
            {
                inputs = "lengths in mm, angles in degrees",
                outputs = "project display units, with an explicit unit and the Revit internal value " +
                          "(internalValue, in ft/ft2/ft3) on numeric parameters",
                elevation = "create_wall ignores locationLine.z (baseLevelId governs); " +
                            "create_door/create_window locationPoint.z is an absolute project elevation " +
                            "unless zMode=relativeToLevel"
            },
            parameterNames = new
            {
                localization = "parameter and schedule-field names resolve in English or in the document " +
                               "language (Mark/Repere, Level/Niveau, Width/Largeur, Type Name/Nom du type)",
                unresolved = "reported in unresolvedParameterNames with suggestions, never as an empty column"
            },
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
            // Stated limitations save an agent a fruitless search of the whole tool
            // catalogue. Everything listed here was looked for, and not found, during
            // real sessions.
            lifecycleLimitations = new[]
            {
                "open_document is not exposed because UIApplication.OpenAndActivateDocument cannot run inside an API event handler.",
                "edit_family is not exposed through the ExternalEvent dispatcher; modal document lifecycle needs a dedicated orchestrator.",
                "There is NO tool to create a blank project from a template. save_as_document DUPLICATES the open document, with everything already in it.",
                "There is no stair tool: Revit builds a standard stair through a modal sketch editor (StairsEditScope), which cannot be driven from a non-modal ExternalEvent.",
                "Rebar propagation is not exposed by the Revit API on any supported version; propagate_rebar only reports that."
            },
            discoveryHints = new[]
            {
                "System types (walls, floors, ceilings, roofs, railings, stairs, title blocks) are NOT loadable families: enumerate them with list_system_types, duplicate them with duplicate_system_type.",
                "There is no 'create similar' tool: copy_elements with an offset re-hosts the copy and does the same job, including across levels (level constraints are recomputed).",
                "To split a room without a physical wall, use create_room_separation_line.",
                "Category labels are localized and sometimes ambiguous (French Revit names the viewport category 'Fenetres ', like windows): prefer the OST_* codes returned as categoryBic."
            }
        });
    }
}
